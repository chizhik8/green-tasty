using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Handlers.Locations;
using APIHandler.Models.WaiterList;
using APIHandler.Services.Waiters;
using Moq;
using System.Text.Json;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

public class GetLocationWaitersHandlerTests
{
    private const string LocationId = "location-1";
    private const string ForbiddenMessage = "Forbidden";
    private const string LocationIdRequiredMessage = "Location id is required.";

    private static readonly string[] WaiterFields = ["createdAt", "locationId", "reservations", "userId"];

    private readonly Mock<IWaiterService> _mockWaiterService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly GetLocationWaitersHandler _handler;

    public GetLocationWaitersHandlerTests()
    {
        _handler = new GetLocationWaitersHandler(_mockWaiterService.Object);
    }

    private static Dictionary<string, string> PathWithLocation(string id = LocationId) => new() { ["id"] = id };

    private void SetupCallerLocation(string? callerLocationId)
    {
        _mockWaiterService
            .Setup(s => s.GetWaiterLocationIdAsync(CallerId))
            .ReturnsAsync(callerLocationId);
    }

    private void SetupWaiters(List<WaiterListItem> waiters)
    {
        _mockWaiterService
            .Setup(s => s.GetWaitersByLocationIdAsync(LocationId))
            .ReturnsAsync(waiters);
    }

    private void VerifyWaiterListNotRead()
    {
        _mockWaiterService.Verify(s => s.GetWaitersByLocationIdAsync(It.IsAny<string>()), Times.Never);
    }

    // S1, S4, S6d / AC9, AC8a
    [Fact]
    public async Task HandleRequest_CallerAssignedToLocation_Returns200WithWaitersInContractShape()
    {
        SetupCallerLocation(LocationId);
        SetupWaiters(
        [
            new WaiterListItem
            {
                UserId = "waiter-a",
                LocationId = LocationId,
                CreatedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc),
                Reservations = ["reservation-1", "reservation-2"],
            },
            new WaiterListItem
            {
                UserId = "waiter-b",
                LocationId = LocationId,
                CreatedAt = new DateTime(2026, 10, 28, 8, 30, 15, DateTimeKind.Utc),
                Reservations = [],
            },
        ]);

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());

        // The order of the array is not defined by the contract, so elements are looked up by userId.
        Dictionary<string, JsonElement> byUserId = body.EnumerateArray()
            .ToDictionary(e => e.GetProperty("userId").GetString()!, StringComparer.Ordinal);

        JsonElement waiterA = byUserId["waiter-a"];
        Assert.Equal(WaiterFields, PropertyNames(waiterA));
        Assert.Equal(LocationId, waiterA.GetProperty("locationId").GetString());
        Assert.Equal("2026-10-27T12:00:00Z", waiterA.GetProperty("createdAt").GetString());
        Assert.Equal(JsonValueKind.Array, waiterA.GetProperty("reservations").ValueKind);
        Assert.Equal(
            new[] { "reservation-1", "reservation-2" },
            waiterA.GetProperty("reservations").EnumerateArray().Select(r => r.GetString()).ToArray());

        JsonElement waiterB = byUserId["waiter-b"];
        Assert.Equal(WaiterFields, PropertyNames(waiterB));
        Assert.Equal(LocationId, waiterB.GetProperty("locationId").GetString());
        Assert.Equal("2026-10-28T08:30:15Z", waiterB.GetProperty("createdAt").GetString());
        Assert.Equal(JsonValueKind.Array, waiterB.GetProperty("reservations").ValueKind);
        Assert.Equal(0, waiterB.GetProperty("reservations").GetArrayLength());

        _mockWaiterService.Verify(s => s.GetWaiterLocationIdAsync(CallerId), Times.Once);
        _mockWaiterService.Verify(s => s.GetWaitersByLocationIdAsync(LocationId), Times.Once);
        _mockWaiterService.VerifyNoOtherCalls();
    }

    // S3 / AC10
    [Fact]
    public async Task HandleRequest_ServiceReturnsNoWaiters_Returns200WithEmptyArray()
    {
        SetupCallerLocation(LocationId);
        SetupWaiters([]);

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
        _mockWaiterService.Verify(s => s.GetWaitersByLocationIdAsync(LocationId), Times.Once);
    }

    // S5 / AC7: RoleHelper accepts the groups claim both as a JSON array and as a comma-separated string.
    [Theory]
    [InlineData("Waiter")]
    [InlineData("[\"Waiter\"]")]
    [InlineData("Customer,Waiter")]
    [InlineData("Customer, Waiter")]
    [InlineData("[\"Customer\",\"Waiter\"]")]
    public async Task HandleRequest_GroupsClaimContainsWaiter_PassesRoleCheck(string groups)
    {
        SetupCallerLocation(LocationId);
        SetupWaiters([]);

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateRequestWithGroups(PathWithLocation(), groups), _context);

        Assert.Equal(200, response.StatusCode);
        _mockWaiterService.Verify(s => s.GetWaitersByLocationIdAsync(LocationId), Times.Once);
    }

    // S5 / AC7
    [Theory]
    [InlineData(NonWaiterCase.AuthorizerNull)]
    [InlineData(NonWaiterCase.GroupsClaimMissing)]
    [InlineData(NonWaiterCase.CustomerOnly)]
    [InlineData(NonWaiterCase.CustomerOnlyJsonArray)]
    public async Task HandleRequest_CallerNotWaiter_Returns403AndDoesNotCallService(NonWaiterCase nonWaiterCase)
    {
        APIGatewayProxyRequest request = CreateWaiterRequest(PathWithLocation());
        request.RequestContext = NonWaiterContext(nonWaiterCase);

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 403, ForbiddenMessage);
        _mockWaiterService.VerifyNoOtherCalls();
    }

    // Order of checks: role (403) comes before the path parameter check (400).
    [Fact]
    public async Task HandleRequest_CallerNotWaiterAndIdMissing_Returns403()
    {
        APIGatewayProxyRequest request = CreateRequestWithGroups(pathParameters: null, groups: "Customer");

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 403, ForbiddenMessage);
        _mockWaiterService.VerifyNoOtherCalls();
    }

    public static TheoryData<Dictionary<string, string>?> MissingIdPaths => new()
    {
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["locationId"] = LocationId },
        new Dictionary<string, string> { ["id"] = string.Empty },
    };

    // S6 / AC8
    [Theory]
    [MemberData(nameof(MissingIdPaths))]
    public async Task HandleRequest_IdMissingOrEmpty_Returns400AndDoesNotCallService(Dictionary<string, string>? pathParameters)
    {
        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(pathParameters), _context);

        AssertErrorResponse(response, 400, LocationIdRequiredMessage);
        _mockWaiterService.VerifyNoOtherCalls();
    }

    // S6a / AC8a
    [Fact]
    public async Task HandleRequest_CallerAssignedToAnotherLocation_Returns403AndDoesNotReadWaiterList()
    {
        SetupCallerLocation("location-2");

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context);

        AssertErrorResponse(response, 403, ForbiddenMessage);
        _mockWaiterService.Verify(s => s.GetWaiterLocationIdAsync(CallerId), Times.Once);
        VerifyWaiterListNotRead();
    }

    // S6a / AC8a: the own-location comparison is ordinal (case-sensitive).
    [Fact]
    public async Task HandleRequest_CallerLocationDiffersOnlyByCase_Returns403AndDoesNotReadWaiterList()
    {
        _mockWaiterService
            .Setup(s => s.GetWaiterLocationIdAsync(CallerId))
            .ReturnsAsync("LOC-1");

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation("loc-1")), _context);

        AssertErrorResponse(response, 403, ForbiddenMessage);
        VerifyWaiterListNotRead();
    }

    // S6b / AC8a
    [Fact]
    public async Task HandleRequest_CallerHasNoWaiterItem_Returns403AndDoesNotReadWaiterList()
    {
        SetupCallerLocation(null);

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context);

        AssertErrorResponse(response, 403, ForbiddenMessage);
        _mockWaiterService.Verify(s => s.GetWaiterLocationIdAsync(CallerId), Times.Once);
        VerifyWaiterListNotRead();
    }

    // S6c / AC8a
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleRequest_WaiterWithoutSub_Returns403AndDoesNotCallService(string? sub)
    {
        var claims = new Dictionary<string, string> { ["cognito:groups"] = "Waiter" };
        if (sub is not null)
            claims["sub"] = sub;

        APIGatewayProxyRequest request = CreateWaiterRequest(PathWithLocation());
        request.RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims },
        };

        APIGatewayProxyResponse response = await _handler.HandleRequest(request, _context);

        AssertErrorResponse(response, 403, ForbiddenMessage);
        _mockWaiterService.VerifyNoOtherCalls();
    }

    // S7: errors from the service propagate to the global handler.
    [Fact]
    public async Task HandleRequest_GetWaiterLocationIdThrows_PropagatesException()
    {
        var exception = new InvalidOperationException("DynamoDB is unavailable.");
        _mockWaiterService
            .Setup(s => s.GetWaiterLocationIdAsync(CallerId))
            .ThrowsAsync(exception);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context));

        Assert.Same(exception, thrown);
        VerifyWaiterListNotRead();
    }

    // S7: errors from the service propagate to the global handler.
    [Fact]
    public async Task HandleRequest_GetWaitersByLocationIdThrows_PropagatesException()
    {
        var exception = new InvalidOperationException("DynamoDB is unavailable.");
        SetupCallerLocation(LocationId);
        _mockWaiterService
            .Setup(s => s.GetWaitersByLocationIdAsync(LocationId))
            .ThrowsAsync(exception);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context));

        Assert.Same(exception, thrown);
    }
}
