using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using Moq;
using System.Text.Json;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

/// <summary>
/// <c>GET /reservations/{reservationId}/invite-token</c> (spec 2026-11-02-group-preorder-bugfixes, H1–H5).
/// </summary>
public class GetInviteTokenHandlerTests
{
    private const string ReservationId = "reservation-1";

    private readonly Mock<IGroupReservationService> _mockGroupReservationService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly GetInviteTokenHandler _handler;

    public GetInviteTokenHandlerTests()
    {
        _handler = new GetInviteTokenHandler(_mockGroupReservationService.Object);
    }

    private static Dictionary<string, string> PathWithReservation() => new() { ["reservationId"] = ReservationId };

    private void VerifyServiceNotCalled() => _mockGroupReservationService.Verify(
        s => s.GetGroupReservationInviteTokenAsync(It.IsAny<string>(), It.IsAny<string>()),
        Times.Never);

    private Task<APIGatewayProxyResponse> HandleWithServiceThrowing(Exception exception)
    {
        _mockGroupReservationService
            .Setup(s => s.GetGroupReservationInviteTokenAsync(CallerId, ReservationId))
            .ThrowsAsync(exception);

        return _handler.HandleRequest(CreateRequest(PathWithReservation()), _context);
    }

    // ---- H5: success (AC6, AC7) ----

    [Fact]
    public async Task HandleRequest_OwnerRequestsToken_Returns200WithCamelCaseTokenAndPassesCallerIdAndReservationId()
    {
        _mockGroupReservationService
            .Setup(s => s.GetGroupReservationInviteTokenAsync(CallerId, ReservationId))
            .ReturnsAsync(new GetGroupInviteTokenResponse("invite-token-1"));

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(PathWithReservation()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(new[] { "token" }, PropertyNames(body));
        Assert.Equal("invite-token-1", body.GetProperty("token").GetString());

        _mockGroupReservationService.Verify(s => s.GetGroupReservationInviteTokenAsync(CallerId, ReservationId), Times.Once);
    }

    // ---- H1: no caller id (AC6) ----

    [Theory]
    [InlineData(MissingSubjectCase.AuthorizerNull)]
    [InlineData(MissingSubjectCase.ClaimsNull)]
    [InlineData(MissingSubjectCase.SubClaimMissing)]
    [InlineData(MissingSubjectCase.SubEmpty)]
    [InlineData(MissingSubjectCase.SubWhitespace)]
    public async Task HandleRequest_NoAuthenticatedSubject_Returns401AndDoesNotCallService(MissingSubjectCase missingSubjectCase)
    {
        APIGatewayProxyRequest request = CreateRequest(PathWithReservation());
        request.RequestContext = ContextWithout(missingSubjectCase);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 401, "Unauthorized.");
        VerifyServiceNotCalled();
    }

    // ---- H2: caller is not the owner (AC6) ----

    [Fact]
    public async Task HandleRequest_CallerIsNotOwner_Returns403()
    {
        var exception = new ActionForbiddenException("Only the reservation owner can perform this action.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 403, "Only the reservation owner can perform this action.");
    }

    // ---- H3: reservation without a group (AC8) ----

    [Fact]
    public async Task HandleRequest_GroupReservationNotFound_Returns404()
    {
        // GroupReservationNotFoundException is a ValidationException: it must not fall through to the 400 mapping.
        var exception = new GroupReservationNotFoundException();

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 404, exception.Message);
    }

    // ---- H4: other domain errors (AC7) ----

    [Fact]
    public async Task HandleRequest_ReservationNotFound_Returns404()
    {
        var exception = new ReservationNotFoundException("Reservation reservation-1 was not found.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 404, exception.Message);
    }

    [Theory]
    [InlineData("The group reservation is expired.")]
    [InlineData(null)]
    public async Task HandleRequest_ReservationNotReservedOrGroupClosed_Returns400WithGroupReservationExpiredCode(string? message)
    {
        // The first case is the reservation that is not Reserved; the second is the Closed group (default message).
        GroupReservationExpiredException exception = message is null
            ? new GroupReservationExpiredException()
            : new GroupReservationExpiredException(message);

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "GroupReservationExpired", exception.Message);
    }

    [Fact]
    public async Task HandleRequest_LessThanTwoGuests_Returns400WithUnknownErrorCode()
    {
        var exception = new ValidationException("Retrieving invitation token for the reservation with only one guest is not permitted.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "Unknown", exception.Message);
    }
}
