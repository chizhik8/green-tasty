using APIHandler.Mappers;
using APIHandler.Models.WaiterList;

namespace APIHandler.Tests.Mappers;

public class WaiterMapperTests
{
    // AC9: "reservations is [] when the item has none", also for an item read without the attribute.
    [Fact]
    public void ToLocationWaiterResponse_ReservationsNull_MapsToEmptyListAndCopiesOtherFields()
    {
        var createdAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var waiter = new WaiterListItem
        {
            UserId = "waiter-a",
            LocationId = "location-1",
            CreatedAt = createdAt,
            Reservations = null!,
        };

        LocationWaiterResponse response = WaiterMapper.ToLocationWaiterResponse(waiter);

        Assert.NotNull(response.Reservations);
        Assert.Empty(response.Reservations);
        Assert.Equal("waiter-a", response.UserId);
        Assert.Equal("location-1", response.LocationId);
        Assert.Equal(createdAt, response.CreatedAt);
    }
}
