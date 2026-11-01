using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Services.GuestSessions;
using Moq;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

public class GetGroupSessionsHandlerTests
{
    private const string GroupId = "group-1";

    private readonly Mock<IGuestSessionService> _mockGuestSessionService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly GetGroupSessionsHandler _handler;

    public GetGroupSessionsHandlerTests()
    {
        _handler = new GetGroupSessionsHandler(_mockGuestSessionService.Object);
    }

    private static Dictionary<string, string> PathWithGroup() => new() { ["groupReservationId"] = GroupId };

    private Task<APIGatewayProxyResponse> HandleWithServiceThrowing(Exception exception)
    {
        _mockGuestSessionService
            .Setup(s => s.GetGroupSessionsAsync(CallerId, GroupId))
            .ThrowsAsync(exception);

        return _handler.HandleRequest(CreateRequest(PathWithGroup()), _context);
    }

    [Fact]
    public async Task HandleRequest_OwnerRequest_Returns200WithCamelCaseSnapshot()
    {
        _mockGuestSessionService
            .Setup(s => s.GetGroupSessionsAsync(CallerId, GroupId))
            .ReturnsAsync(GroupSnapshotSample.Create());

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(PathWithGroup()), _context);

        Assert.Equal(200, response.StatusCode);
        GroupSnapshotSample.AssertJson(ParseBody(response));
        _mockGuestSessionService.Verify(s => s.GetGroupSessionsAsync(CallerId, GroupId), Times.Once);
    }

    [Theory]
    [InlineData(MissingSubjectCase.AuthorizerNull)]
    [InlineData(MissingSubjectCase.ClaimsNull)]
    [InlineData(MissingSubjectCase.SubClaimMissing)]
    [InlineData(MissingSubjectCase.SubEmpty)]
    [InlineData(MissingSubjectCase.SubWhitespace)]
    public async Task HandleRequest_NoAuthenticatedSubject_Returns401AndDoesNotCallService(MissingSubjectCase missingSubjectCase)
    {
        APIGatewayProxyRequest request = CreateRequest(PathWithGroup());
        request.RequestContext = ContextWithout(missingSubjectCase);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 401, "Unauthorized.");
        _mockGuestSessionService.Verify(s => s.GetGroupSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleRequest_MissingGroupReservationId_Returns400AndDoesNotCallService(bool pathParametersNull)
    {
        Dictionary<string, string>? path = pathParametersNull ? null : new() { ["groupReservationId"] = string.Empty };

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(path), _context);

        AssertErrorResponse(response, 400);
        _mockGuestSessionService.Verify(s => s.GetGroupSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
    public async Task HandleRequest_CallerIsNotOwner_Returns403()
    {
        var exception = new ActionForbiddenException("Only the reservation owner can perform this action.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorResponse(response, 403, exception.Message);
    }

    [Fact]
    public async Task HandleRequest_DomainValidationError_Returns400WithUnknownErrorCode()
    {
        var exception = new ValidationException("Something is wrong with the group.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "Unknown", exception.Message);
    }
}
