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
using System.Text.Json;

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
        reservation.GuestCount = 4;
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
        Assert.Equal(4, result.MaxGuestCount);
        Assert.Equal(reservation.StartTime, result.StartTime);
        Assert.Equal(reservation.EndTime, result.EndTime);
        Assert.Equal(table.TableNumber, result.TableNumber);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal(location.Address, result.LocationAddress);
        Assert.Equal(expectedUserName, result.InviterName);
        // AC2, AC3: an Open group with no joined guests has every place but the owner's free.
        Assert.Equal(GroupReservationStatus.Open, result.Status);
        Assert.Equal(3, result.AvailableSpots);
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

    // ---- GetGroupReservationByTokenAsync: group status (spec 2026-11-02-group-preorder-bugfixes, T5, AC3, AC4) ----

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsClosed_ThrowsGroupReservationExpiredWithoutReadingReservation(
        GroupReservation groupReservation)
    {
        groupReservation.Status = GroupReservationStatus.Closed;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));

        _reservationRepositoryMock.Verify(r => r.GetReservationByIdAsync(It.IsAny<string>()), Times.Never);
        _guestSessionRepositoryMock.Verify(r => r.CountJoinedGuestsByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsSubmitted_ReturnsInfoWithSubmittedStatusAndNoAvailableSpots()
    {
        // No guest has joined: if the group were treated as Open, AvailableSpots would be 3.
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 0, GroupReservationStatus.Submitted);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal(GroupReservationStatus.Submitted, result.Status);
        Assert.Equal(0, result.AvailableSpots);
        Assert.Equal(GroupId, result.GroupReservationId);
        Assert.Equal("location-1", result.LocationId);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal("Main street 1", result.LocationAddress);
        Assert.Equal(4, result.MaxGuestCount);
        Assert.Equal(7, result.TableNumber);
        Assert.Equal("Maria", result.InviterName);

        // Accepted at the step 3 review: a submitted group skips the joined-guest count query.
        _guestSessionRepositoryMock.Verify(r => r.CountJoinedGuestsByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_ReservationMissing_ThrowsValidationException()
    {
        GroupReservation group = BuildGroup();
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(group.ReservationToken))
            .ReturnsAsync(group);
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync((Reservation?)null);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationByTokenAsync(group.ReservationToken));

        Assert.Equal("Associated reservation not found.", exception.Message);
        _tableRepositoryMock.Verify(r => r.GetTableAsync(It.IsAny<string>()), Times.Never);
    }

    // ---- GetGroupReservationByTokenAsync: other response fields (AC5) ----

    [Fact]
    public async Task GetGroupReservationByTokenAsync_LocationMissingAndReservationHasNoCustomer_ReturnsFallbackAddressAndInviterName()
    {
        SetupInviteLookup(BuildReservation(customerId: null, guestCount: 4), joinedGuests: 1);
        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync("location-1"))
            .ReturnsAsync((Location?)null);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal("Unknown Address", result.LocationAddress);
        Assert.Equal("Unknown", result.InviterName);
        Assert.Equal("location-1", result.LocationId);
        Assert.Equal(2, result.AvailableSpots);
        _userServiceMock.Verify(s => s.GetUserName(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_InviterNameNotFound_ReturnsUnknownInviterName()
    {
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 1);
        _userServiceMock
            .Setup(s => s.GetUserName(OwnerUserId))
            .ReturnsAsync((string?)null);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal("Unknown", result.InviterName);
        Assert.Equal("Main street 1", result.LocationAddress);
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

    // ---- GetGroupReservationInviteTokenAsync (spec 2026-11-02-group-preorder-bugfixes, T8–T11, AC6–AC8) ----
    // The reservation owner is reservation.CustomerId; with AutoData it is a random string.

    [Theory]
    [InlineAutoData(2)]
    [InlineAutoData(3)]
    public async Task GetGroupReservationInviteTokenAsync_ValidInput_ReturnsToken(int guestCount, Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = guestCount;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.CustomerId!, reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_InvalidReservationId_ThrowsNotFound(string userId, string reservationId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(userId, reservationId));

        _groupReservationRepositoryMock.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(1)]
    [InlineAutoData(0)]
    public async Task GetGroupReservationInviteTokenAsync_LessThanTwoGuests_ThrowsValidationException(int guestCount, Reservation reservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = guestCount;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.CustomerId!, reservation.Id));

        _groupReservationRepositoryMock.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    [InlineAutoData(ReservationStatus.InProgress)]
    public async Task GetGroupReservationInviteTokenAsync_StatusIsNotReserved_ThrowsGroupReservationExpired(ReservationStatus reservationStatus, Reservation reservation)
    {
        reservation.Status = reservationStatus;
        reservation.GuestCount = 3;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        GroupReservationExpiredException exception = await Assert.ThrowsAsync<GroupReservationExpiredException>(
            () => _service.GetGroupReservationInviteTokenAsync(reservation.CustomerId!, reservation.Id));

        Assert.Equal("The group reservation is expired.", exception.Message);
        _groupReservationRepositoryMock.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_GroupClosed_ThrowsGroupReservationExpired(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Closed;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.CustomerId!, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_GroupSubmitted_ReturnsToken(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Submitted;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.CustomerId!, reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Fact]
    public async Task GetGroupReservationInviteTokenAsync_NoGroupReservation_ThrowsGroupReservationNotFound()
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(BuildReservation());
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(ReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(OwnerUserId, ReservationId));
    }

    [Theory]
    [InlineData("another-user", 4, ReservationStatus.Reserved)]
    [InlineData(null, 4, ReservationStatus.Reserved)]
    [InlineData("another-user", 1, ReservationStatus.Reserved)]
    [InlineData("another-user", 4, ReservationStatus.Cancelled)]
    [InlineData("another-user", 1, ReservationStatus.Completed)]
    public async Task GetGroupReservationInviteTokenAsync_CallerIsNotReservationOwner_ThrowsActionForbiddenBeforeAnyOtherCheck(
        string? reservationCustomerId,
        int guestCount,
        ReservationStatus reservationStatus)
    {
        // Ownership is checked right after the reservation lookup: a non-owner gets 403 whatever the
        // reservation's guest count or status, and the group is never read.
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(BuildReservation(reservationStatus, customerId: reservationCustomerId, guestCount: guestCount));
        // An Open group exists, so without the ownership check a valid reservation would return its token.
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(ReservationId))
            .ReturnsAsync(BuildGroup());

        ActionForbiddenException exception = await Assert.ThrowsAsync<ActionForbiddenException>(
            () => _service.GetGroupReservationInviteTokenAsync(OwnerUserId, ReservationId));

        Assert.Equal("Only the reservation owner can perform this action.", exception.Message);
        _groupReservationRepositoryMock.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
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

    private void SetupInviteLookup(
        Reservation reservation,
        int joinedGuests,
        GroupReservationStatus groupStatus = GroupReservationStatus.Open)
    {
        GroupReservation group = BuildGroup(groupStatus);

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
        Assert.Equal(GroupReservationStatus.Open, result.Status);
        Assert.Equal(1, result.AvailableSpots);
    }

    // ---- GetGroupReservationByTokenAsync is a plain read: no guest limit (spec 2026-11-02-group-preorder-bugfixes, T1–T4, AC1, AC2) ----
    // The limit is checked only by POST /guest/sessions (GuestSessionServiceTests); a full group returns AvailableSpots 0.

    [Fact]
    public async Task GetGroupReservationByTokenAsync_OwnerSessionAndJoinedGuestsAtLimit_ReturnsInfoWithNoAvailableSpots()
    {
        // GP-01: capacity 4 = owner + 3 guests; the repository reports 3 joined guests (owner session not counted).
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 3);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal(GroupId, result.GroupReservationId);
        Assert.Equal(4, result.MaxGuestCount);
        Assert.Equal(GroupReservationStatus.Open, result.Status);
        Assert.Equal(0, result.AvailableSpots);
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_LastGuestFilledGroupOfThree_ReturnsInfoWithNoAvailableSpots()
    {
        // The develop case: GuestCount 3 = owner + 2 guests, both guests joined.
        SetupInviteLookup(BuildReservation(guestCount: 3), joinedGuests: 2);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal(GroupId, result.GroupReservationId);
        Assert.Equal(3, result.MaxGuestCount);
        Assert.Equal(GroupReservationStatus.Open, result.Status);
        Assert.Equal(0, result.AvailableSpots);
    }

    [Fact]
    public async Task GetGroupReservationByTokenAsync_JoinedGuestsEqualGuestCount_ReturnsInfoWithNoAvailableSpots()
    {
        // Above the limit (possible through concurrent joins, Risk R4): joined guests == GuestCount.
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 4);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal(GroupId, result.GroupReservationId);
        Assert.Equal(4, result.MaxGuestCount);
        Assert.Equal(GroupReservationStatus.Open, result.Status);
        Assert.Equal(0, result.AvailableSpots);
    }

    [Theory]
    [InlineData(4, 0, 3)]
    [InlineData(4, 2, 1)]
    [InlineData(4, 3, 0)]
    [InlineData(4, 5, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 0, 0)]
    public async Task GetGroupReservationByTokenAsync_OpenGroup_AvailableSpotsExcludeOwnerPlaceAndJoinedGuestsAndAreNeverNegative(
        int guestCount,
        int joinedGuests,
        int expectedAvailableSpots)
    {
        SetupInviteLookup(BuildReservation(guestCount: guestCount), joinedGuests);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");

        Assert.Equal(expectedAvailableSpots, result.AvailableSpots);
        Assert.Equal(GroupReservationStatus.Open, result.Status);
        _guestSessionRepositoryMock.Verify(r => r.CountJoinedGuestsByGroupReservationIdAsync(GroupId), Times.Once);
    }

    [Theory]
    [InlineData(GroupReservationStatus.Open, "Open", 1)]
    [InlineData(GroupReservationStatus.Submitted, "Submitted", 0)]
    public async Task GetGroupReservationByTokenAsync_ResultSerializedWithoutOptions_WritesStatusAsStringAndAvailableSpotsAsNumber(
        GroupReservationStatus groupStatus,
        string expectedStatus,
        int expectedAvailableSpots)
    {
        // AC3: GET /guest/invites/{token} serializes the result with JsonSerializer.Serialize and no options
        // (ResponseBuilder.CreateSuccessResponse), so the field names stay PascalCase.
        SetupInviteLookup(BuildReservation(guestCount: 4), joinedGuests: 2, groupStatus);

        var result = await _service.GetGroupReservationByTokenAsync("invite-token-1");
        string json = JsonSerializer.Serialize(result);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement status = document.RootElement.GetProperty("Status");
        Assert.Equal(JsonValueKind.String, status.ValueKind);
        Assert.Equal(expectedStatus, status.GetString());

        JsonElement availableSpots = document.RootElement.GetProperty("AvailableSpots");
        Assert.Equal(JsonValueKind.Number, availableSpots.ValueKind);
        Assert.Equal(expectedAvailableSpots, availableSpots.GetInt32());

        Assert.Contains($"\"Status\":\"{expectedStatus}\"", json);
        Assert.Contains($"\"AvailableSpots\":{expectedAvailableSpots}", json);
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
