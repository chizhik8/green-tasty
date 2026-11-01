using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestReservation;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public class GroupReservationService(
    IGroupReservationRepository groupReservationRepository,
    ITableRepository tableRepository,
    IReservationRepository reservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IUserService userService,
    ILocationService locationService,
    IDishRepository dishRepository,
    IOrderRepository orderRepository) : IGroupReservationService
{
    private const string DefaultRestaurantName = "Green & Tasty";

    public async Task<GetGroupReservationResponse> GetGroupReservationByTokenAsync(string token)
    {
        GroupReservation groupReservation = await groupReservationRepository.GetGroupReservationByTokenAsync(token)
            ?? throw new GroupReservationNotFoundException();

        if (groupReservation.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException();

        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(groupReservation.ReservationId);
        // Owner sessions are not joined guests and do not count toward the guest limit.
        Task<int> joinedGuestsCountTask = guestSessionRepository.CountJoinedGuestsByGroupReservationIdAsync(groupReservation.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new ValidationException("Associated reservation not found.");
        int joinedGuestsCount = await joinedGuestsCountTask;

        if (joinedGuestsCount + 1 >= reservation.GuestCount)
            throw new GuestLimitExceededException();

        Table table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new InvalidOperationException("Associated table not found.");

        Location? location = await locationService.GetLocationByIdAsync(table.LocationId);
        string restaurantName = DefaultRestaurantName;
        string address = location?.Address ?? "Unknown Address";

        string inviterName = "Unknown";
        if (!string.IsNullOrWhiteSpace(reservation.CustomerId))
        {
            inviterName = await userService.GetUserName(reservation.CustomerId) ?? "Unknown";
        }
        return new GetGroupReservationResponse(
            groupReservation.GroupReservationId,
            table.LocationId,
            restaurantName,
            address,
            reservation.StartTime,
            reservation.EndTime,
            reservation.GuestCount,
            table.TableNumber,
            inviterName);
    }

    public async Task<GroupReservation> GetGroupReservationAsync(string groupReservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        return groupReservation ?? throw new InvalidOperationException($"Group reservation with ID {groupReservationId} not found.");
    }

    public async Task<GroupReservation> CreateGroupReservationAsync(string reservationId)
    {
        var groupReservation = new GroupReservation
        {
            GroupReservationId = Guid.NewGuid().ToString(),
            ReservationId = reservationId,
            ReservationToken = Guid.NewGuid().ToString("N"),
            Status = GroupReservationStatus.Open,
        };

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
        return groupReservation;
    }

    public async Task CreateGroupReservationIfDoesNotExistAsync(string userId, string reservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (group is not null)
            return;

        await CreateGroupReservationAsync(reservationId);
    }

    public async Task UpdateGroupReservationStatusAsync(string groupReservationId, GroupReservationStatus status)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        groupReservation.Status = status;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task DeleteGroupReservationAsync(string groupReservationId)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        await groupReservationRepository.DeleteGroupReservationAsync(groupReservation);
    }

    public async Task CloseGroupReservationAsync(string reservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (groupReservation is null)
            return;

        groupReservation.Status = GroupReservationStatus.Closed;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task<GetGroupInviteTokenResponse> GetGroupReservationInviteTokenAsync(string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        if (reservation.GuestCount < 2)
            throw new ValidationException("Retrieving invitation token for the reservation with only one guest is not permitted.");

        if (reservation.Status != ReservationStatus.Reserved)
            throw new GroupReservationExpiredException("The group reservation is expired.");

        GroupReservation groupReservation = (await groupReservationRepository.GetByReservationIdAsync(reservationId))!;

        if (groupReservation.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (groupReservation.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        return new GetGroupInviteTokenResponse(groupReservation.ReservationToken);
    }

    public async Task<IReadOnlyList<SubmittedGroupDishResponse>> SubmitGroupReservationAsync(string userId, string groupReservationId, string reservationId)
    {
        GroupReservation groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        if (groupReservation.ReservationId != reservationId)
            throw new ValidationException($"Reservation '{reservationId}' does not belong to group reservation '{groupReservationId}'.");

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(groupReservation.ReservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        GroupPreOrderGuards.EnsureReservationOwner(reservation, userId);

        GroupPreOrderGuards.EnsureGroupIsActive(groupReservation, reservation);

        GroupPreOrderGuards.EnsureBeforePreOrderCutoff(reservation);

        List<GuestSession> submittedSessions = (await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId) ?? [])
            .Where(s => s.IsSubmitted)
            .ToList();

        if (submittedSessions.Count == 0)
            throw new ValidationException("No participant has submitted a cart yet.");

        List<OrderItem> submittedLines = submittedSessions
            .SelectMany(s => s.Dishes ?? [])
            .ToList();

        string[] dishIds = submittedLines
            .Select(l => l.DishId)
            .Distinct()
            .ToArray();

        HashSet<string> availableDishIds = (await dishRepository.GetDishesByIdsAsync(dishIds) ?? [])
            .Where(d => d.State == DishState.Available)
            .Select(d => d.Id)
            .ToHashSet();

        // Dishes that no longer exist or are not available are left out of the consolidated pre-order.
        List<SubmittedGroupDishResponse> consolidatedDishes = submittedLines
            .Where(l => availableDishIds.Contains(l.DishId))
            .GroupBy(l => l.DishId)
            .Select(g => new SubmittedGroupDishResponse(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        if (consolidatedDishes.Count == 0)
            throw new ValidationException("The submitted carts contain no available dishes.");

        var order = await orderRepository.GetOrderByReservationIdAsync(reservation.Id)
            ?? throw new ValidationException($"No active order was found for reservation '{reservation.Id}'.");

        order.Dishes ??= [];

        foreach (SubmittedGroupDishResponse consolidatedDish in consolidatedDishes)
        {
            OrderItem? existingItem = order.Dishes.FirstOrDefault(d => d.DishId == consolidatedDish.DishId);

            if (existingItem is not null)
            {
                existingItem.Quantity += consolidatedDish.Quantity;
            }
            else
            {
                order.Dishes.Add(new OrderItem
                {
                    DishId = consolidatedDish.DishId,
                    Quantity = consolidatedDish.Quantity,
                    State = OrderItemState.Pending
                });
            }
        }

        order.Status = OrderStatus.SubmittedByCustomer;
        groupReservation.Status = GroupReservationStatus.Submitted;

        // One transaction, conditioned on the group still being Open: a concurrent or repeated submit
        // writes nothing, so the dishes are never added to the order twice.
        bool written = await groupReservationRepository.TryUpdateStatusWithOrderAsync(groupReservation, GroupReservationStatus.Open, order);
        if (!written)
            await ThrowGroupNoLongerOpenAsync(groupReservationId);

        return consolidatedDishes;
    }

    /// <summary>
    /// Called when the submit transaction was canceled because the group was no longer <c>Open</c>:
    /// re-reads the group and throws the error matching its current status.
    /// </summary>
    private async Task ThrowGroupNoLongerOpenAsync(string groupReservationId)
    {
        GroupReservation? current = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);

        if (current is not null)
            GroupPreOrderGuards.EnsureGroupIsOpen(current);

        throw new InvalidOperationException(
            $"The submit of group reservation '{groupReservationId}' was rejected because the group is no longer open, but its current status is '{current?.Status.ToString() ?? "not found"}'.");
    }
}

