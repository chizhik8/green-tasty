using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupReservations;

public class GroupReservationRepository : IGroupReservationRepository
{
    private const string GroupReservationIdAttributeName = "groupReservationId";
    private const string StatusAttributeName = "status";
    private const string ConditionalCheckFailedCode = "ConditionalCheckFailed";

    // Position of the group update in the transaction (after the order put); DynamoDB reports
    // the cancellation reasons in the same order as the transaction items.
    private const int GroupUpdateIndex = 1;

    private static readonly string GroupReservationsTableName = ResourceNames.GroupReservationsTableName;
    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public GroupReservationRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<GroupReservation?> GetGroupReservationByIdAsync(string groupReservationId)
    {
        return await _dynamoDbContext.LoadAsync<GroupReservation>(groupReservationId, DynamoConfig.LoadConfig(GroupReservationsTableName));
    }

    public async Task DeleteGroupReservationAsync(GroupReservation groupReservation)
    {
        await _dynamoDbContext.DeleteAsync(groupReservation, DynamoConfig.DeleteConfig(GroupReservationsTableName));
    }

    public async Task SaveGroupReservationAsync(GroupReservation groupReservation)
    {
        await _dynamoDbContext.SaveAsync(groupReservation, DynamoConfig.SaveConfig(GroupReservationsTableName));
    }

    public async Task<GroupReservation?> GetGroupReservationByTokenAsync(string token)
    {
        var results = await _dynamoDbContext.QueryAsync<GroupReservation?>(token, new QueryConfig
        {
            OverrideTableName = GroupReservationsTableName,
            IndexName = GlobalSecondaryIndexes.GroupReservation.ReservationToken,
        }).GetRemainingAsync();

        return results.FirstOrDefault();
    }

    public async Task<GroupReservation?> GetByReservationIdAsync(string reservationId)
    {
        var results = await _dynamoDbContext.QueryAsync<GroupReservation?>(reservationId, new QueryConfig
        {
            OverrideTableName = GroupReservationsTableName,
            IndexName = GlobalSecondaryIndexes.GroupReservation.ReservationId,
        }).GetRemainingAsync();

        return results.FirstOrDefault();
    }

    public async Task<bool> TryUpdateStatusWithOrderAsync(GroupReservation groupReservation, GroupReservationStatus expectedCurrentStatus, Order order)
    {
        string ordersTableName = ResourceNames.OrdersTableName;

        // The order is converted by the context, so the item has the same attribute names and value
        // representation as IOrderRepository.SaveOrderAsync writes. Null properties are left out.
        Dictionary<string, AttributeValue> orderItem = _dynamoDbContext.ToAttributeMap(order, ordersTableName);

        // Item order matters: the cancellation reasons are matched by GroupUpdateIndex.
        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = ordersTableName,
                        Item = orderItem,
                    },
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = GroupReservationsTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            [GroupReservationIdAttributeName] = new AttributeValue { S = groupReservation.GroupReservationId },
                        },
                        UpdateExpression = "SET #status = :newStatus",
                        ConditionExpression = "#status = :expectedStatus",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#status"] = StatusAttributeName,
                        },
                        // Converted by the context: GroupReservation.Status has no property converter, so the stored
                        // representation is the context's default enum conversion, and the condition must use the same one.
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":newStatus"] = ToStatusAttributeValue(groupReservation, groupReservation.Status),
                            [":expectedStatus"] = ToStatusAttributeValue(groupReservation, expectedCurrentStatus),
                        },
                    },
                },
            ],
        };

        try
        {
            await _dynamoDbClient.TransactWriteItemsAsync(request);
            return true;
        }
        catch (TransactionCanceledException ex) when (IsConditionalCheckFailed(ex, GroupUpdateIndex))
        {
            // The whole transaction was canceled, so neither the order nor the group status was written.
            return false;
        }
    }

    private AttributeValue ToStatusAttributeValue(GroupReservation groupReservation, GroupReservationStatus status)
    {
        var withStatus = new GroupReservation
        {
            GroupReservationId = groupReservation.GroupReservationId,
            ReservationId = groupReservation.ReservationId,
            ReservationToken = groupReservation.ReservationToken,
            Status = status,
        };

        return _dynamoDbContext.ToAttributeMap(withStatus, GroupReservationsTableName)[StatusAttributeName];
    }

    private static bool IsConditionalCheckFailed(TransactionCanceledException exception, int itemIndex) =>
        exception.CancellationReasons is { } reasons
        && reasons.Count > itemIndex
        && reasons[itemIndex]?.Code == ConditionalCheckFailedCode;
}
