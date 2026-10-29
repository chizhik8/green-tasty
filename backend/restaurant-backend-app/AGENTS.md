# Backend Guidelines — restaurant-backend-app

Instructions for every coding agent working under `backend/restaurant-backend-app`. `CLAUDE.md` in this folder only imports this file, so add or change backend rules here. Repository-wide rules (required reading, AWS region, deployment, what never to commit) are in the root [AGENTS.md](../../AGENTS.md) and apply here too.

This file is a first draft: the content moved here from the root `AGENTS.md` and will be extended when the backend instructions are set up.

**Paths.** Paths are relative to this folder (`backend/restaurant-backend-app/`). Paths that start with `../../` are relative to the repository root; Terraform files are in `../../infra/terraform/`. The `api-handler` code lives in `dnapp/lambdas/api-handler/`, shortened below to `api-handler/`.

## Build and Test

```bash
dotnet build restaurant-backend-app.sln
dotnet test Tests/APIHandler.Tests.csproj
dotnet test Tests/APIHandler.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests"            # one class
dotnet test Tests/APIHandler.Tests.csproj --filter "FullyQualifiedName~OrderServiceTests.<MethodName>" # one test
```

The repository configures no local backend host or emulator (no Docker or LocalStack setup). Unit tests run locally; AWS integration checks require a deployed backend, and deploying happens only when the user asks (root `AGENTS.md`, Security & Configuration). Deployment uses `syndicate build`, then `syndicate deploy --deploy_name dev` (full) or `syndicate update` (incremental), from a venv with `aws-syndicate` and `SDCT_CONF` pointing at `.syndicate-config-restaurant-backend-app` — see the root [README.md](../../README.md).

## Coding Style

Match existing C# four-space indentation, PascalCase types/methods, and `I`-prefixed interfaces. Keep files grouped by domain: request handling, business logic, and persistence go in `api-handler/Handlers/`, `api-handler/Services/`, and `api-handler/Repositories/`, respectively.

## Testing

`Tests/APIHandler.Tests.csproj` references only `api-handler`. Its existing tests (`Tests/Services/*Tests.cs`, xUnit, Moq, AutoFixture) cover `api-handler` service classes: they mock the repository interfaces and `ILambdaContext`/`ILambdaLogger`, then construct the service directly. `reports-handler` and `report-sender` have no tests.

Add tests at the layer relevant to the change. Cover success, failure, and boundary cases; add regression tests for fixes. No numeric coverage threshold is configured.

## Architecture

**Three Lambdas** in `dnapp/lambdas/`, all in `restaurant-backend-app.sln` (how they interact: [../../docs/architecture.md](../../docs/architecture.md), section 4):

- `api-handler` — one Lambda behind API Gateway that serves the entire REST API.
- `reports-handler` — SQS consumer of the reports queue (`../../infra/terraform/sqs.tf`, with DLQ). It aggregates messages into `DailyAggregate` items in the `Reports` table and returns partial batch failures; malformed or inconsistent messages are logged as `PERMANENT_ERROR` and not retried.
- `report-sender` — invoked weekly by EventBridge Scheduler (`../../infra/terraform/eventbridge.tf`, Fridays 08:00 UTC); builds report files and emails them to the Cognito `Admin` group via SES.

**Deployment definitions.** Each Lambda has its own `dnapp/lambdas/<lambda>/lambda_config.json` (env vars, IAM role, runtime, event sources) and `deployment_resources.json` (its IAM role). The top-level `deployment_resources.json` defines the API Gateway (resources, methods, CORS, `cognito_authorizer` vs `NONE` per method), all DynamoDB tables and GSIs, the shared `lambda-basic-execution` policy and the Swagger UI bucket. `${...}` placeholders resolve from `syndicate_aliases.yml`. The Cognito user pool is created by Terraform (`../../infra/terraform/cognito.tf`); Syndicate finds it by name and injects its id and client id into env vars.

**Request flow in `api-handler`.**

1. `api-handler/Function.cs`: `FunctionHandler` answers `OPTIONS` itself, then matches `HttpMethod` + path against the route table. Unmatched → 404. Only the call to the matched handler's `HandleRequest` is wrapped in `try/catch`: an exception thrown there goes to `api-handler/Services/ExceptionHandler.cs` → 500. Exceptions outside that call (building the route table in the `Function` constructor, route matching) are not caught by it.
2. `api-handler/Routing/RouteRegistry.cs` is the composition root **and** the route table. It builds a `ServiceCollection` (AWS clients → repositories → services, all singletons), then maps each `Route(method, "/path/{param}")` to a handler created with `ActivatorUtilities.CreateInstance`. `{param}` segments become `request.PathParameters`. Order matters within a method: more specific templates must come before ones that would also match.
3. Handlers (`api-handler/Handlers/<Domain>/*Handler.cs`) implement `IApiRequestHandler` and get dependencies through constructor injection — some through primary constructors, most through classic constructors; match the style of the file you edit. They read input via `api-handler/Helpers/APIGatewayProxyRequestExtensions` / `RequestBodyHelper`, call a service, catch domain exceptions from `api-handler/Exceptions/`, and build responses with `api-handler/Helpers/ResponseBuilder`.
4. Services (`api-handler/Services/<Domain>/`, interface + impl) hold business logic. Repositories (`api-handler/Repositories/<Domain>/`) wrap DynamoDB via `IDynamoDBContext`, except `api-handler/Repositories/Auth/UserRepository.cs`, which wraps the Cognito user pool (`AmazonCognitoIdentityProviderClient`). Entities in `api-handler/Entities/` are DynamoDB models; request/response DTOs live in `api-handler/Models/`; `api-handler/Mappers/` convert between them.

**Adding an endpoint** touches: handler class → registration in `api-handler/Routing/RouteRegistry.cs` (plus any new repo/service in the DI section) → method/resource in the top-level `deployment_resources.json` (with the right `authorization_type`) → `docs/ApiRoutes.md`. New tables also need an entry in the top-level `deployment_resources.json`, an env var in `api-handler/lambda_config.json`, an accessor in `api-handler/Constants/ResourceNames.cs`, and IAM permissions.

**Choosing authorization.** Check `authorization_type` in the top-level `deployment_resources.json` per method, not the path prefix:

- `cognito_authorizer` routes: API Gateway validates the token; the handler reads `sub` and `cognito:groups` from `request.RequestContext.Authorizer.Claims` via `UserContextHelper` / `RoleHelper`. Cognito only proves the caller is signed in; any ownership or role rule must be checked in code.
- Guest operations (`POST /guest/sessions`, `/guest/sessions/{guestSessionId}/...`, `GET /guest/invites/{token}`, `GET /guest/groups/{id}/status`) are `NONE`; handlers that act on a session authorize in code via `IGuestAuthorizationService` (guest token in `Authorization: Bearer <token>`).
- Owner operations are `cognito_authorizer`, but only one of them checks ownership today:
  - `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` passes the caller's `sub` to `GuestSessionService.DeleteGuestSessionAsync`, which calls `ValidateReservationOwnerAsync` (`ActionForbiddenException` → 403).
  - `GET /reservations/{reservationId}/invite-token` has **no ownership check**: `GetInviteTokenHandler` does not read the caller's id, and `GroupReservationService.GetGroupReservationInviteTokenAsync` takes only `reservationId`. The service still checks the reservation (two or more guests, status `Reserved`) and the group (not submitted or closed), so any signed-in user who knows another user's reservation id can get its invite token whenever those checks pass.

**Group pre-order model.** A `GroupReservation` belongs to an owner's reservation; each joined guest has a `GuestSession` with a cart that can be submitted. `GuestSession` also supports an owner session (`IsOwnerSession`, `OwnerUserId`); whether any code path creates one yet is tracked in `../../docs/architecture.md`, section 4.4.

## References

- State machines and the ER diagram: `docs/Diagrams.md`; editable draw.io sources are in `docs/assets/`.
- API contract: `docs/ApiRoutes.md`.
- Group pre-order homework: [../../homework-backend.md](../../homework-backend.md) (in Ukrainian). Implementation status of its endpoints is tracked in `../../docs/architecture.md` (section 4.4) — re-check it against `api-handler/Routing/RouteRegistry.cs` and update it when an endpoint changes.
- Swagger: follow the full procedure in [docs/GenerateSwagger.md](docs/GenerateSwagger.md) — it includes renaming the export to `specs.json`, the `syndicate generate swagger-ui` arguments and the `target_bucket` fix in the generated `swagger_ui_config.json` (today at `swagger_src/swagger-ui/swagger_ui_config.json`; `docs/GenerateSwagger.md` omits the `swagger-ui/` folder).
