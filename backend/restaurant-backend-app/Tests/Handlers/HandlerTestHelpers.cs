using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Models.Dishes;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers;

/// <summary>
/// Ways in which a request can arrive without a usable Cognito <c>sub</c> claim.
/// </summary>
public enum MissingSubjectCase
{
    AuthorizerNull,
    ClaimsNull,
    SubClaimMissing,
    SubEmpty,
    SubWhitespace,
}

/// <summary>
/// Builds API Gateway requests and checks responses for the group pre-order owner handler tests.
/// </summary>
internal static class HandlerTestHelpers
{
    public const string CallerId = "owner-user-1";

    public static APIGatewayProxyRequest CreateRequest(Dictionary<string, string>? pathParameters, string? body = null) => new()
    {
        PathParameters = pathParameters,
        Body = body,
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext
            {
                Claims = new Dictionary<string, string>
                {
                    ["sub"] = CallerId,
                    ["cognito:groups"] = "Customer",
                },
            },
        },
    };

    // UserContextHelper dereferences RequestContext, so every case keeps a non-null RequestContext.
    public static APIGatewayProxyRequest.ProxyRequestContext ContextWithout(MissingSubjectCase missingSubjectCase) => missingSubjectCase switch
    {
        MissingSubjectCase.AuthorizerNull => new APIGatewayProxyRequest.ProxyRequestContext { Authorizer = null },
        MissingSubjectCase.ClaimsNull => new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext { Claims = null },
        },
        MissingSubjectCase.SubClaimMissing => WithClaims(new Dictionary<string, string> { ["email"] = "owner@example.com" }),
        MissingSubjectCase.SubEmpty => WithClaims(new Dictionary<string, string> { ["sub"] = string.Empty }),
        MissingSubjectCase.SubWhitespace => WithClaims(new Dictionary<string, string> { ["sub"] = "   " }),
        _ => throw new ArgumentOutOfRangeException(nameof(missingSubjectCase)),
    };

    private static APIGatewayProxyRequest.ProxyRequestContext WithClaims(Dictionary<string, string> claims) => new()
    {
        Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims },
    };

    public static ILambdaContext CreateLambdaContext()
    {
        var logger = new Mock<ILambdaLogger>();
        var context = new Mock<ILambdaContext>();
        context.Setup(c => c.Logger).Returns(logger.Object);
        return context.Object;
    }

    public static JsonElement ParseBody(APIGatewayProxyResponse response)
    {
        using JsonDocument document = JsonDocument.Parse(response.Body);
        return document.RootElement.Clone();
    }

    public static string[] PropertyNames(JsonElement element) => element
        .EnumerateObject()
        .Select(p => p.Name)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Asserts the <c>{ "error": "..." }</c> shape built by <c>ResponseBuilder.CreateErrorResponse</c>.</summary>
    public static void AssertErrorResponse(APIGatewayProxyResponse response, int expectedStatusCode, string? expectedMessage = null)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);

        JsonElement body = ParseBody(response);
        Assert.Equal(new[] { "error" }, PropertyNames(body));

        string? error = body.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));
        if (expectedMessage is not null)
            Assert.Equal(expectedMessage, error);
    }

    /// <summary>Asserts the 400 <c>{ "message": "...", "errorCode": "..." }</c> shape.</summary>
    public static void AssertErrorCodeResponse(APIGatewayProxyResponse response, string expectedErrorCode, string expectedMessage)
    {
        Assert.Equal(400, response.StatusCode);

        JsonElement body = ParseBody(response);
        Assert.Equal(new[] { "errorCode", "message" }, PropertyNames(body));
        Assert.Equal(expectedMessage, body.GetProperty("message").GetString());
        Assert.Equal(expectedErrorCode, body.GetProperty("errorCode").GetString());
    }
}

/// <summary>
/// A group snapshot returned by the mocked service and the checks of its JSON contract (spec "Group snapshot").
/// </summary>
internal static class GroupSnapshotSample
{
    private static readonly string[] DishFields =
    [
        "calories", "carbohydrates", "description", "dishType", "fats", "id", "imageUrl",
        "name", "price", "proteins", "state", "vitamins", "weight",
    ];

    public static GetAllSessionsResponse Create()
    {
        var dish = new DishResponse
        {
            Id = "dish1",
            Name = "Caesar Salad",
            Description = "Fresh salad",
            DishType = "Main Course",
            Price = 180m,
            Weight = "300 g",
            Calories = "350 kcal",
            Proteins = "10 g",
            Fats = "12 g",
            Carbohydrates = "20 g",
            Vitamins = "A, C",
            ImageUrl = null,
            State = "Available",
        };

        return new GetAllSessionsResponse(
            [
                new Participant("owner-session", ParticipantType.Owner, "Maria", [], 0m, false),
                new Participant("session1", ParticipantType.Guest, "Andrii", [new OrderItemResponse { Dish = dish, Quantity = 2 }], 360m, true),
            ],
            360m);
    }

    public static void AssertJson(JsonElement body)
    {
        Assert.Equal(new[] { "participants", "total" }, HandlerTestHelpers.PropertyNames(body));
        Assert.Equal(360m, body.GetProperty("total").GetDecimal());

        JsonElement[] participants = body.GetProperty("participants").EnumerateArray().ToArray();
        Assert.Equal(2, participants.Length);

        string[] participantFields = ["id", "isSubmitted", "items", "name", "total", "type"];

        JsonElement owner = participants[0];
        Assert.Equal(participantFields, HandlerTestHelpers.PropertyNames(owner));
        Assert.Equal("owner-session", owner.GetProperty("id").GetString());
        Assert.Equal("Owner", owner.GetProperty("type").GetString());
        Assert.Equal("Maria", owner.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, owner.GetProperty("items").ValueKind);
        Assert.Equal(0, owner.GetProperty("items").GetArrayLength());
        Assert.Equal(0m, owner.GetProperty("total").GetDecimal());
        Assert.False(owner.GetProperty("isSubmitted").GetBoolean());

        JsonElement guest = participants[1];
        Assert.Equal(participantFields, HandlerTestHelpers.PropertyNames(guest));
        Assert.Equal("session1", guest.GetProperty("id").GetString());
        Assert.Equal("Guest", guest.GetProperty("type").GetString());
        Assert.Equal("Andrii", guest.GetProperty("name").GetString());
        Assert.Equal(360m, guest.GetProperty("total").GetDecimal());
        Assert.True(guest.GetProperty("isSubmitted").GetBoolean());

        JsonElement item = Assert.Single(guest.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "dish", "quantity" }, HandlerTestHelpers.PropertyNames(item));
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());

        JsonElement dish = item.GetProperty("dish");
        Assert.Equal(DishFields, HandlerTestHelpers.PropertyNames(dish));
        Assert.Equal("dish1", dish.GetProperty("id").GetString());
        Assert.Equal("Caesar Salad", dish.GetProperty("name").GetString());
        Assert.Equal(180m, dish.GetProperty("price").GetDecimal());
        Assert.Equal("Available", dish.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, dish.GetProperty("imageUrl").ValueKind);
    }
}
