using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.WaiterList;

public class LocationWaiterResponse
{
    [JsonPropertyName("userId")]
    public required string UserId { get; set; }

    [JsonPropertyName("locationId")]
    public required string LocationId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("reservations")]
    public List<string> Reservations { get; set; } = [];
}
