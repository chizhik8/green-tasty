using System.Text.RegularExpressions;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Repositories.GroupReservations;
using Moq;

namespace APIHandler.Tests.Repositories;

// Submit transaction of the group pre-order (S25, AC18): docs/specs/2026-10-31-group-preorder-owner-endpoints.md
public class GroupReservationRepositoryTests
{
    private const string GroupId = "group-1";
    private const string OrdersTableVariable = "ordersTable";
    private const string GroupReservationsTableVariable = "groupReservationsTable";
    private const string ConditionalCheckFailedCode = "ConditionalCheckFailed";

    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly Mock<IAmazonDynamoDB> _mockDynamoDbClient = new();
    private readonly GroupReservationRepository _repository;

    private TransactWriteItemsRequest? _sentRequest;

    public GroupReservationRepositoryTests()
    {
        // The repository reads the table names from the environment.
        if (Environment.GetEnvironmentVariable(OrdersTableVariable) is null)
            Environment.SetEnvironmentVariable(OrdersTableVariable, "Orders-test");
        if (Environment.GetEnvironmentVariable(GroupReservationsTableVariable) is null)
            Environment.SetEnvironmentVariable(GroupReservationsTableVariable, "GroupReservations-test");

        _repository = new GroupReservationRepository(_mockContext.Object, _mockDynamoDbClient.Object);
    }

    private static GroupReservation BuildGroup(GroupReservationStatus status) => new()
    {
        GroupReservationId = GroupId,
        ReservationId = "reservation-1",
        ReservationToken = "invite-token-1",
        Status = status,
    };

    private static Order BuildOrder() => new()
    {
        OrderId = "order-1",
        CustomerId = "owner-user-1",
        ReservationId = "reservation-1",
        ReservationAddress = "Main street 1, Table 7",
        ReservationTime = "Oct 31, 2026, 7:00 p.m. - 8:30 p.m.",
        Dishes = [new OrderItem { DishId = "dish1", Quantity = 2, State = OrderItemState.Pending }],
        Status = OrderStatus.SubmittedByCustomer,
    };

    // Stand-in for the context's conversion of the group status. The representation is deliberately unlike any
    // hand-written value, so the tests show that the condition and the new value come from the context's conversion
    // (spec: the condition value uses the representation in which DynamoDBContext stores GroupReservationStatus).
    private static string ConvertedStatus(GroupReservationStatus status) => $"converted-status-{status}";

    private void SetupConversions(Order order)
    {
        _mockContext
            .Setup(c => c.ToDocument(order, It.IsAny<ToDocumentConfig>()))
            .Returns(new Document
            {
                ["orderId"] = order.OrderId,
                ["customerId"] = order.CustomerId,
                ["status"] = "converted-order-status",
            });

        _mockContext
            .Setup(c => c.ToDocument(It.IsAny<GroupReservation>(), It.IsAny<ToDocumentConfig>()))
            .Returns((GroupReservation group, ToDocumentConfig _) => new Document
            {
                ["groupReservationId"] = group.GroupReservationId,
                ["reservationId"] = group.ReservationId,
                ["reservationToken"] = group.ReservationToken,
                ["status"] = ConvertedStatus(group.Status),
            });
    }

    private void SetupTransactionSucceeds()
    {
        _mockDynamoDbClient
            .Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((request, _) => _sentRequest = request)
            .ReturnsAsync(new TransactWriteItemsResponse());
    }

    // DynamoDB reports one cancellation reason per transaction item, in item order; "None" for items that did not fail.
    private void SetupTransactionCanceled(string groupItemReasonCode)
    {
        _mockDynamoDbClient
            .Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Returns((TransactWriteItemsRequest request, CancellationToken _) =>
                Task.FromException<TransactWriteItemsResponse>(new TransactionCanceledException("Transaction cancelled")
                {
                    CancellationReasons = request.TransactItems
                        .Select(item => new CancellationReason { Code = IsGroupUpdate(item) ? groupItemReasonCode : "None" })
                        .ToList(),
                }));
    }

    private static bool IsGroupUpdate(TransactWriteItem item) =>
        item.Update?.TableName == ResourceNames.GroupReservationsTableName;

    private static string ResolveNames(string expression, Dictionary<string, string>? names) =>
        (names ?? new Dictionary<string, string>())
            .Aggregate(expression, (resolved, name) => resolved.Replace(name.Key, name.Value));

    [Fact]
    public async Task TryUpdateStatusWithOrderAsync_WritesOrderPutAndConditionalGroupUpdateInOneTransactionAndReturnsTrue()
    {
        Order order = BuildOrder();
        SetupConversions(order);
        SetupTransactionSucceeds();

        bool written = await _repository.TryUpdateStatusWithOrderAsync(BuildGroup(GroupReservationStatus.Submitted), GroupReservationStatus.Open, order);

        Assert.True(written);
        _mockDynamoDbClient.Verify(
            c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockDynamoDbClient.VerifyNoOtherCalls();

        Assert.NotNull(_sentRequest);
        List<TransactWriteItem> items = _sentRequest.TransactItems;
        Assert.Equal(2, items.Count);
        // No ConditionCheck item: the IAM policy does not grant dynamodb:ConditionCheckItem.
        Assert.All(items, item =>
        {
            Assert.Null(item.ConditionCheck);
            Assert.Null(item.Delete);
        });

        // Put of the whole order, converted by the context for the orders table, as SaveOrderAsync writes it.
        Put put = Assert.Single(items, item => item.Put is not null).Put;
        Assert.Equal(ResourceNames.OrdersTableName, put.TableName);
        Assert.Equal("order-1", put.Item["orderId"].S);
        Assert.Equal("owner-user-1", put.Item["customerId"].S);
        Assert.Equal("converted-order-status", put.Item["status"].S);
        _mockContext.Verify(
            c => c.ToDocument(order, It.Is<ToDocumentConfig>(t => t.OverrideTableName == ResourceNames.OrdersTableName)),
            Times.Once);

        // Update of the group: SET status = Submitted, on the condition status = Open.
        Update update = Assert.Single(items, item => item.Update is not null).Update;
        Assert.Equal(ResourceNames.GroupReservationsTableName, update.TableName);
        KeyValuePair<string, AttributeValue> key = Assert.Single(update.Key);
        Assert.Equal("groupReservationId", key.Key);
        Assert.Equal(GroupId, key.Value.S);

        Match condition = Regex.Match(
            ResolveNames(update.ConditionExpression, update.ExpressionAttributeNames),
            @"^\s*status\s*=\s*(:\w+)\s*$");
        Assert.True(condition.Success, $"Unexpected condition expression: '{update.ConditionExpression}'.");
        Assert.Equal(ConvertedStatus(GroupReservationStatus.Open), update.ExpressionAttributeValues[condition.Groups[1].Value].S);

        Match set = Regex.Match(
            ResolveNames(update.UpdateExpression, update.ExpressionAttributeNames),
            @"^\s*SET\s+status\s*=\s*(:\w+)\s*$",
            RegexOptions.IgnoreCase);
        Assert.True(set.Success, $"Unexpected update expression: '{update.UpdateExpression}'.");
        Assert.Equal(ConvertedStatus(GroupReservationStatus.Submitted), update.ExpressionAttributeValues[set.Groups[1].Value].S);
    }

    [Fact]
    public async Task TryUpdateStatusWithOrderAsync_TransactionCanceledByGroupCondition_ReturnsFalse()
    {
        Order order = BuildOrder();
        SetupConversions(order);
        SetupTransactionCanceled(ConditionalCheckFailedCode);

        bool written = await _repository.TryUpdateStatusWithOrderAsync(BuildGroup(GroupReservationStatus.Submitted), GroupReservationStatus.Open, order);

        Assert.False(written);
        // One attempt, no retry and no other write.
        _mockDynamoDbClient.Verify(
            c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockDynamoDbClient.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("TransactionConflict")]
    [InlineData("ValidationError")]
    public async Task TryUpdateStatusWithOrderAsync_TransactionCanceledForAnotherReason_Throws(string groupItemReasonCode)
    {
        Order order = BuildOrder();
        SetupConversions(order);
        SetupTransactionCanceled(groupItemReasonCode);

        await Assert.ThrowsAsync<TransactionCanceledException>(() =>
            _repository.TryUpdateStatusWithOrderAsync(BuildGroup(GroupReservationStatus.Submitted), GroupReservationStatus.Open, order));
    }

    [Fact]
    public async Task TryUpdateStatusWithOrderAsync_OtherDynamoDbFailure_Throws()
    {
        Order order = BuildOrder();
        SetupConversions(order);
        _mockDynamoDbClient
            .Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("Service unavailable"));

        await Assert.ThrowsAsync<AmazonDynamoDBException>(() =>
            _repository.TryUpdateStatusWithOrderAsync(BuildGroup(GroupReservationStatus.Submitted), GroupReservationStatus.Open, order));
    }
}
