using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Services.Waiters;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Locations;

public class GetLocationWaitersHandler(IWaiterService waiterService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!RoleHelper.IsWaiter(request))
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden", context.Logger);
        }

        if (!request.TryGetPathValue("id", out string? locationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Location id is required.", context.Logger);
        }

        string? userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden", context.Logger);
        }

        // A waiter may list only the waiters of the location they are assigned to.
        string? callerLocationId = await waiterService.GetWaiterLocationIdAsync(userId);
        if (!string.Equals(callerLocationId, locationId, StringComparison.Ordinal))
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden", context.Logger);
        }

        var waiters = await waiterService.GetWaitersByLocationIdAsync(locationId);
        return ResponseBuilder.CreateSuccessResponse(WaiterMapper.ToLocationWaiterResponseList(waiters));
    }
}
