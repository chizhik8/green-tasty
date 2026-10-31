using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Exceptions;

namespace APIHandler.Services.GroupReservations;

/// <summary>
/// Shared checks for the reservation owner's group pre-order operations.
/// </summary>
internal static class GroupPreOrderGuards
{
    /// <summary>
    /// Throws <see cref="ActionForbiddenException"/> if the caller is not the customer who owns the reservation.
    /// </summary>
    public static void EnsureReservationOwner(Reservation reservation, string userId)
    {
        if (reservation.CustomerId != userId)
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
    }

    /// <summary>
    /// Throws if the group pre-order can no longer be changed: the group is submitted or closed,
    /// or the reservation is not in the <see cref="ReservationStatus.Reserved"/> status.
    /// </summary>
    public static void EnsureGroupIsActive(GroupReservation group, Reservation reservation)
    {
        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        if (reservation.Status != ReservationStatus.Reserved)
            throw new GroupReservationExpiredException($"The reservation is no longer active. Current reservation status: '{reservation.Status}'.");
    }
}
