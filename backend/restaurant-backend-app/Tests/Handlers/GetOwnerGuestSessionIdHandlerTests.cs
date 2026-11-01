using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

public class GetOwnerGuestSessionIdHandlerTests
{
    private const string ReservationId = "reservation-1";

    private readonly Mock<IGuestSessionService> _mockGuestSessionService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly GetOwnerGuestSessionIdHandler _handler;

    public GetOwnerGuestSessionIdHandlerTests()
    {
        _handler = new GetOwnerGuestSessionIdHandler(_mockGuestSessionService.Object);
    }

    private static Dictionary<string, string> PathWithReservation() => new() { ["reservationId"] = ReservationId };

    private Task<APIGatewayProxyResponse> HandleWithServiceThrowing(Exception exception)
    {
        _mockGuestSessionService
            .Setup(s => s.GetOrCreateOwnerSessionIdAsync(CallerId, ReservationId))
            .ThrowsAsync(exception);

        return _handler.HandleRequest(CreateRequest(PathWithReservation()), _context);
    }

    [Fact]
    public async Task HandleRequest_OwnerRequest_Returns200WithGuestSessionId()
    {
        _mockGuestSessionService
            .Setup(s => s.GetOrCreateOwnerSessionIdAsync(CallerId, ReservationId))
            .ReturnsAsync(new OwnerGuestSessionIdResponse("owner-session-123"));

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(PathWithReservation()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(new[] { "guestSessionId" }, PropertyNames(body));
        Assert.Equal("owner-session-123", body.GetProperty("guestSessionId").GetString());
        _mockGuestSessionService.Verify(s => s.GetOrCreateOwnerSessionIdAsync(CallerId, ReservationId), Times.Once);
    }

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
        _mockGuestSessionService.Verify(s => s.GetOrCreateOwnerSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleRequest_MissingReservationId_Returns400AndDoesNotCallService(bool pathParametersNull)
    {
        Dictionary<string, string>? path = pathParametersNull ? null : new() { ["reservationId"] = string.Empty };

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(path), _context);

        AssertErrorResponse(response, 400);
        _mockGuestSessionService.Verify(s => s.GetOrCreateOwnerSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleRequest_ReservationNotFound_Returns404()
    {
        var exception = new ReservationNotFoundException("Reservation reservation-1 was not found.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 404, exception.Message);
    }

    [Fact]
    public async Task HandleRequest_GroupReservationNotFound_Returns404()
    {
        var exception = new GroupReservationNotFoundException();

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 404, exception.Message);
    }

    [Fact]
    public async Task HandleRequest_CallerIsNotOwner_Returns403()
    {
        var exception = new ActionForbiddenException("Only the reservation owner can perform this action.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 403, exception.Message);
    }

    [Fact]
    public async Task HandleRequest_GroupSubmitted_Returns400WithGroupReservationSubmittedCode()
    {
        var exception = new GroupReservationSubmittedException();

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "GroupReservationSubmitted", exception.Message);
    }

    [Fact]
    public async Task HandleRequest_GroupExpired_Returns400WithGroupReservationExpiredCode()
    {
        var exception = new GroupReservationExpiredException("The reservation is no longer active.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "GroupReservationExpired", exception.Message);
    }
}
