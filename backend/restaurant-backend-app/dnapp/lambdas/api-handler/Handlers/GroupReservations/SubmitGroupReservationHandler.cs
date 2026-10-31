using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

public class SubmitGroupReservationHandler(IGroupReservationService groupReservationService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out string? groupReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'groupReservationId' in path parameters.", context.Logger);
        }

        string? userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
        {
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);
        }

        if (!RequestBodyHelper.TryParseAndValidate(request, context, out SubmitGroupReservationRequest? requestBody, out APIGatewayProxyResponse? errorResponse))
        {
            return errorResponse;
        }

        if (string.IsNullOrWhiteSpace(requestBody.ReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'reservationId' in request body.", context.Logger);
        }

        try
        {
            IReadOnlyList<SubmittedGroupDishResponse> result = await groupReservationService.SubmitGroupReservationAsync(
                userId, groupReservationId, requestBody.ReservationId);
            return ResponseBuilder.CreateJsonResponse(200, result);
        }
        catch (GroupReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ActionForbiddenException ex)
        {
            return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorCodeResponse(ex);
        }
    }
}
