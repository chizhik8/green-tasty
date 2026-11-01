using APIHandler.Models.WaiterList;
using System.Collections.Generic;
using System.Linq;

namespace APIHandler.Mappers;

public static class WaiterMapper
{
    public static LocationWaiterResponse ToLocationWaiterResponse(WaiterListItem waiter)
    {
        return new LocationWaiterResponse
        {
            UserId = waiter.UserId,
            LocationId = waiter.LocationId,
            CreatedAt = waiter.CreatedAt,
            Reservations = waiter.Reservations ?? []
        };
    }

    public static List<LocationWaiterResponse> ToLocationWaiterResponseList(IEnumerable<WaiterListItem> waiters)
    {
        return waiters.Select(ToLocationWaiterResponse).ToList();
    }
}
