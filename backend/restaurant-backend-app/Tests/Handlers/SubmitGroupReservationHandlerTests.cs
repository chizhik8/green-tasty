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

public class SubmitGroupReservationHandlerTests
{
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";
    private const string ValidBody = "{\"reservationId\":\"reservation-1\"}";

    private readonly Mock<IGroupReservationService> _mockGroupReservationService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly SubmitGroupReservationHandler _handler;

    public SubmitGroupReservationHandlerTests()
    {
        _handler = new SubmitGroupReservationHandler(_mockGroupReservationService.Object);
    }

    private static Dictionary<string, string> PathWithGroup() => new() { ["groupReservationId"] = GroupId };

    private void VerifyServiceNotCalled() => _mockGroupReservationService.Verify(
        s => s.SubmitGroupReservationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
        Times.Never);

    private Task<APIGatewayProxyResponse> HandleWithServiceThrowing(Exception exception)
    {
        _mockGroupReservationService
            .Setup(s => s.SubmitGroupReservationAsync(CallerId, GroupId, ReservationId))
            .ThrowsAsync(exception);

        return _handler.HandleRequest(CreateRequest(PathWithGroup(), ValidBody), _context);
    }

    [Fact]
    public async Task HandleRequest_OwnerSubmits_Returns200WithCamelCaseConsolidatedDishes()
    {
        _mockGroupReservationService
            .Setup(s => s.SubmitGroupReservationAsync(CallerId, GroupId, ReservationId))
            .ReturnsAsync([new SubmittedGroupDishResponse("dish1", 3), new SubmittedGroupDishResponse("dish2", 2)]);

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(PathWithGroup(), ValidBody), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        JsonElement[] items = body.EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal(new[] { "dishId", "quantity" }, PropertyNames(item)));
        Assert.Equal(
            new[] { ("dish1", 3), ("dish2", 2) },
            items
                .Select(i => (i.GetProperty("dishId").GetString()!, i.GetProperty("quantity").GetInt32()))
                .OrderBy(i => i.Item1, StringComparer.Ordinal)
                .ToArray());

        _mockGroupReservationService.Verify(s => s.SubmitGroupReservationAsync(CallerId, GroupId, ReservationId), Times.Once);
    }

    [Theory]
    [InlineData(MissingSubjectCase.AuthorizerNull)]
    [InlineData(MissingSubjectCase.ClaimsNull)]
    [InlineData(MissingSubjectCase.SubClaimMissing)]
    [InlineData(MissingSubjectCase.SubEmpty)]
    [InlineData(MissingSubjectCase.SubWhitespace)]
    public async Task HandleRequest_NoAuthenticatedSubject_Returns401AndDoesNotCallService(MissingSubjectCase missingSubjectCase)
    {
        APIGatewayProxyRequest request = CreateRequest(PathWithGroup(), ValidBody);
        request.RequestContext = ContextWithout(missingSubjectCase);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 401, "Unauthorized.");
        VerifyServiceNotCalled();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task HandleRequest_NoAuthenticatedSubjectAndInvalidBody_Returns401BeforeBodyValidation(string? body)
    {
        APIGatewayProxyRequest request = CreateRequest(PathWithGroup(), body);
        request.RequestContext = ContextWithout(MissingSubjectCase.SubClaimMissing);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 401, "Unauthorized.");
        VerifyServiceNotCalled();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"reservationId\":123}")]
    [InlineData("{}")]
    [InlineData("{\"reservationId\":\"\"}")]
    [InlineData("{\"reservationId\":\"   \"}")]
    public async Task HandleRequest_MissingOrInvalidBodyOrEmptyReservationId_Returns400AndDoesNotCallService(string? body)
    {
        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(PathWithGroup(), body), _context);

        AssertErrorResponse(response, 400);
        VerifyServiceNotCalled();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleRequest_MissingGroupReservationId_Returns400AndDoesNotCallService(bool pathParametersNull)
    {
        Dictionary<string, string>? path = pathParametersNull ? null : new() { ["groupReservationId"] = string.Empty };

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequest(path, ValidBody), _context);

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
        var exception = new ReservationNotFoundException("Reservation reservation-1 was not found.");

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
    public async Task HandleRequest_GroupOrReservationExpired_Returns400WithGroupReservationExpiredCode()
    {
        var exception = new GroupReservationExpiredException("The reservation is no longer active.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "GroupReservationExpired", exception.Message);
    }

    [Fact]
    public async Task HandleRequest_DomainValidationError_Returns400WithUnknownErrorCode()
    {
        var exception = new ValidationException("No participant has submitted a cart yet.");

        APIGatewayProxyResponse response = await HandleWithServiceThrowing(exception);

        AssertErrorCodeResponse(response, "Unknown", exception.Message);
    }
}
