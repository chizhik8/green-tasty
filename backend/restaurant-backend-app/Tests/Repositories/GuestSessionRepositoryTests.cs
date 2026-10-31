using Amazon.DynamoDBv2.DataModel;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Repositories.GuestSessions;
using Moq;

namespace APIHandler.Tests.Repositories;

public class GuestSessionRepositoryTests
{
    private const string GroupId = "group-1";
    private const string GuestSessionTableVariable = "guestSessionTable";

    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly GuestSessionRepository _repository;

    public GuestSessionRepositoryTests()
    {
        // The repository reads the table name from the environment when it builds the query config.
        if (Environment.GetEnvironmentVariable(GuestSessionTableVariable) is null)
            Environment.SetEnvironmentVariable(GuestSessionTableVariable, "GuestSessions-test");

        _repository = new GuestSessionRepository(_mockContext.Object);
    }

    private static GuestSession BuildSession(string id, bool isOwner) => new()
    {
        Id = id,
        GuestName = isOwner ? "Owner" : $"Guest {id}",
        GroupReservationId = GroupId,
        IsOwnerSession = isOwner,
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
}
