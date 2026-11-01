using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.Locations;
using APIHandler.Services.Locations;
using Moq;
using System.Text.Json;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

public class RemoveDishFromLocationHandlerTests
{
    private const string LocationId = "location-1";
    private const string DishId = "dish-1";

    private readonly Mock<ILocationService> _mockLocationService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly RemoveDishFromLocationHandler _handler;

    public RemoveDishFromLocationHandlerTests()
    {
        _handler = new RemoveDishFromLocationHandler(_mockLocationService.Object);
    }

    private static Dictionary<string, string> PathWithLocationAndDish() => new()
    {
        ["id"] = LocationId,
        ["dishId"] = DishId,
    };

    private void VerifyServiceNotCalled()
    {
        _mockLocationService.Verify(s => s.RemoveDishFromLocationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // S11 / AC2
    [Fact]
    public async Task HandleRequest_WaiterWithIdAndDishId_RemovesDishAndReturns200WithMessage()
    {
        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocationAndDish()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(new[] { "message" }, PropertyNames(body));
        Assert.Equal(
            "Dish 'dish-1' successfully removed from location 'location-1'.",
            body.GetProperty("message").GetString());

        _mockLocationService.Verify(s => s.RemoveDishFromLocationAsync(LocationId, DishId), Times.Once);
        _mockLocationService.VerifyNoOtherCalls();
    }

    public static TheoryData<Dictionary<string, string>?> MissingParameterPaths => new()
    {
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["dishId"] = DishId },
        new Dictionary<string, string> { ["id"] = LocationId },
        new Dictionary<string, string> { ["locationId"] = LocationId, ["dishId"] = DishId },
    };

    // S12 / AC2: the message text is not fixed by the spec, so only the status code and error shape are asserted.
    [Theory]
    [MemberData(nameof(MissingParameterPaths))]
    public async Task HandleRequest_IdOrDishIdMissing_Returns400AndDoesNotCallService(Dictionary<string, string>? pathParameters)
    {
        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(pathParameters), _context);

        AssertErrorResponse(response, 400);
        VerifyServiceNotCalled();
    }

    // S13 / AC2
    [Theory]
    [InlineData(NonWaiterCase.AuthorizerNull)]
    [InlineData(NonWaiterCase.GroupsClaimMissing)]
    [InlineData(NonWaiterCase.CustomerOnly)]
    [InlineData(NonWaiterCase.CustomerOnlyJsonArray)]
    public async Task HandleRequest_CallerNotWaiter_Returns403AndDoesNotCallService(NonWaiterCase nonWaiterCase)
    {
        APIGatewayProxyRequest request = CreateWaiterRequest(PathWithLocationAndDish());
        request.RequestContext = NonWaiterContext(nonWaiterCase);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 403, "Forbidden: Only waiters can perform this action.");
        VerifyServiceNotCalled();
    }

    // S14 / AC2: location not found, or dish not on the location's specialty dishes.
    [Theory]
    [InlineData("Location 'location-1' not found.")]
    [InlineData("Dish 'dish-1' is not currently on the menu for this location.")]
    public async Task HandleRequest_ServiceThrowsValidationException_Returns400WithExceptionMessage(string message)
    {
        _mockLocationService
            .Setup(s => s.RemoveDishFromLocationAsync(LocationId, DishId))
            .ThrowsAsync(new ValidationException(message));

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocationAndDish()), _context);

        AssertErrorResponse(response, 400, message);
    }

    // Contract: other errors propagate to the global handler.
    [Fact]
    public async Task HandleRequest_ServiceThrowsOtherException_PropagatesException()
    {
        var exception = new InvalidOperationException("DynamoDB is unavailable.");
        _mockLocationService
            .Setup(s => s.RemoveDishFromLocationAsync(LocationId, DishId))
            .ThrowsAsync(exception);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleRequest(CreateWaiterRequest(PathWithLocationAndDish()), _context));

        Assert.Same(exception, thrown);
    }
}
