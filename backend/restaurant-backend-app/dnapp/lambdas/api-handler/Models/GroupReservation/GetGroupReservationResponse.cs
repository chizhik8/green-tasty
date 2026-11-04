using APIHandler.Entities;
using System;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestReservation;

/// <summary>
/// Invite details for <c>GET /guest/invites/{token}</c>. <c>Status</c> is <see cref="GroupReservationStatus.Open"/>
/// or <see cref="GroupReservationStatus.Submitted"/>, serialized as a string; <c>AvailableSpots</c> is how many more
/// guests can join now (always <c>0</c> for a submitted group).
/// </summary>
public record GetGroupReservationResponse(
    string GroupReservationId,
    string LocationId,
    string RestaurantName,
    string LocationAddress,
    DateTime StartTime,
    DateTime EndTime,
    int MaxGuestCount,
    int TableNumber,
    string InviterName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<GroupReservationStatus>))] GroupReservationStatus Status,
    int AvailableSpots
);
