using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Services.GuestSessions;
using Moq;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

public class DeleteGuestSessionHandlerTests
{
    private const string GroupId = "group-1";
    private const string GuestSessionId = "session-a";

    private readonly Mock<IGuestSessionService> _mockGuestSessionService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly DeleteGuestSessionHandler _handler;

    public DeleteGuestSessionHandlerTests()
    {
        _handler = new DeleteGuestSessionHandler(_mockGuestSessionService.Object);
    }

    private static Dictionary<string, string> FullPath() => new()
    {
        ["groupReservationId"] = GroupId,
        ["guestSessionId"] = GuestSessionId,
    };

    private void VerifyServiceNotCalled() => _mockGuestSessionService.Verify(
        s => s.DeleteGuestSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
        Times.Never);

    private Task<APIGatewayProxyResponse> HandleWithServiceThrowing(Exception exception)
    {
        _mockGuestSessionService
            .Setup(s => s.DeleteGuestSessionAsync(CallerId, GroupId, GuestSessionId))
            .ThrowsAsync(exception);

        return _handler.HandleRequest(CreateRequest(FullPath()), _context);
    }

    [Fact]
    public async Task HandleRequest_OwnerDeletesGuestSession_Returns200WithCamelCaseSnapshot()
    {
        _mockGuestSessionService
            .Setup(s => s.DeleteGuestSessionAsync(CallerId, GroupId, GuestSessionId))
            .ReturnsAsync(GroupSnapshotSample.Create());

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(FullPath()), _context);

        Assert.Equal(200, response.StatusCode);
        GroupSnapshotSample.AssertJson(ParseBody(response));
        _mockGuestSessionService.Verify(s => s.DeleteGuestSessionAsync(CallerId, GroupId, GuestSessionId), Times.Once);
    }

    [Theory]
    [InlineData(MissingSubjectCase.AuthorizerNull)]
    [InlineData(MissingSubjectCase.ClaimsNull)]
    [InlineData(MissingSubjectCase.SubClaimMissing)]
    [InlineData(MissingSubjectCase.SubEmpty)]
    [InlineData(MissingSubjectCase.SubWhitespace)]
    public async Task HandleRequest_NoAuthenticatedSubject_Returns401AndDoesNotCallService(MissingSubjectCase missingSubjectCase)
    {
        APIGatewayProxyRequest request = CreateRequest(FullPath());
        request.RequestContext = ContextWithout(missingSubjectCase);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 401, "Unauthorized.");
        VerifyServiceNotCalled();
    }

    [Theory]
    [InlineData("groupReservationId")]
    [InlineData("guestSessionId")]
    public async Task HandleRequest_MissingPathParameter_Returns400AndDoesNotCallService(string missingParameter)
    {
        Dictionary<string, string> path = FullPath();
        path.Remove(missingParameter);

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(path), _context);

        AssertErrorResponse(response, 400);
        VerifyServiceNotCalled();
    }

    [Fact]
    public async Task HandleRequest_GroupReservationNotFound_Returns404()
    {
        var exception = new GroupReservationNotFoundException();

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 404, exception.Message);
    }

    [Fact]
    public async Task HandleRequest_ReservationNotFound_Returns404()
    {
        var exception = new ReservationNotFoundException("Parent reservation not found.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 404, exception.Message);
    }

    [Fact]
    public async Task HandleRequest_GuestSessionNotFound_Returns404()
    {
        var exception = new GuestSessionNotFoundException("Guest session with id session-a not found.");

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
        var exception = new GroupReservationExpiredException();

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "GroupReservationExpired", exception.Message);
    }

    [Fact]
    public async Task HandleRequest_DomainValidationError_Returns400WithUnknownErrorCode()
    {
        var exception = new ValidationException("Guest session does not belong to this group reservation.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "Unknown", exception.Message);
    }
}
