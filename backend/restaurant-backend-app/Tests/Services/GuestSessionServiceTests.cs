using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using NPOI.POIFS.Storage;
using System.Security.Cryptography;
using System.Text;

namespace APIHandler.Tests.Services;

public class GuestSessionServiceTests
{
    private readonly Mock<IGuestSessionRepository> _mockGuestSessionRepository = new();
    private readonly Mock<IGroupReservationRepository> _mockGroupReservationRepository = new();
    private readonly Mock<IDishRepository> _mockDishRepository = new();
    private readonly Mock<IReservationRepository> _mockReservationRepository = new();
    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<ITableRepository> _mockTableRepository = new();
    private readonly Mock<IOrderRepository> _mockOrderRepository = new();
    private readonly GuestSessionService _guestSessionService;

    public GuestSessionServiceTests()
    {
        _guestSessionService = new GuestSessionService(
            _mockGroupReservationRepository.Object,
            _mockGuestSessionRepository.Object,
            _mockDishRepository.Object,
            _mockReservationRepository.Object,
            _mockUserRepository.Object,
            _mockTableRepository.Object,
            _mockOrderRepository.Object);
    }
    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(3)]
    public async Task CreateGuestSessionAsync_CorrectData_ReturnsSessionToken(
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = 5;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s);

        _mockGuestSessionRepository
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var response = await _guestSessionService.CreateGuestSessionAsync(request);

        Assert.NotEmpty(response.SessionId);
        Assert.NotEmpty(response.SessionToken);
        Assert.NotNull(session);
        Assert.Equal(groupReservation.GroupReservationId, session.GroupReservationId);
        Assert.Equal(request.GuestName, session.GuestName);
        Assert.NotNull(session.SessionTokenHash);
        Assert.NotEmpty(session.SessionTokenHash);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_InvalidGroupReservationId_ThrowsGroupReservationNotFoundException(CreateGuestSessionRequest request)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_GroupReservationExpired_ThrowsGroupReservationExpiredException(CreateGuestSessionRequest request, GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldAddNewDish_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;
        int quantityToAdd = 2;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(dish.Id, guestSession.Dishes[0].DishId);
        Assert.Equal(quantityToAdd, guestSession.Dishes[0].Quantity);
        Assert.Equal(OrderItemState.Pending, guestSession.Dishes[0].State);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldIncreaseQuantity_WhenDishAlreadyExists(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        int initialQuantity = 3;
        int quantityToAdd = 2;
        guestSession.Dishes = [new OrderItem { DishId = dish.Id, Quantity = initialQuantity, State = OrderItemState.Pending }];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity + quantityToAdd, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(-1)]
    public async Task AddDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(int quantity, string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, quantity));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotFound_WhenDishNotFound(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, string dishId)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);

        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dishId, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotAvailable_WhenDishIsNotAvailable(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        dish.State = DishState.OutOfStock;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        var exception = await Assert.ThrowsAsync<DishIsNotAvailableException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task AddDishAsync_ShouldThrowGroupReservationExpired_WhenGroupStatusIsNotOpen(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation, Dish dish)
    {
        dish.State = DishState.Available;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldDecreaseQuantity_WhenQuantityIsGreaterThanAmountToRemove(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 5;
        int quantityToRemove = 2;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);


        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity - quantityToRemove, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldRemoveDishFromList_WhenQuantityToRemoveIsEqualOrGreater(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 2;
        int quantityToRemove = 3;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Empty(guestSession.Dishes);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowDishNotFoundException_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
           .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync<DishNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, 2));

        Assert.Contains("not found in guest session", exception.Message);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(string guestSessionId, string dishId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 0));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, -5));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, string dishId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 2));
    }
    [Theory]
    [InlineAutoData(1, 0)]
    [InlineAutoData(1, 1)]
    [InlineAutoData(2, 1)]
    [InlineAutoData(2, 2)]
    public async Task CreateGuestSessionAsync_WhenCapacityReached_ThrowsGuestLimitExceededException(
        int reservationCapacity,
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = reservationCapacity;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var exception = await Assert.ThrowsAsync<GuestLimitExceededException>(() => _guestSessionService.CreateGuestSessionAsync(request));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_MissingParentReservation_ThrowsGroupReservationExpiredException(
        CreateGuestSessionRequest request,
        GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        var exception = await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_ValidTokenAndId_ReturnsTrue(GuestSession guestSession, byte[] tokenBytes)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSession.Id);

        Assert.True(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidToken_ReturnsFalse(byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync((GuestSession?)null);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidGuestSessionId_ReturnsFalse(GuestSession guestSession, byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }
    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_Valid_SubmitsSuccessfuilly(GuestSession guestSession, GroupReservation groupReservation, Dish[] dishes)
    {
        foreach (var dish in dishes)
            dish.State = DishState.Available;

        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(dishes);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s);

        await _guestSessionService.SubmitCartAsync(guestSession.Id);

        Assert.NotNull(session);
        Assert.True(session.IsSubmitted);
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_SessionNotFound_ThrowsGuestSessionNotFound(string guestSessionId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.SubmitCartAsync(guestSessionId));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task SubmitCartAsync_GroupReservationNotOpen_ThrowsException(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(exceptionType, () => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionHasNoDishes_ThrowsCartIsEmptyException(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<CartIsEmptyException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionSubmitted_ThrowsAlreadySubmited(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.IsSubmitted = true;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GuestSessionSubmittedException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [InlineAutoData(DishState.OutOfStock)]
    [InlineAutoData(DishState.OnStop)]
    public async Task SubmitCartAsync_DishNotAvailable_ThrowsDishNotAvailable(DishState state, Dish dish, GuestSession guestSession, GroupReservation groupReservation)
    {
        dish.State = state;
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        guestSession.Dishes = 
        [
            new OrderItem()
            {
                DishId = dish.Id,
                Quantity = 1,
            }
        ];

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish]);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<DishIsNotAvailableException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }
    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_Guest_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = false;

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(session.GuestName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_GroupOwner_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session,
        GroupReservation groupReservation,
        Reservation reservation,
        string customerId,
        string ownerName,
        UserType userType)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = true;
        session.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = customerId;
        userType.Attributes.Add(new AttributeType()
        {
            Name = "given_name",
            Value = ownerName,
        });

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(customerId))
            .ReturnsAsync(userType);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(ownerName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    public static TheoryData<IEnumerable<string>, IEnumerable<Dish>, List<OrderItem>, decimal> PepareDishesForGuestSession()
    {
        var dishMap = new Dictionary<string, Dish>()
        {
            ["1"] = new()
            {
                Id = "1",
                Price = 1
            },
            ["2"] = new()
            {
                Id = "2",
                Price = 2
            },
            ["3"] = new()
            {
                Id = "3",
                Price = 3
            }
        };

        var orderItems = new List<OrderItem>
        {
            new OrderItem()
                    {
                        DishId = "1",
                        Quantity = 2
                    },
                    new OrderItem()
                    {
                        DishId = "2",
                        Quantity = 3
                    },
                    new OrderItem()
                    {
                        DishId = "3",
                        Quantity = 1
                    }
        };

        var expectedTotal = orderItems.Sum(o => dishMap[o.DishId].Price * o.Quantity);

        return new()
        {
            { dishMap.Keys, dishMap.Values, orderItems, expectedTotal }
        };
    }

    // Group pre-order owner endpoints: docs/specs/2026-10-31-group-preorder-owner-endpoints.md

    private const string OwnerUserId = "owner-user-1";
    private const string GroupId = "group-1";
    private const string OtherGroupId = "group-2";
    private const string ReservationId = "reservation-1";
    private const string StoredOwnerName = "Stored Owner Name";

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
        int guestCount = 4) => new()
    {
        Id = ReservationId,
        TableId = "table-1",
        WaiterId = "waiter-1",
        CustomerId = customerId,
        GuestCount = guestCount,
        Status = status,
    };

    private static GuestSession BuildSession(
        string id,
        string name,
        bool isOwner = false,
        bool isSubmitted = false,
        string groupId = GroupId,
        List<OrderItem>? lines = null) => new()
    {
        Id = id,
        GuestName = name,
        GroupReservationId = groupId,
        IsOwnerSession = isOwner,
        OwnerUserId = isOwner ? OwnerUserId : null,
        IsSubmitted = isSubmitted,
        Dishes = lines ?? [],
    };

    private static GuestSession BuildOwnerSession(List<OrderItem>? lines = null) =>
        BuildSession("owner-session", StoredOwnerName, isOwner: true, lines: lines);

    private static Dish BuildDish(string id, decimal price, DishState state = DishState.Available) => new()
    {
        Id = id,
        Name = $"Dish {id}",
        Description = $"Description of {id}",
        DishType = DishType.MainCourse,
        Price = price,
        Weight = "300 g",
        Calories = "400 kcal",
        Proteins = "20 g",
        Fats = "10 g",
        Carbohydrates = "50 g",
        Vitamins = "A, C",
        ImageUrl = $"https://images.example/{id}.png",
        State = state,
        LocationId = "location-1",
    };

    private static OrderItem Line(string dishId, int quantity) => new()
    {
        DishId = dishId,
        Quantity = quantity,
        State = OrderItemState.Pending,
    };

    private static UserType BuildUser(params (string Name, string Value)[] attributes) => new()
    {
        Username = OwnerUserId,
        Attributes = attributes.Select(a => new AttributeType { Name = a.Name, Value = a.Value }).ToList(),
    };

    private static (string DishId, int Quantity)[] ItemsOf(Participant participant) => participant.Items
        .Select(i => (i.Dish.Id, i.Quantity))
        .OrderBy(i => i.Id, StringComparer.Ordinal)
        .ToArray();

    private void SetupGroupAndReservation(GroupReservation group, Reservation? reservation)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId))
            .ReturnsAsync(group);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(group.ReservationId))
            .ReturnsAsync(reservation);
    }

    private void SetupSessions(params GuestSession[] sessions)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(GroupId))
            .ReturnsAsync(sessions.ToList());
    }

    // Behaves like the Dishes table: returns only the existing dishes among the requested ids.
    private void SetupDishes(params Dish[] dishes)
    {
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => dishes.Where(d => ids.Contains(d.Id)));
    }

    private void VerifyNoSessionDeleted() =>
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);

    private void VerifyNoSessionSaved() =>
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);

    // ---- Owner session helpers (AC8, AC20) ----

    // The deterministic owner session id of the group "group-1": owner-{groupReservationId}.
    private const string OwnerSessionId = "owner-" + GroupId;

    private void SetupOwnerSessionLookup(
        GroupReservationStatus groupStatus = GroupReservationStatus.Open,
        ReservationStatus reservationStatus = ReservationStatus.Reserved,
        GuestSession? storedOwnerSession = null)
    {
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(BuildReservation(reservationStatus));
        _mockGroupReservationRepository
            .Setup(r => r.GetByReservationIdAsync(ReservationId))
            .ReturnsAsync(BuildGroup(groupStatus));
        // Only the deterministic id is set up: a lookup by any other id returns null.
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionIdConsistentAsync(OwnerSessionId))
            .ReturnsAsync(storedOwnerSession);
    }

    private static GuestSession BuildStoredOwnerSession() =>
        BuildSession(OwnerSessionId, StoredOwnerName, isOwner: true);

    private void VerifyNoOwnerSessionCreated()
    {
        _mockGuestSessionRepository.Verify(r => r.TryCreateAsync(It.IsAny<GuestSession>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    // S9b: the owner session is not looked up in the group's session list (groupReservationId index).
    private void VerifyGroupSessionListNotRead() =>
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);

    // ---- GetGroupSessionsAsync: access checks (S7, AC2) ----

    [Fact]
    public async Task GetGroupSessionsAsync_GroupReservationNotFound_ThrowsGroupReservationNotFoundException()
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(GroupId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId));

        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_ReservationNotFound_ThrowsReservationNotFoundException()
    {
        SetupGroupAndReservation(BuildGroup(), null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId));

        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("another-user")]
    [InlineData(null)]
    public async Task GetGroupSessionsAsync_CallerIsNotReservationOwner_ThrowsActionForbiddenException(string? reservationCustomerId)
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation(customerId: reservationCustomerId));

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId));

        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted, ReservationStatus.Reserved)]
    [InlineData(GroupReservationStatus.Closed, ReservationStatus.Cancelled)]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.InProgress)]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Completed)]
    public async Task GetGroupSessionsAsync_AnyGroupAndReservationStatus_ReturnsSnapshot(
        GroupReservationStatus groupStatus,
        ReservationStatus reservationStatus)
    {
        SetupGroupAndReservation(BuildGroup(groupStatus), BuildReservation(reservationStatus));
        SetupSessions(BuildSession("session-a", "Andrii", lines: [Line("dish1", 2)]));
        SetupDishes(BuildDish("dish1", 100m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant participant = Assert.Single(result.Participants);
        Assert.Equal("session-a", participant.Id);
        Assert.Equal(200m, result.Total);
    }

    // ---- Group snapshot (S1–S5, AC3–AC6) ----

    [Fact]
    public async Task GetGroupSessionsAsync_TwoGuests_ReturnsParticipantsWithItemsAndTotals()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(
            BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dish1", 2), Line("dish2", 1)]),
            BuildSession("session-b", "Olena", isSubmitted: false, lines: [Line("dish3", 3)]));
        SetupDishes(BuildDish("dish1", 180m), BuildDish("dish2", 50m), BuildDish("dish3", 100m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        List<Participant> participants = result.Participants.ToList();
        Assert.Equal(2, participants.Count);

        Participant first = participants[0];
        Assert.Equal("session-a", first.Id);
        Assert.Equal(ParticipantType.Guest, first.Type);
        Assert.Equal("Andrii", first.Name);
        Assert.True(first.IsSubmitted);
        Assert.Equal(new[] { ("dish1", 2), ("dish2", 1) }, ItemsOf(first));
        Assert.Equal(410m, first.Total);

        OrderItemResponse dish1Item = first.Items.Single(i => i.Dish.Id == "dish1");
        Assert.Equal("Dish dish1", dish1Item.Dish.Name);
        Assert.Equal(180m, dish1Item.Dish.Price);
        Assert.Equal(DishStateLabels.Available, dish1Item.Dish.State);

        Participant second = participants[1];
        Assert.Equal("session-b", second.Id);
        Assert.Equal(ParticipantType.Guest, second.Type);
        Assert.Equal("Olena", second.Name);
        Assert.False(second.IsSubmitted);
        Assert.Equal(new[] { ("dish3", 3) }, ItemsOf(second));
        Assert.Equal(300m, second.Total);

        Assert.Equal(710m, result.Total);
    }

    [Theory]
    [InlineData(DishState.OnStop, DishStateLabels.OnStop)]
    [InlineData(DishState.OutOfStock, DishStateLabels.OutOfStock)]
    public async Task GetGroupSessionsAsync_UnavailableDish_IsListedButExcludedFromTotals(DishState unavailableState, string expectedStateLabel)
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii", lines: [Line("dish1", 2), Line("dish2", 3)]));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 50m, unavailableState));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant participant = Assert.Single(result.Participants);
        Assert.Equal(new[] { ("dish1", 2), ("dish2", 3) }, ItemsOf(participant));
        Assert.Equal(expectedStateLabel, participant.Items.Single(i => i.Dish.Id == "dish2").Dish.State);
        Assert.Equal(200m, participant.Total);
        Assert.Equal(200m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_GuestWithEmptyCart_ReturnsEmptyItemsAndZeroTotal()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(
            BuildSession("session-empty", "Empty Cart"),
            BuildSession("session-b", "Olena", lines: [Line("dish1", 1)]));
        SetupDishes(BuildDish("dish1", 75m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant empty = result.Participants.Single(p => p.Id == "session-empty");
        Assert.Empty(empty.Items);
        Assert.Equal(0m, empty.Total);
        Assert.Equal(75m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_GroupWithoutSessions_ReturnsEmptyParticipantsAndZeroTotal()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions();
        SetupDishes();

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_DishMissingFromDishesTable_OmitsLineWithoutError()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii", lines: [Line("dish1", 1), Line("deleted-dish", 5)]));
        SetupDishes(BuildDish("dish1", 100m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant participant = Assert.Single(result.Participants);
        Assert.Equal(new[] { ("dish1", 1) }, ItemsOf(participant));
        Assert.Equal(100m, participant.Total);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_DishRepositoryReturnsNull_ReturnsParticipantsWithoutItems()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii", lines: [Line("dish1", 1)]));
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<Dish>)null!);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant participant = Assert.Single(result.Participants);
        Assert.Empty(participant.Items);
        Assert.Equal(0m, participant.Total);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_DishRepositoryReturnsDuplicateDish_CountsLineOnce()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii", lines: [Line("dish1", 2)]));
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([BuildDish("dish1", 100m), BuildDish("dish1", 100m)]);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant participant = Assert.Single(result.Participants);
        Assert.Equal(new[] { ("dish1", 2) }, ItemsOf(participant));
        Assert.Equal(200m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_OwnerSessionReturnedLast_ComesFirstWithCognitoGivenName()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(
            BuildSession("session-a", "Andrii", lines: [Line("dish1", 1)]),
            BuildSession("session-b", "Olena"),
            BuildOwnerSession());
        // The owner's lines come from the reservation's order (docs/specs/2026-11-02-owner-preorder-in-group.md).
        SetupOwnerOrder(BuildOrder(OrderStatus.Created, Line("dish2", 2)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 30m));
        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(OwnerUserId))
            .ReturnsAsync(BuildUser(("family_name", "Koval"), ("given_name", "Maria")));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        List<Participant> participants = result.Participants.ToList();
        Assert.Equal(new[] { "owner-session", "session-a", "session-b" }, participants.Select(p => p.Id).ToArray());

        Assert.Equal(ParticipantType.Owner, participants[0].Type);
        Assert.Equal("Maria", participants[0].Name);
        Assert.Equal(60m, participants[0].Total);

        Assert.Equal(ParticipantType.Guest, participants[1].Type);
        Assert.Equal("Andrii", participants[1].Name);
        Assert.Equal(ParticipantType.Guest, participants[2].Type);
        Assert.Equal("Olena", participants[2].Name);

        Assert.Equal(160m, result.Total);
        _mockUserRepository.Verify(r => r.GetUserByIdAsync(OwnerUserId), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetGroupSessionsAsync_OwnerGivenNameNotFound_UsesStoredSessionName(bool userExistsWithoutGivenName)
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii"), BuildOwnerSession());
        SetupDishes();
        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(OwnerUserId))
            .ReturnsAsync(userExistsWithoutGivenName ? BuildUser(("family_name", "Koval")) : null);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(StoredOwnerName, owner.Name);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_NoOwnerSession_DoesNotReadCognitoUser()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii"));
        SetupDishes();

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Assert.All(result.Participants, p => Assert.Equal(ParticipantType.Guest, p.Type));
        _mockUserRepository.Verify(r => r.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
    }

    // ---- Owner participant from the reservation's order (T1–T8, AC1–AC5) ----
    // Spec: docs/specs/2026-11-02-owner-preorder-in-group.md

    private static Order BuildOrder(OrderStatus status, params OrderItem[] lines) => new()
    {
        OrderId = "order-1",
        CustomerId = OwnerUserId,
        ReservationId = ReservationId,
        ReservationAddress = "Main street 1, Table 1",
        ReservationTime = "Nov 2, 2026, 7:00 p.m. - 8:00 p.m.",
        Dishes = lines.ToList(),
        Status = status,
    };

    // The repository skips Cancelled orders, so "no order" and "only a Cancelled order" both arrive as null.
    private void SetupOwnerOrder(Order? order) =>
        _mockOrderRepository
            .Setup(r => r.GetOrderByReservationIdAsync(ReservationId))
            .ReturnsAsync(order);

    private void VerifyOwnerOrderReadOnceForReservation()
    {
        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(ReservationId), Times.Once);
        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_OpenGroupOrderCreated_OwnerHasOrderLinesTotalAndIsNotSubmitted()
    {
        // T1 (AC1, AC2).
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildOwnerSession());
        SetupOwnerOrder(BuildOrder(OrderStatus.Created, Line("dish1", 2), Line("dish2", 1)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 50m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = Assert.Single(result.Participants);
        Assert.Equal("owner-session", owner.Id);
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dish1", 2), ("dish2", 1) }, ItemsOf(owner));

        OrderItemResponse dish1Item = owner.Items.Single(i => i.Dish.Id == "dish1");
        Assert.Equal("Dish dish1", dish1Item.Dish.Name);
        Assert.Equal(100m, dish1Item.Dish.Price);
        Assert.Equal(DishStateLabels.Available, dish1Item.Dish.State);

        Assert.Equal(250m, owner.Total);
        Assert.False(owner.IsSubmitted);
        Assert.Equal(250m, result.Total);

        VerifyOwnerOrderReadOnceForReservation();
    }

    [Theory]
    [InlineData(OrderStatus.Created, false)]
    [InlineData(OrderStatus.SubmittedByCustomer, true)]
    [InlineData(OrderStatus.Submitted, true)]
    public async Task GetGroupSessionsAsync_OwnerIsSubmitted_FollowsOrderStatusNotOwnerSessionFlag(OrderStatus orderStatus, bool expectedIsSubmitted)
    {
        // T2 (AC2): the owner session's own flag is the opposite value, so only the order status can produce the expected one.
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildSession("owner-session", StoredOwnerName, isOwner: true, isSubmitted: !expectedIsSubmitted));
        SetupOwnerOrder(BuildOrder(orderStatus, Line("dish1", 1)));
        SetupDishes(BuildDish("dish1", 100m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = Assert.Single(result.Participants);
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(expectedIsSubmitted, owner.IsSubmitted);
        Assert.Equal(new[] { ("dish1", 1) }, ItemsOf(owner));
        Assert.Equal(100m, owner.Total);
    }

    [Theory]
    [InlineData(GroupReservationStatus.Open)]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task GetGroupSessionsAsync_NoActiveOrder_OwnerHasNoItemsZeroTotalAndIsNotSubmitted(GroupReservationStatus groupStatus)
    {
        // T3 (AC3): the owner session's own dishes must not be used as a fallback.
        SetupGroupAndReservation(BuildGroup(groupStatus), BuildReservation());
        SetupSessions(
            BuildOwnerSession(lines: [Line("dish2", 2)]),
            BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dish1", 1)]),
            BuildSession("session-b", "Olena", lines: [Line("dish2", 3)]));
        SetupOwnerOrder(null);
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 30m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Empty(owner.Items);
        Assert.Equal(0m, owner.Total);
        Assert.False(owner.IsSubmitted);
        Assert.Equal(190m, result.Total);

        VerifyOwnerOrderReadOnceForReservation();
    }

    [Fact]
    public async Task GetGroupSessionsAsync_OwnerSessionHasOtherDishes_OwnerShowsOrderLinesAndSessionDishesAreNotLookedUp()
    {
        // T4 (AC5): the owner session's Dishes are not read for the owner participant.
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(
            BuildOwnerSession(lines: [Line("session-dish", 5)]),
            BuildSession("session-a", "Andrii", lines: [Line("dish2", 1)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.Created, Line("dish1", 2)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 50m), BuildDish("session-dish", 7m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dish1", 2) }, ItemsOf(owner));
        Assert.Equal(200m, owner.Total);
        Assert.Equal(250m, result.Total);

        // One dish lookup covers the order's and the guests' dishes, without the owner session's dishes.
        _mockDishRepository.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Once);
        _mockDishRepository.Verify(
            r => r.GetDishesByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.ToHashSet().SetEquals(new[] { "dish1", "dish2" }))),
            Times.Once);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_SubmittedGroup_OwnerLinesAreOrderMinusSubmittedGuestLines()
    {
        // T5 (AC4): the order holds the owner's dishA×2, dishB×1 plus the merged guest cart dishB×1, dishC×3.
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation());
        SetupSessions(
            BuildOwnerSession(),
            BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dishB", 1), Line("dishC", 3)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.Submitted, Line("dishA", 2), Line("dishB", 2), Line("dishC", 3)));
        SetupDishes(BuildDish("dishA", 10m), BuildDish("dishB", 20m), BuildDish("dishC", 5m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        List<Participant> participants = result.Participants.ToList();
        Assert.Equal(2, participants.Count);

        Participant owner = participants[0];
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dishA", 2), ("dishB", 1) }, ItemsOf(owner));
        Assert.Equal(40m, owner.Total);
        Assert.True(owner.IsSubmitted);

        Participant guest = participants[1];
        Assert.Equal("session-a", guest.Id);
        Assert.Equal(new[] { ("dishB", 1), ("dishC", 3) }, ItemsOf(guest));
        Assert.Equal(35m, guest.Total);
        Assert.True(guest.IsSubmitted);

        Assert.Equal(75m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_SubmittedGroupDevelopExample_OwnerShowsOnlyOwnThreeDishes()
    {
        // AC4 example from develop: owner's 3 dishes ($39.50) + the submitted guest's 3 dishes ($46.49) merged into the order.
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation());
        SetupSessions(
            BuildOwnerSession(),
            BuildSession(
                "session-a",
                "Andrii",
                isSubmitted: true,
                lines: [Line("caesar-salad", 1), Line("mushroom-soup", 1), Line("tiramisu", 1)]));
        SetupOwnerOrder(BuildOrder(
            OrderStatus.Submitted,
            Line("margherita-pizza", 1),
            Line("apple-tart", 1),
            Line("beef-carpaccio", 1),
            Line("caesar-salad", 1),
            Line("mushroom-soup", 1),
            Line("tiramisu", 1)));
        SetupDishes(
            BuildDish("margherita-pizza", 14.50m),
            BuildDish("apple-tart", 9.00m),
            BuildDish("beef-carpaccio", 16.00m),
            BuildDish("caesar-salad", 18.99m),
            BuildDish("mushroom-soup", 12.50m),
            BuildDish("tiramisu", 15.00m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("apple-tart", 1), ("beef-carpaccio", 1), ("margherita-pizza", 1) }, ItemsOf(owner));
        Assert.Equal(39.50m, owner.Total);

        Participant guest = result.Participants.Single(p => p.Id == "session-a");
        Assert.Equal(46.49m, guest.Total);

        Assert.Equal(85.99m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_SubmittedGroupSeveralSubmittedGuests_SubtractsSumPerDishAndDropsLinesBelowZero()
    {
        // AC4: dishB = 4 − (1 + 2) = 1; dishC = 1 − 2 = −1 is left out.
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation());
        SetupSessions(
            BuildOwnerSession(),
            BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dishB", 1), Line("dishC", 2)]),
            BuildSession("session-b", "Olena", isSubmitted: true, lines: [Line("dishB", 2)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.Submitted, Line("dishA", 1), Line("dishB", 4), Line("dishC", 1)));
        SetupDishes(BuildDish("dishA", 10m), BuildDish("dishB", 20m), BuildDish("dishC", 5m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dishA", 1), ("dishB", 1) }, ItemsOf(owner));
        Assert.Equal(30m, owner.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_SubmittedGroupUnsubmittedGuest_GuestDishesAreNotSubtracted()
    {
        // T6 (AC4).
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation());
        SetupSessions(
            BuildOwnerSession(),
            BuildSession("session-a", "Andrii", isSubmitted: false, lines: [Line("dishB", 2)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.Submitted, Line("dishA", 1), Line("dishB", 2)));
        SetupDishes(BuildDish("dishA", 10m), BuildDish("dishB", 20m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dishA", 1), ("dishB", 2) }, ItemsOf(owner));
        Assert.Equal(50m, owner.Total);

        Participant guest = result.Participants.Single(p => p.Id == "session-a");
        Assert.Equal(new[] { ("dishB", 2) }, ItemsOf(guest));
        Assert.False(guest.IsSubmitted);

        Assert.Equal(90m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_SubmittedGroupOwnerSessionMarkedSubmitted_OwnerSessionDishesAreNotSubtracted()
    {
        // AC4: only guest sessions (not IsOwnerSession) are subtracted.
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation());
        SetupSessions(BuildSession("owner-session", StoredOwnerName, isOwner: true, isSubmitted: true, lines: [Line("dishA", 2)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.Submitted, Line("dishA", 2)));
        SetupDishes(BuildDish("dishA", 10m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = Assert.Single(result.Participants);
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dishA", 2) }, ItemsOf(owner));
        Assert.Equal(20m, owner.Total);
    }

    [Theory]
    [InlineData(GroupReservationStatus.Open)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task GetGroupSessionsAsync_GroupNotSubmitted_OrderLinesShownWithoutSubtractingSubmittedGuests(GroupReservationStatus groupStatus)
    {
        // AC4 / API contract: Open and Closed groups show the order's lines as they are.
        SetupGroupAndReservation(BuildGroup(groupStatus), BuildReservation());
        SetupSessions(
            BuildOwnerSession(),
            BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dishB", 1)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.SubmittedByCustomer, Line("dishA", 2), Line("dishB", 1)));
        SetupDishes(BuildDish("dishA", 10m), BuildDish("dishB", 20m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dishA", 2), ("dishB", 1) }, ItemsOf(owner));
        Assert.Equal(40m, owner.Total);
        Assert.Equal(60m, result.Total);
    }

    [Theory]
    [InlineData(DishState.OnStop, DishStateLabels.OnStop)]
    [InlineData(DishState.OutOfStock, DishStateLabels.OutOfStock)]
    public async Task GetGroupSessionsAsync_OrderLineWithMissingOrUnavailableDish_MissingOmittedUnavailableListedButNotCounted(
        DishState unavailableState,
        string expectedStateLabel)
    {
        // T7 (AC1).
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(BuildOwnerSession());
        SetupOwnerOrder(BuildOrder(
            OrderStatus.Created,
            Line("dish1", 2),
            Line("dish-unavailable", 3),
            Line("deleted-dish", 4)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish-unavailable", 50m, unavailableState));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant owner = Assert.Single(result.Participants);
        Assert.Equal(new[] { ("dish-unavailable", 3), ("dish1", 2) }, ItemsOf(owner));
        Assert.Equal(expectedStateLabel, owner.Items.Single(i => i.Dish.Id == "dish-unavailable").Dish.State);
        Assert.Equal(200m, owner.Total);
        Assert.Equal(200m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_NoOwnerSession_DoesNotReadOrderAndGuestsAreUnchanged()
    {
        // Implementation constraint 3: no owner session → no owner card and no order read.
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation());
        SetupSessions(BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dish1", 2)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.Submitted, Line("dish1", 2), Line("dish2", 1)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 50m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant guest = Assert.Single(result.Participants);
        Assert.Equal(ParticipantType.Guest, guest.Type);
        Assert.Equal(new[] { ("dish1", 2) }, ItemsOf(guest));
        Assert.Equal(200m, guest.Total);
        Assert.Equal(200m, result.Total);

        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_OwnerFromOrder_GuestParticipantsUnchangedAndResponseTotalIsSumOfParticipants()
    {
        // T8 (AC5): the order shares dishes with the guests; in an Open group neither side changes the other.
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(
            BuildSession("session-a", "Andrii", isSubmitted: true, lines: [Line("dish1", 2), Line("dish2", 1)]),
            BuildOwnerSession(),
            BuildSession("session-b", "Olena", isSubmitted: false, lines: [Line("dish3", 3)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.SubmittedByCustomer, Line("dish1", 1), Line("dish3", 1)));
        SetupDishes(BuildDish("dish1", 180m), BuildDish("dish2", 50m), BuildDish("dish3", 100m));
        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(OwnerUserId))
            .ReturnsAsync(BuildUser(("given_name", "Maria")));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        List<Participant> participants = result.Participants.ToList();
        Assert.Equal(new[] { "owner-session", "session-a", "session-b" }, participants.Select(p => p.Id).ToArray());

        Participant owner = participants[0];
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal("Maria", owner.Name);
        Assert.Equal(new[] { ("dish1", 1), ("dish3", 1) }, ItemsOf(owner));
        Assert.Equal(280m, owner.Total);
        Assert.True(owner.IsSubmitted);

        Participant first = participants[1];
        Assert.Equal(ParticipantType.Guest, first.Type);
        Assert.Equal("Andrii", first.Name);
        Assert.True(first.IsSubmitted);
        Assert.Equal(new[] { ("dish1", 2), ("dish2", 1) }, ItemsOf(first));
        Assert.Equal(410m, first.Total);

        Participant second = participants[2];
        Assert.Equal(ParticipantType.Guest, second.Type);
        Assert.Equal("Olena", second.Name);
        Assert.False(second.IsSubmitted);
        Assert.Equal(new[] { ("dish3", 3) }, ItemsOf(second));
        Assert.Equal(300m, second.Total);

        Assert.Equal(990m, result.Total);
    }

    [Theory]
    [InlineData(OrderStatus.Submitted, false)]
    [InlineData(OrderStatus.Created, true)]
    public async Task GetGroupSessionsAsync_GuestIsSubmitted_ComesFromGuestSessionNotFromOwnerOrder(OrderStatus orderStatus, bool guestIsSubmitted)
    {
        // AC5: the guest's isSubmitted is the session flag, whatever the owner's order status.
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        SetupSessions(
            BuildOwnerSession(),
            BuildSession("session-a", "Andrii", isSubmitted: guestIsSubmitted, lines: [Line("dish1", 1)]));
        SetupOwnerOrder(BuildOrder(orderStatus, Line("dish2", 1)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 50m));

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(OwnerUserId, GroupId);

        Participant guest = result.Participants.Single(p => p.Id == "session-a");
        Assert.Equal(ParticipantType.Guest, guest.Type);
        Assert.Equal(guestIsSubmitted, guest.IsSubmitted);
        Assert.Equal(new[] { ("dish1", 1) }, ItemsOf(guest));
        Assert.Equal(100m, guest.Total);
    }

    // ---- DeleteGuestSessionAsync (S19, S20, AC15, AC16) ----

    [Fact]
    public async Task DeleteGuestSessionAsync_GroupReservationNotFound_ThrowsAndDeletesNothing()
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(GroupId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, "session-a"));

        VerifyNoSessionDeleted();
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_ReservationNotFound_ThrowsAndDeletesNothing()
    {
        SetupGroupAndReservation(BuildGroup(), null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, "session-a"));

        VerifyNoSessionDeleted();
    }

    [Theory]
    [InlineData("another-user")]
    [InlineData(null)]
    public async Task DeleteGuestSessionAsync_CallerIsNotReservationOwner_ThrowsActionForbiddenAndDeletesNothing(string? reservationCustomerId)
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation(customerId: reservationCustomerId));
        GuestSession session = BuildSession("session-a", "Andrii");
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(session.Id)).ReturnsAsync(session);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, session.Id));

        VerifyNoSessionDeleted();
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_CallerIsNotOwnerOfSubmittedGroup_ThrowsActionForbiddenBeforeStatusCheck()
    {
        SetupGroupAndReservation(BuildGroup(GroupReservationStatus.Submitted), BuildReservation(customerId: "another-user"));

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, "session-a"));

        VerifyNoSessionDeleted();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted, ReservationStatus.Reserved, typeof(GroupReservationSubmittedException))]
    [InlineData(GroupReservationStatus.Closed, ReservationStatus.Reserved, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Cancelled, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.InProgress, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Completed, typeof(GroupReservationExpiredException))]
    public async Task DeleteGuestSessionAsync_GroupOrReservationNotActive_ThrowsBeforeSessionLookupAndDeletesNothing(
        GroupReservationStatus groupStatus,
        ReservationStatus reservationStatus,
        Type expectedException)
    {
        SetupGroupAndReservation(BuildGroup(groupStatus), BuildReservation(reservationStatus));
        // The session does not exist either: the status check must come first (spec check order 4 before 5).
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId("session-a"))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync(expectedException, () =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, "session-a"));

        VerifyNoSessionDeleted();
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_GuestSessionNotFound_ThrowsGuestSessionNotFoundAndDeletesNothing()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId("missing-session"))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, "missing-session"));

        VerifyNoSessionDeleted();
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_SessionOfAnotherGroup_ThrowsValidationExceptionAndDeletesNothing()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        GuestSession foreignSession = BuildSession("session-x", "Stranger", groupId: OtherGroupId);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(foreignSession.Id)).ReturnsAsync(foreignSession);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, foreignSession.Id));

        VerifyNoSessionDeleted();
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_OwnerSession_ThrowsValidationExceptionAndDeletesNothing()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        GuestSession ownerSession = BuildOwnerSession();
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(ownerSession.Id)).ReturnsAsync(ownerSession);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, ownerSession.Id));

        VerifyNoSessionDeleted();
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_GuestSession_DeletesItAndReturnsSnapshotOfRemainingSessions()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        GuestSession deleted = BuildSession("session-a", "Andrii", lines: [Line("dish1", 4)]);
        GuestSession remaining = BuildSession("session-b", "Olena", isSubmitted: true, lines: [Line("dish1", 1)]);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(deleted.Id)).ReturnsAsync(deleted);
        // The index read after the delete may still return the deleted session (eventual consistency).
        SetupSessions(deleted, remaining, BuildOwnerSession());
        SetupDishes(BuildDish("dish1", 100m));
        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(OwnerUserId))
            .ReturnsAsync(BuildUser(("given_name", "Maria")));

        GetAllSessionsResponse result = await _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, deleted.Id);

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(deleted), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.Is<GuestSession>(s => s.Id != deleted.Id)), Times.Never);

        List<Participant> participants = result.Participants.ToList();
        Assert.Equal(new[] { "owner-session", "session-b" }, participants.Select(p => p.Id).ToArray());
        Assert.Equal(ParticipantType.Owner, participants[0].Type);
        Assert.Equal("Maria", participants[0].Name);
        Assert.Equal(ParticipantType.Guest, participants[1].Type);
        Assert.Equal("Olena", participants[1].Name);
        Assert.True(participants[1].IsSubmitted);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_RemainingSessionWithUnavailableAndMissingDishes_AppliesSnapshotRules()
    {
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        GuestSession deleted = BuildSession("session-a", "Andrii");
        GuestSession remaining = BuildSession(
            "session-b",
            "Olena",
            lines: [Line("dish1", 2), Line("dish-on-stop", 1), Line("deleted-dish", 3)]);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(deleted.Id)).ReturnsAsync(deleted);
        SetupSessions(remaining);
        SetupDishes(BuildDish("dish1", 40m), BuildDish("dish-on-stop", 500m, DishState.OnStop));

        GetAllSessionsResponse result = await _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, deleted.Id);

        Participant participant = Assert.Single(result.Participants);
        Assert.Equal(new[] { ("dish-on-stop", 1), ("dish1", 2) }, ItemsOf(participant));
        Assert.Equal(80m, participant.Total);
        Assert.Equal(80m, result.Total);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_OwnerSessionRemains_OwnerParticipantBuiltFromOrder()
    {
        // T9 (AC6): docs/specs/2026-11-02-owner-preorder-in-group.md.
        SetupGroupAndReservation(BuildGroup(), BuildReservation());
        GuestSession deleted = BuildSession("session-a", "Andrii", lines: [Line("dish1", 4)]);
        GuestSession remaining = BuildSession("session-b", "Olena", isSubmitted: true, lines: [Line("dish1", 1)]);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(deleted.Id)).ReturnsAsync(deleted);
        SetupSessions(deleted, remaining, BuildOwnerSession(lines: [Line("session-dish", 9)]));
        SetupOwnerOrder(BuildOrder(OrderStatus.SubmittedByCustomer, Line("dish2", 2)));
        SetupDishes(BuildDish("dish1", 100m), BuildDish("dish2", 30m), BuildDish("session-dish", 7m));

        GetAllSessionsResponse result = await _guestSessionService.DeleteGuestSessionAsync(OwnerUserId, GroupId, deleted.Id);

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(deleted), Times.Once);

        List<Participant> participants = result.Participants.ToList();
        Assert.Equal(new[] { "owner-session", "session-b" }, participants.Select(p => p.Id).ToArray());

        Participant owner = participants[0];
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(new[] { ("dish2", 2) }, ItemsOf(owner));
        Assert.Equal(60m, owner.Total);
        Assert.True(owner.IsSubmitted);

        Participant guest = participants[1];
        Assert.Equal(new[] { ("dish1", 1) }, ItemsOf(guest));
        Assert.Equal(100m, guest.Total);
        Assert.True(guest.IsSubmitted);

        Assert.Equal(160m, result.Total);
        VerifyOwnerOrderReadOnceForReservation();
    }

    // ---- GetOrCreateOwnerSessionIdAsync (S8–S11, S9a, S9b, AC7, AC8, AC20) ----

    [Fact]
    public async Task GetOrCreateOwnerSessionIdAsync_ReservationNotFound_ThrowsReservationNotFoundAndCreatesNothing()
    {
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId));

        VerifyNoOwnerSessionCreated();
    }

    [Theory]
    [InlineData("another-user")]
    [InlineData(null)]
    public async Task GetOrCreateOwnerSessionIdAsync_CallerIsNotReservationOwner_ThrowsActionForbiddenBeforeGroupLookup(string? reservationCustomerId)
    {
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(BuildReservation(customerId: reservationCustomerId));
        // No group either: the ownership check must come first (403 before 404).
        _mockGroupReservationRepository
            .Setup(r => r.GetByReservationIdAsync(ReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId));

        VerifyNoOwnerSessionCreated();
    }

    [Fact]
    public async Task GetOrCreateOwnerSessionIdAsync_NoGroupReservation_ThrowsGroupReservationNotFoundAndCreatesNothing()
    {
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(ReservationId))
            .ReturnsAsync(BuildReservation());
        _mockGroupReservationRepository
            .Setup(r => r.GetByReservationIdAsync(ReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId));

        VerifyNoOwnerSessionCreated();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Reserved)]
    [InlineData(GroupReservationStatus.Submitted, ReservationStatus.Reserved)]
    [InlineData(GroupReservationStatus.Closed, ReservationStatus.Cancelled)]
    public async Task GetOrCreateOwnerSessionIdAsync_OwnerSessionExists_ReturnsItsIdWithoutCreating(
        GroupReservationStatus groupStatus,
        ReservationStatus reservationStatus)
    {
        // S8 (AC8, AC20): the session owner-{groupReservationId} exists, whatever the group or reservation status.
        SetupOwnerSessionLookup(groupStatus, reservationStatus, storedOwnerSession: BuildStoredOwnerSession());

        OwnerGuestSessionIdResponse result = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId);

        Assert.Equal(OwnerSessionId, result.GuestSessionId);
        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionIdConsistentAsync(OwnerSessionId), Times.Once);
        VerifyNoOwnerSessionCreated();
        VerifyGroupSessionListNotRead();
    }

    [Fact]
    public async Task GetOrCreateOwnerSessionIdAsync_NoOwnerSessionAndGroupActive_CreatesOwnerSessionWithDeterministicIdThroughConditionalCreate()
    {
        // S9 (AC8, AC20).
        SetupOwnerSessionLookup();

        var created = new List<GuestSession>();
        _mockGuestSessionRepository
            .Setup(r => r.TryCreateAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(created.Add)
            .ReturnsAsync(true);

        OwnerGuestSessionIdResponse result = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId);

        GuestSession session = Assert.Single(created);
        Assert.Equal(OwnerSessionId, session.Id);
        Assert.True(session.IsOwnerSession);
        Assert.Equal(OwnerUserId, session.OwnerUserId);
        Assert.Equal(GroupId, session.GroupReservationId);
        Assert.Empty(session.Dishes);
        Assert.Null(session.SessionTokenHash);
        Assert.False(session.IsSubmitted);
        Assert.Equal(session.Id, result.GuestSessionId);

        // The create goes only through the conditional put.
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        VerifyGroupSessionListNotRead();
    }

    [Fact]
    public async Task GetOrCreateOwnerSessionIdAsync_ConditionalCreateReportsSessionAlreadyExists_ReturnsDeterministicIdWithoutAnotherCreate()
    {
        // S9a (AC20): a concurrent call created the session between the lookup and the conditional create.
        SetupOwnerSessionLookup();
        _mockGuestSessionRepository
            .Setup(r => r.TryCreateAsync(It.IsAny<GuestSession>()))
            .ReturnsAsync(false);

        OwnerGuestSessionIdResponse result = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId);

        Assert.Equal(OwnerSessionId, result.GuestSessionId);
        // Exactly one create attempt in total, and it was for the deterministic id.
        _mockGuestSessionRepository.Verify(r => r.TryCreateAsync(It.IsAny<GuestSession>()), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.TryCreateAsync(It.Is<GuestSession>(s => s.Id == OwnerSessionId)), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateOwnerSessionIdAsync_GroupSessionListHasOwnerSessionWithAnotherId_DoesNotReadListAndCreatesDeterministicSession()
    {
        // S9b (AC20): no lookup through the group's session list and no fallback for owner sessions with random ids.
        SetupOwnerSessionLookup();
        SetupSessions(BuildSession("session-a", "Andrii"), BuildOwnerSession());
        _mockGuestSessionRepository
            .Setup(r => r.TryCreateAsync(It.IsAny<GuestSession>()))
            .ReturnsAsync(true);

        OwnerGuestSessionIdResponse result = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId);

        Assert.Equal(OwnerSessionId, result.GuestSessionId);
        _mockGuestSessionRepository.Verify(r => r.TryCreateAsync(It.IsAny<GuestSession>()), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.TryCreateAsync(It.Is<GuestSession>(s => s.Id == OwnerSessionId)), Times.Once);
        VerifyGroupSessionListNotRead();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted, ReservationStatus.Reserved, typeof(GroupReservationSubmittedException))]
    [InlineData(GroupReservationStatus.Closed, ReservationStatus.Reserved, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Cancelled, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.InProgress, typeof(GroupReservationExpiredException))]
    [InlineData(GroupReservationStatus.Open, ReservationStatus.Completed, typeof(GroupReservationExpiredException))]
    public async Task GetOrCreateOwnerSessionIdAsync_NoOwnerSessionAndGroupNotActive_ThrowsAndCreatesNothing(
        GroupReservationStatus groupStatus,
        ReservationStatus reservationStatus,
        Type expectedException)
    {
        // S10 (AC8).
        SetupOwnerSessionLookup(groupStatus, reservationStatus, storedOwnerSession: null);

        await Assert.ThrowsAsync(expectedException, () =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(OwnerUserId, ReservationId));

        VerifyNoOwnerSessionCreated();
    }

    // ---- Owner session excluded from guest counts (S12, AC9) ----
    // These service tests check the guest limit and participant count against the repository's
    // joined-guest count (CountJoinedGuestsByGroupReservationIdAsync), mocked here as the value the
    // repository returns for a group that also has an owner session. Excluding owner sessions from
    // that count is covered by GuestSessionRepositoryTests.

    [Fact]
    public async Task CreateGuestSessionAsync_OwnerSessionAndJoinedGuestsOneBelowLimit_CreatesSession()
    {
        // Capacity 4 = owner + 3 guests; the repository reports 2 joined guests (owner session not counted).
        SetupGroupAndReservation(BuildGroup(), BuildReservation(guestCount: 4));
        _mockGuestSessionRepository
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(GroupId))
            .ReturnsAsync(2);

        CreatGuestSessionResponse response = await _guestSessionService.CreateGuestSessionAsync(
            new CreateGuestSessionRequest(GroupId, "Taras"));

        Assert.False(string.IsNullOrEmpty(response.SessionId));
        _mockGuestSessionRepository.Verify(
            r => r.SaveAsync(It.Is<GuestSession>(s => s.GuestName == "Taras" && !s.IsOwnerSession && s.GroupReservationId == GroupId)),
            Times.Once);
    }

    [Fact]
    public async Task CreateGuestSessionAsync_OwnerSessionAndJoinedGuestsAtLimit_ThrowsGuestLimitExceeded()
    {
        // Capacity 4 = owner + 3 guests; the repository reports 3 joined guests (owner session not counted).
        SetupGroupAndReservation(BuildGroup(), BuildReservation(guestCount: 4));
        _mockGuestSessionRepository
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(GroupId))
            .ReturnsAsync(3);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() =>
            _guestSessionService.CreateGuestSessionAsync(new CreateGuestSessionRequest(GroupId, "Taras")));

        VerifyNoSessionSaved();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task GetGroupReservationStatusAsync_OwnerSessionExists_ReportsJoinedGuestsPlusOwner(int joinedGuests, int expectedParticipants)
    {
        // The repository reports only joined guests; the service adds the owner to the participant count.
        SetupGroupAndReservation(BuildGroup(), BuildReservation(guestCount: 4));
        _mockGuestSessionRepository
            .Setup(r => r.CountJoinedGuestsByGroupReservationIdAsync(GroupId))
            .ReturnsAsync(joinedGuests);

        GroupReservationStatusResponse result = await _guestSessionService.GetGroupReservationStatusAsync(GroupId);

        Assert.Equal(expectedParticipants, result.CurrentParticipants);
        Assert.Equal(4, result.MaxCapacity);
        Assert.Equal(GroupReservationStatus.Open, result.Status);
    }
}





