using System;

namespace APIHandler.Helpers;

/// <summary>
/// The pre-order cutoff: a pre-order can be changed only up to 30 minutes before the reservation start.
/// </summary>
public static class PreOrderCutoffHelper
{
    public const int MinutesBeforeReservationStart = 30;

    /// <summary>
    /// Returns true when the current UTC time is later than the reservation start time (UTC) minus 30 minutes.
    /// A call exactly at the cutoff is still before it.
    /// </summary>
    public static bool IsPastCutoff(DateTime reservationStartTime) =>
        DateTime.UtcNow > reservationStartTime.ToUniversalTime().AddMinutes(-MinutesBeforeReservationStart);
}
