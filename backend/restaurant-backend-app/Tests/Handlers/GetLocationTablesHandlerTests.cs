using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Handlers.Tables.Management;
using APIHandler.Services.Tables;
using Moq;
using System.Text.Json;
using static APIHandler.Tests.Handlers.HandlerTestHelpers;

namespace APIHandler.Tests.Handlers;

public class GetLocationTablesHandlerTests
{
    private const string LocationId = "location-1";

    private static readonly string[] TableFields = ["capacity", "id", "locationId", "tableNumber"];

    private readonly Mock<ITableService> _mockTableService = new();
    private readonly ILambdaContext _context = CreateLambdaContext();
    private readonly GetLocationTablesHandler _handler;

    public GetLocationTablesHandlerTests()
    {
        _handler = new GetLocationTablesHandler(_mockTableService.Object);
    }

    private static Dictionary<string, string> PathWithLocation() => new() { ["id"] = LocationId };

    private void VerifyServiceNotCalled()
    {
        _mockTableService.Verify(s => s.GetAllTablesForLocationAsync(It.IsAny<string>()), Times.Never);
    }

    // S8 / AC3
    [Fact]
    public async Task HandleRequest_WaiterWithIdPathParameter_PassesIdToServiceAndReturns200WithTables()
    {
        _mockTableService
            .Setup(s => s.GetAllTablesForLocationAsync(LocationId))
            .ReturnsAsync(new List<Table>
            {
                new() { Id = "table-1", LocationId = LocationId, Capacity = 4, TableNumber = 10 },
                new() { Id = "table-2", LocationId = LocationId, Capacity = 2, TableNumber = 11 },
            });

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement[] tables = ParseBody(response).EnumerateArray().ToArray();
        Assert.Equal(2, tables.Length);

        Assert.Equal(TableFields, PropertyNames(tables[0]));
        Assert.Equal("table-1", tables[0].GetProperty("id").GetString());
        Assert.Equal(LocationId, tables[0].GetProperty("locationId").GetString());
        Assert.Equal(4, tables[0].GetProperty("capacity").GetInt32());
        Assert.Equal(10, tables[0].GetProperty("tableNumber").GetInt32());

        Assert.Equal(TableFields, PropertyNames(tables[1]));
        Assert.Equal("table-2", tables[1].GetProperty("id").GetString());
        Assert.Equal(LocationId, tables[1].GetProperty("locationId").GetString());
        Assert.Equal(2, tables[1].GetProperty("capacity").GetInt32());
        Assert.Equal(11, tables[1].GetProperty("tableNumber").GetInt32());

        _mockTableService.Verify(s => s.GetAllTablesForLocationAsync(LocationId), Times.Once);
        _mockTableService.VerifyNoOtherCalls();
    }

    // AC3: "200 OK with [] when the location has no tables or does not exist".
    [Fact]
    public async Task HandleRequest_LocationHasNoTables_Returns200WithEmptyArray()
    {
        _mockTableService
            .Setup(s => s.GetAllTablesForLocationAsync(LocationId))
            .ReturnsAsync(new List<Table>());

        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context);

        Assert.Equal(200, response.StatusCode);
        JsonElement body = ParseBody(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    public static TheoryData<Dictionary<string, string>?> MissingIdPaths => new()
    {
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["locationId"] = LocationId },
    };

    // S9 / AC3: the message text is not fixed by the spec, so only the status code and error shape are asserted.
    [Theory]
    [MemberData(nameof(MissingIdPaths))]
    public async Task HandleRequest_IdPathParameterMissing_Returns400AndDoesNotCallService(Dictionary<string, string>? pathParameters)
    {
        APIGatewayProxyResponse response = await _handler.HandleRequest(CreateWaiterRequest(pathParameters), _context);

        AssertErrorResponse(response, 400);
        VerifyServiceNotCalled();
    }

    // S10 / AC3
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

        AssertErrorResponse(response, 403, "Forbidden");
        VerifyServiceNotCalled();
    }

    // Contract: other errors propagate to the global handler.
    [Fact]
    public async Task HandleRequest_ServiceThrows_PropagatesException()
    {
        var exception = new InvalidOperationException("DynamoDB is unavailable.");
        _mockTableService
            .Setup(s => s.GetAllTablesForLocationAsync(LocationId))
            .ThrowsAsync(exception);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleRequest(CreateWaiterRequest(PathWithLocation()), _context));

        Assert.Same(exception, thrown);
    }
}
