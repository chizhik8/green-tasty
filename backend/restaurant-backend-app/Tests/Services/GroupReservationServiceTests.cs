using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupReservationServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepositoryMock = new();
    private readonly Mock<ITableRepository> _tableRepositoryMock = new();
    private readonly Mock<IReservationRepository> _reservationRepositoryMock = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();
    private readonly Mock<IDishRepository> _dishRepositoryMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();

    private readonly GroupReservationService _service;

    public GroupReservationServiceTests()
    {
        _service = new GroupReservationService(
            _groupReservationRepositoryMock.Object,
            _tableRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _userServiceMock.Object,
            _locationServiceMock.Object,
            _dishRepositoryMock.Object,
            _orderRepositoryMock.Object);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_ValidInput_ReturnsGroupReservationInfo(
        GroupReservation groupReservation,
        Table table,
        Reservation reservation,
        Location location,
        string expectedUserName)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(0);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);

        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);

        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync(expectedUserName);

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
        Assert.Equal(table.LocationId, result.LocationId);
        Assert.Equal(reservation.GuestCount, result.MaxGuestCount);
        Assert.Equal(reservation.StartTime, result.StartTime);
        Assert.Equal(reservation.EndTime, result.EndTime);
        Assert.Equal(table.TableNumber, result.TableNumber);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal(location.Address, result.LocationAddress);
        Assert.Equal(expectedUserName, result.InviterName);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_InvalidToken_ThrowsNotFound(string token)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(token))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetGroupReservationByTokenAsync(token));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsNotOpen_ThrowsException(
        GroupReservationStatus status,
        GroupReservation groupReservation)
    {
        groupReservation.Status = status;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_WhenGuestCountIsMax_ThrowsGuestLimitExceeded(GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(reservation.GuestCount);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_ValidInput_UpdatesGroupReservationStatus(GroupReservation groupReservation)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(groupReservation);

        GroupReservation? savedGroupReservation = null;
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(r => savedGroupReservation = r);

        await _service.CloseGroupReservationAsync(groupReservation.ReservationId);

        Assert.NotNull(savedGroupReservation);
        Assert.Equal(GroupReservationStatus.Closed, savedGroupReservation.Status);
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_GroupReservationNotFound_DoesNotThrowException(string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservationId))
            .ReturnsAsync((GroupReservation?)null);

        await _service.CloseGroupReservationAsync(reservationId);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_ValidInput_ReturnsToken(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_InvalidReservationId_ThrowsNotFound(string reservationId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_OneGuest_ThrowsValidationException(Reservation reservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    [InlineAutoData(ReservationStatus.InProgress)]
    public async Task GetGroupReservationInviteTokenAsync_StatusIsNotReserved_ThrowsValidationException(ReservationStatus reservationStatus, Reservation reservation)
    {
        reservation.Status = reservationStatus;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    public async Task GetGroupReservationInviteTokenAsync_GroupStatusIsNotOpen_ThrowsException(GroupReservationStatus status, Type type, Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = status;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(type, () => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    // Group pre-order owner endpoints: docs/specs/2026-10-31-group-preorder-owner-endpoints.md

    private const string OwnerUserId = "owner-user-1";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";

    private static GroupReservation BuildGroup(GroupReservationStatus status = GroupReservationStatus.Open) => new()
    {
        GroupReservationId = GroupId,
        ReservationId = ReservationId,
        ReservationToken = "invite-token-1",
        Status = status,
    };

    private static Reservation BuildReservation(
        ReservationStatus status = ReservationStatus.Reserved,
        string? customerId = OwnerUserId,
        int guestCount = 4,
        DateTime? startTime = null) => new()
    {
        Id = ReservationId,
        TableId = "table-1",
        WaiterId = "waiter-1",
        CustomerId = customerId,
        GuestCount = guestCount,
        Status = status,
        // Far before the 30-minute pre-order cutoff (AC19) unless a test sets the start time.
        StartTime = startTime ?? DateTime.UtcNow.AddDays(1),
    };

    private static GuestSession BuildSession(
        string id,
        bool isSubmitted,
        bool isOwner = false,
        List<OrderItem>? lines = null) => new()
    {
        Id = id,
        GuestName = isOwner ? "Owner" : $"Guest {id}",
        GroupReservationId = GroupId,
        IsOwnerSession = isOwner,
        OwnerUserId = isOwner ? OwnerUserId : null,
        IsSubmitted = isSubmitted,
        Dishes = lines ?? [],
    };

    private static Dish BuildDish(string id, DishState state = DishState.Available) => new()
    {
        Id = id,
        Name = $"Dish {id}",
        Price = 100m,
        State = state,
        LocationId = "location-1",
    };

    private static OrderItem Line(string dishId, int quantity, OrderItemState state = OrderItemState.Pending) => new()
    {
        DishId = dishId,
        Quantity = quantity,
        State = state,
    };

    private static Order BuildOrder(params OrderItem[] lines) => new()
    {
        OrderId = "order-1",
        CustomerId = OwnerUserId,
        ReservationId = ReservationId,
        ReservationAddress = "Main street 1, Table 7",
        ReservationTime = "Oct 31, 2026, 7:00 p.m. - 8:30 p.m.",
        Dishes = lines.ToList(),
        Status = OrderStatus.Created,
    };

    private static SubmittedGroupDishResponse[] Sorted(IEnumerable<SubmittedGroupDishResponse> dishes) => dishes
        .OrderBy(d => d.DishId, StringComparer.Ordinal)
        .ToArray();

    private static (string DishId, int Quantity, OrderItemState State)[] OrderLines(Order order) => order.Dishes
        .Select(d => (d.DishId, d.Quantity, d.State))
        .OrderBy(d => d.DishId, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// What the service passed to the transactional write, copied at call time,
    /// so later changes to the same objects do not affect the assertions.
    /// </summary>
    private sealed record TransactionalWrite(
        string GroupReservationId,
        GroupReservationStatus NewGroupStatus,
        GroupReservationStatus ExpectedCurrentGroupStatus,
        Order Order,
        OrderStatus OrderStatus,
        (string DishId, int Quantity, OrderItemState State)[] OrderLines);

    private readonly List<TransactionalWrite> _transactionalWrites = [];

    private void SetupSubmit(
        GroupReservation group,
        Reservation? reservation,
        List<GuestSession> sessions,
        List<Dish> dishes,
        Order? order,
        bool transactionSucceeds = true)
    {
        // The transactional write (AC18): true = both items written, false = the group condition failed.
        _groupReservationRepositoryMock
            .Setup(r => r.TryUpdateStatusWithOrderAsync(It.IsAny<GroupReservation>(), It.IsAny<GroupReservationStatus>(), It.IsAny<Order>()))
            .Callback<GroupReservation, GroupReservationStatus, Order>((g, expectedStatus, o) => _transactionalWrites.Add(
                new TransactionalWrite(g.GroupReservationId, g.Status, expectedStatus, o, o.Status, OrderLines(o))))
            .ReturnsAsync(transactionSucceeds);

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId))
            .ReturnsAsync(group);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(group.ReservationId))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync(sessions);

        // Behaves like the Dishes table: returns only the existing dishes among the requested ids.
        _dishRepositoryMock
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => dishes.Where(d => ids.Contains(d.Id)));

        _orderRepositoryMock
            .Setup(r => r.GetOrderByReservationIdAsync(group.ReservationId))
            .ReturnsAsync(order);
    }

    private void VerifyNothingSaved()
    {
        _groupReservationRepositoryMock.Verify(
            r => r.TryUpdateStatusWithOrderAsync(It.IsAny<GroupReservation>(), It.IsAny<GroupReservationStatus>(), It.IsAny<Order>()),
            Times.Never);
        VerifyNoSeparateSaves();
    }

    // AC18: no code path of the submit saves the order or the group with separate writes.
    private void VerifyNoSeparateSaves()
    {
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    private void VerifyOneTransactionalWrite() =>
        _groupReservationRepositoryMock.Verify(
            r => r.TryUpdateStatusWithOrderAsync(It.IsAny<GroupReservation>(), It.IsAny<GroupReservationStatus>(), It.IsAny<Order>()),
            Times.Once);

    private static List<GuestSession> OneSubmittedSession() =>
        [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 2)])];

    // ---- Owner session excluded from the guest limit (S12, AC9) ----
    // These service tests check the guest limit against the repository's joined-guest count
    // (CountJoinedGuestsByGroupReservationIdAsync), mocked here as the value the repository returns
    // for a group that also has an owner session. Excluding owner sessions from that count is
    // covered by GuestSessionRepositoryTests.

    private void SetupInviteLookup(Reservation reservation, int joinedGuests)
    {
        GroupReservation group = BuildGroup();

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(group.ReservationToken))
            .ReturnsAsync(group);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(GroupId))
            .ReturnsAsync(joinedGuests);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(reservation.TableId))
            .ReturnsAsync(new Table { Id = reservation.TableId, LocationId = "location-1", TableNumber = 7 });

        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync("location-1"))
            .ReturnsAsync(new Location
            {
                Id = "location-1",
                Address = "Main street 1",
                Description = "Downtown",
                AverageOccupancy = "50%",
            });

        _userServiceMock
            .Setup(s => s.GetUserName(OwnerUserId))
            .ReturnsAsync("Maria");
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_OwnerSessionAndJoinedGuestsOneBelowLimit_ReturnsGroupReservationInfo()
    {
        // Capacity 4 = owner + 3 guests; the repository reports 2 joined guests (owner session not counted).
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 2);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal(GroupId, result.GroupReservationId);
        Assert.Equal(4, result.MaxGuestCount);
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_OwnerSessionAndJoinedGuestsAtLimit_ThrowsGuestLimitExceeded()
    {
        // Capacity 4 = owner + 3 guests; the repository reports 3 joined guests (owner session not counted).
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 3);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() => _service.GetGroupReservationByTokenAsync("invite-token-1"));
    }

    // ---- SubmitGroupReservationAsync: success (S13–S15, AC14) ----

    [Fact]
    public async Task SubmitGroupReservationAsync_SubmittedSessionsShareDish_ReturnsMergedLinesOfSubmittedSessionsOnly()
    {
        Order order = BuildOrder();
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [
                BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 2), Line("dish2", 1)]),
                BuildSession("session-b", isSubmitted: true, lines: [Line("dish1", 1), Line("dish3", 4)]),
                BuildSession("session-c", isSubmitted: false, lines: [Line("dish1", 10), Line("dish4", 5)]),
            ],
            [BuildDish("dish1"), BuildDish("dish2"), BuildDish("dish3"), BuildDish("dish4")],
            order);

        IReadOnlyList<SubmittedGroupDishResponse> result = await _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId);

        Assert.Equal(
            new[]
            {
                new SubmittedGroupDishResponse("dish1", 3),
                new SubmittedGroupDishResponse("dish2", 1),
                new SubmittedGroupDishResponse("dish3", 4),
            },
            Sorted(result));

        Assert.Equal(
            new[]
            {
                ("dish1", 3, OrderItemState.Pending),
                ("dish2", 1, OrderItemState.Pending),
                ("dish3", 4, OrderItemState.Pending),
            },
            OrderLines(order));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_SubmittedOwnerSession_IncludesItsDishes()
    {
        Order order = BuildOrder();
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [
                BuildSession("owner-session", isSubmitted: true, isOwner: true, lines: [Line("dish1", 1)]),
                BuildSession("session-a", isSubmitted: true, lines: [Line("dish2", 2)]),
            ],
            [BuildDish("dish1"), BuildDish("dish2")],
            order);

        IReadOnlyList<SubmittedGroupDishResponse> result = await _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId);

        Assert.Equal(
            new[] { new SubmittedGroupDishResponse("dish1", 1), new SubmittedGroupDishResponse("dish2", 2) },
            Sorted(result));
    }

    [Theory]
    [InlineData(OrderItemState.Confirmed)]
    [InlineData(OrderItemState.Canceled)]
    [InlineData(OrderItemState.Pending)]
    public async Task SubmitGroupReservationAsync_OrderHasLines_MergesIntoExistingLineKeepingStateAndAppendsNewAsPending(OrderItemState existingState)
    {
        Order order = BuildOrder(Line("dish1", 1, existingState), Line("dish9", 2, OrderItemState.Confirmed));
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 2), Line("dish5", 3)])],
            [BuildDish("dish1"), BuildDish("dish5"), BuildDish("dish9")],
            order);

        IReadOnlyList<SubmittedGroupDishResponse> result = await _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId);

        // The response is the consolidated list, not the whole order.
        Assert.Equal(
            new[] { new SubmittedGroupDishResponse("dish1", 2), new SubmittedGroupDishResponse("dish5", 3) },
            Sorted(result));

        // S14, AC18: the order and the group status go to the one transactional write.
        TransactionalWrite write = Assert.Single(_transactionalWrites);
        Assert.Same(order, write.Order);
        Assert.Equal(OrderStatus.SubmittedByCustomer, write.OrderStatus);
        Assert.Equal(
            new[]
            {
                ("dish1", 3, existingState),
                ("dish5", 3, OrderItemState.Pending),
                ("dish9", 2, OrderItemState.Confirmed),
            },
            write.OrderLines);

        Assert.Equal(GroupId, write.GroupReservationId);
        Assert.Equal(GroupReservationStatus.Submitted, write.NewGroupStatus);
        Assert.Equal(GroupReservationStatus.Open, write.ExpectedCurrentGroupStatus);

        VerifyNoSeparateSaves();
    }

    // ---- SubmitGroupReservationAsync: one transaction (S22, S23, AC18) ----

    [Fact]
    public async Task SubmitGroupReservationAsync_Success_WritesOrderAndGroupInOneTransactionAndNoSeparateSaves()
    {
        Order order = BuildOrder();
        SetupSubmit(BuildGroup(), BuildReservation(), OneSubmittedSession(), [BuildDish("dish1")], order);

        await _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId);

        _groupReservationRepositoryMock.Verify(
            r => r.TryUpdateStatusWithOrderAsync(
                It.Is<GroupReservation>(g => g.GroupReservationId == GroupId),
                GroupReservationStatus.Open,
                order),
            Times.Once);
        VerifyNoSeparateSaves();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task SubmitGroupReservationAsync_TransactionGroupConditionFails_RereadsGroupAndThrowsForItsStatus(
        GroupReservationStatus currentGroupStatus,
        Type expectedException)
    {
        SetupSubmit(BuildGroup(), BuildReservation(), OneSubmittedSession(), [BuildDish("dish1")], BuildOrder(), transactionSucceeds: false);
        // First read: the group is Open; the re-read after the failed condition sees its current status.
        _groupReservationRepositoryMock
            .SetupSequence(r => r.GetGroupReservationByIdAsync(GroupId))
            .ReturnsAsync(BuildGroup())
            .ReturnsAsync(BuildGroup(currentGroupStatus));

        await Assert.ThrowsAsync(expectedException, () =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        _groupReservationRepositoryMock.Verify(r => r.GetGroupReservationByIdAsync(GroupId), Times.Exactly(2));
        VerifyOneTransactionalWrite();
        VerifyNoSeparateSaves();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubmitGroupReservationAsync_TransactionGroupConditionFailsAndRereadGroupIsOpenOrMissing_ThrowsInvalidOperation(bool groupFoundOnReread)
    {
        // Spec, submit "On success": a failed group condition with any status other than Submitted/Closed propagates (500).
        // A group that is not found on the re-read is treated the same way (backend-developer report).
        SetupSubmit(BuildGroup(), BuildReservation(), OneSubmittedSession(), [BuildDish("dish1")], BuildOrder(), transactionSucceeds: false);
        _groupReservationRepositoryMock
            .SetupSequence(r => r.GetGroupReservationByIdAsync(GroupId))
            .ReturnsAsync(BuildGroup())
            .ReturnsAsync(groupFoundOnReread ? BuildGroup(GroupReservationStatus.Open) : null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyOneTransactionalWrite();
        VerifyNoSeparateSaves();
    }

    // ---- SubmitGroupReservationAsync: 30-minute cutoff (S24, AC19) ----
    // Start times are far from the cutoff on purpose: the exact boundary is not tested against the real clock.

    [Fact]
    public async Task SubmitGroupReservationAsync_ReservationStartsIn20Minutes_ThrowsGroupReservationExpiredAndSavesNothing()
    {
        SetupSubmit(
            BuildGroup(),
            BuildReservation(startTime: DateTime.UtcNow.AddMinutes(20)),
            OneSubmittedSession(),
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_ReservationStartsIn40Minutes_SubmitsGroupPreOrder()
    {
        SetupSubmit(
            BuildGroup(),
            BuildReservation(startTime: DateTime.UtcNow.AddMinutes(40)),
            OneSubmittedSession(),
            [BuildDish("dish1")],
            BuildOrder());

        IReadOnlyList<SubmittedGroupDishResponse> result = await _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId);

        Assert.Equal(new[] { new SubmittedGroupDishResponse("dish1", 2) }, Sorted(result));
        VerifyOneTransactionalWrite();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_GroupSubmittedAndCutoffPassed_ThrowsGroupReservationSubmittedBecauseStatusIsCheckedFirst()
    {
        SetupSubmit(
            BuildGroup(GroupReservationStatus.Submitted),
            BuildReservation(startTime: DateTime.UtcNow.AddMinutes(20)),
            OneSubmittedSession(),
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_CutoffPassedAndNoSubmittedSession_ThrowsGroupReservationExpiredBeforeSessionChecks()
    {
        // Step 5a comes before step 6: the cutoff error wins over "no submitted session".
        SetupSubmit(
            BuildGroup(),
            BuildReservation(startTime: DateTime.UtcNow.AddMinutes(20)),
            [BuildSession("session-a", isSubmitted: false, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_UnavailableAndMissingDishes_AreDroppedFromResponseAndOrder()
    {
        Order order = BuildOrder();
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [
                BuildSession("session-a", isSubmitted: true, lines:
                [
                    Line("dish1", 2),
                    Line("dish-on-stop", 1),
                    Line("dish-out-of-stock", 1),
                    Line("deleted-dish", 1),
                ]),
            ],
            [BuildDish("dish1"), BuildDish("dish-on-stop", DishState.OnStop), BuildDish("dish-out-of-stock", DishState.OutOfStock)],
            order);

        IReadOnlyList<SubmittedGroupDishResponse> result = await _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId);

        Assert.Equal(new[] { new SubmittedGroupDishResponse("dish1", 2) }, Sorted(result));
        Assert.Equal(new[] { ("dish1", 2, OrderItemState.Pending) }, OrderLines(order));
    }

    // ---- SubmitGroupReservationAsync: nothing to submit (S16, AC13) ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubmitGroupReservationAsync_NoSubmittedSession_ThrowsValidationExceptionAndSavesNothing(bool groupHasUnsubmittedSessions)
    {
        List<GuestSession> sessions = groupHasUnsubmittedSessions
            ? [BuildSession("session-a", isSubmitted: false, lines: [Line("dish1", 1)])]
            : [];
        SetupSubmit(BuildGroup(), BuildReservation(), sessions, [BuildDish("dish1")], BuildOrder());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_SubmittedSessionsWithEmptyCarts_ThrowsValidationExceptionAndSavesNothing()
    {
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [BuildSession("session-a", isSubmitted: true), BuildSession("session-b", isSubmitted: true)],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_AllSubmittedDishesUnavailableOrMissing_ThrowsValidationExceptionAndSavesNothing()
    {
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish-on-stop", 1), Line("dish-out-of-stock", 2), Line("deleted-dish", 1)])],
            [BuildDish("dish-on-stop", DishState.OnStop), BuildDish("dish-out-of-stock", DishState.OutOfStock)],
            BuildOrder());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_NoActiveOrder_ThrowsValidationExceptionAndSavesNothing()
    {
        SetupSubmit(
            BuildGroup(),
            BuildReservation(),
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            order: null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    // ---- SubmitGroupReservationAsync: access and status checks (S17, AC11, AC12) ----

    [Fact]
    public async Task SubmitGroupReservationAsync_GroupReservationNotFound_ThrowsGroupReservationNotFoundAndSavesNothing()
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(GroupId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_BodyReservationIdDoesNotMatchGroup_ThrowsValidationExceptionBeforeReservationLookup()
    {
        // The group's reservation does not exist either: the id mismatch must be reported first (400 before 404).
        SetupSubmit(
            BuildGroup(),
            reservation: null,
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, "another-reservation"));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_ReservationNotFound_ThrowsReservationNotFoundAndSavesNothing()
    {
        SetupSubmit(
            BuildGroup(),
            reservation: null,
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Theory]
    [InlineData("another-user")]
    [InlineData(null)]
    public async Task SubmitGroupReservationAsync_CallerIsNotReservationOwner_ThrowsActionForbiddenAndSavesNothing(string? reservationCustomerId)
    {
        SetupSubmit(
            BuildGroup(),
            BuildReservation(customerId: reservationCustomerId),
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_CallerIsNotOwnerOfSubmittedGroup_ThrowsActionForbiddenBeforeStatusCheck()
    {
        SetupSubmit(
            BuildGroup(GroupReservationStatus.Submitted),
            BuildReservation(customerId: "another-user"),
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted, ReservationStatus.Reserved, typeof(GroupReservationSubmittedException))]
    [InlineData(GroupReservationStatus.Closed, ReservationStatus.Reserved, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Cancelled, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.InProgress, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Completed, typeof(GroupReservationExpiredException))]
    public async Task SubmitGroupReservationAsync_GroupOrReservationNotActive_ThrowsAndSavesNothing(
        GroupReservationStatus groupStatus,
        ReservationStatus reservationStatus,
        Type expectedException)
    {
        SetupSubmit(
            BuildGroup(groupStatus),
            BuildReservation(reservationStatus),
            [BuildSession("session-a", isSubmitted: true, lines: [Line("dish1", 1)])],
            [BuildDish("dish1")],
            BuildOrder());

        await Assert.ThrowsAsync(expectedException, () =>
            _service.SubmitGroupReservationAsync(OwnerUserId, GroupId, ReservationId));

        VerifyNothingSaved();
    }
}
