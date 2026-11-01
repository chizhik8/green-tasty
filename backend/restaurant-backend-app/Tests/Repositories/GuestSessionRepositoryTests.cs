using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Repositories.GuestSessions;
using Moq;

namespace APIHandler.Tests.Repositories;

public class GuestSessionRepositoryTests
{
    private const string GroupId = "group-1";
    private const string OwnerSessionId = "owner-" + GroupId;
    private const string GuestSessionTableVariable = "guestSessionTable";

    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly Mock<IAmazonDynamoDB> _mockDynamoDbClient = new();
    private readonly GuestSessionRepository _repository;

    public GuestSessionRepositoryTests()
    {
        // The repository reads the table name from the environment when it builds the query config.
        if (Environment.GetEnvironmentVariable(GuestSessionTableVariable) is null)
            Environment.SetEnvironmentVariable(GuestSessionTableVariable, "GuestSessions-test");

        _repository = new GuestSessionRepository(_mockContext.Object, _mockDynamoDbClient.Object);
    }

    private static GuestSession BuildSession(string id, bool isOwner) => new()
    {
        Id = id,
        GuestName = isOwner ? "Owner" : $"Guest {id}",
        GroupReservationId = GroupId,
        IsOwnerSession = isOwner,
        OwnerUserId = isOwner ? "owner-user-1" : null,
        Dishes = [],
    };

    private void SetupGroupQueryResult(List<GuestSession> sessions)
    {
        var search = new Mock<IAsyncSearch<GuestSession>>();
        search
            .Setup(s => s.GetRemainingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        _mockContext
            .Setup(c => c.QueryAsync<GuestSession>(GroupId, It.IsAny<QueryConfig>()))
            .Returns(search.Object);
    }

    // Stands in for the context's entity conversion; the values are only markers that let the tests
    // check that the request item is the context's conversion of the session.
    private void SetupSessionConversion(GuestSession session)
    {
        _mockContext
            .Setup(c => c.ToDocument(session, It.IsAny<ToDocumentConfig>()))
            .Returns(new Document
            {
                ["guestSessionId"] = session.Id,
                ["groupReservationId"] = session.GroupReservationId,
                ["isOwnerSession"] = session.IsOwnerSession ? "converted-true" : "converted-false",
            });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    public async Task CountJoinedGuestsByGroupReservationIdAsync_GuestAndOwnerSessions_CountsGuestSessionsOnly(int guestSessions, int ownerSessions)
    {
        List<GuestSession> sessions =
        [
            .. Enumerable.Range(0, guestSessions).Select(i => BuildSession($"guest-{i}", isOwner: false)),
            .. Enumerable.Range(0, ownerSessions).Select(i => BuildSession($"owner-{i}", isOwner: true)),
        ];
        SetupGroupQueryResult(sessions);

        int result = await _repository.CountJoinedGuestsByGroupReservationIdAsync(GroupId);

        Assert.Equal(guestSessions, result);
    }

    [Fact]
    public async Task CountJoinedGuestsByGroupReservationIdAsync_QueriesGroupReservationIdIndex()
    {
        SetupGroupQueryResult([BuildSession("guest-1", isOwner: false)]);

        await _repository.CountJoinedGuestsByGroupReservationIdAsync(GroupId);

        _mockContext.Verify(
            c => c.QueryAsync<GuestSession>(
                GroupId,
                It.Is<QueryConfig>(q => q.IndexName == GlobalSecondaryIndexes.GuestSession.GroupReservationId)),
            Times.Once);
    }

    // ---- Owner session lookup and conditional create (S25, AC20) ----

    [Fact]
    public async Task GetByGuestSessionIdConsistentAsync_LoadsSessionByIdWithConsistentRead()
    {
        GuestSession stored = BuildSession(OwnerSessionId, isOwner: true);
        _mockContext
            .Setup(c => c.LoadAsync<GuestSession?>(
                OwnerSessionId,
                It.Is<LoadConfig>(l => l.ConsistentRead == true && l.OverrideTableName == ResourceNames.GuestSessionTableName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        GuestSession? result = await _repository.GetByGuestSessionIdConsistentAsync(OwnerSessionId);

        Assert.Same(stored, result);
        // A lookup by key, not through the groupReservationId index.
        _mockContext.Verify(c => c.QueryAsync<GuestSession>(It.IsAny<object>(), It.IsAny<QueryConfig>()), Times.Never);
    }

    [Fact]
    public async Task TryCreateAsync_NoSessionWithThisId_PutsConvertedItemWithAttributeNotExistsConditionAndReturnsTrue()
    {
        GuestSession session = BuildSession(OwnerSessionId, isOwner: true);
        SetupSessionConversion(session);

        PutItemRequest? sentRequest = null;
        _mockDynamoDbClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((request, _) => sentRequest = request)
            .ReturnsAsync(new PutItemResponse());

        bool created = await _repository.TryCreateAsync(session);

        Assert.True(created);
        _mockDynamoDbClient.Verify(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(sentRequest);
        Assert.Equal(ResourceNames.GuestSessionTableName, sentRequest.TableName);
        Assert.Equal("attribute_not_exists(guestSessionId)", sentRequest.ConditionExpression);
        Assert.Equal(OwnerSessionId, sentRequest.Item["guestSessionId"].S);
        Assert.Equal(GroupId, sentRequest.Item["groupReservationId"].S);
        Assert.Equal("converted-true", sentRequest.Item["isOwnerSession"].S);
        _mockContext.Verify(
            c => c.ToDocument(session, It.Is<ToDocumentConfig>(t => t.OverrideTableName == ResourceNames.GuestSessionTableName)),
            Times.Once);
    }

    [Fact]
    public async Task TryCreateAsync_ConditionalCheckFailed_ReturnsFalse()
    {
        GuestSession session = BuildSession(OwnerSessionId, isOwner: true);
        SetupSessionConversion(session);
        _mockDynamoDbClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("The conditional request failed"));

        bool created = await _repository.TryCreateAsync(session);

        Assert.False(created);
        // The conditional put was attempted once; the failure is not retried as another write.
        _mockDynamoDbClient.Verify(
            c => c.PutItemAsync(It.Is<PutItemRequest>(p => p.ConditionExpression == "attribute_not_exists(guestSessionId)"), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockDynamoDbClient.VerifyNoOtherCalls();
    }
}
