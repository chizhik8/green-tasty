using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Helpers;

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
    /// Throws if the group itself is no longer open: <see cref="GroupReservationSubmittedException"/> when it is
    /// <see cref="GroupReservationStatus.Submitted"/>, <see cref="GroupReservationExpiredException"/> when it is
    /// <see cref="GroupReservationStatus.Closed"/>. Checks the group status only.
    /// </summary>
    public static void EnsureGroupIsOpen(GroupReservation group)
    {
        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();
    }

    /// <summary>
    /// Throws if the group pre-order can no longer be changed: the group is submitted or closed,
    /// or the reservation is not in the <see cref="ReservationStatus.Reserved"/> status.
    /// </summary>
    public static void EnsureGroupIsActive(GroupReservation group, Reservation reservation)
    {
        EnsureGroupIsOpen(group);

        if (reservation.Status != ReservationStatus.Reserved)
            throw new GroupReservationExpiredException($"The reservation is no longer active. Current reservation status: '{reservation.Status}'.");
    }

    /// <summary>
    /// Throws <see cref="GroupReservationExpiredException"/> if the pre-order cutoff (30 minutes before
    /// the reservation start) has passed. Same rule and boundary as the individual pre-order in <c>OrderService</c>.
    /// </summary>
    public static void EnsureBeforePreOrderCutoff(Reservation reservation)
    {
        if (PreOrderCutoffHelper.IsPastCutoff(reservation.StartTime))
            throw new GroupReservationExpiredException(
                $"The group pre-order can only be submitted up to {PreOrderCutoffHelper.MinutesBeforeReservationStart} minutes before the reservation start time.");
    }
}
