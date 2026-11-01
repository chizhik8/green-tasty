# Location routes: path parameter fix, missing CORS settings and `GET /locations/{id}/waiters`

**Status:** Approved (2026-11-01)

## Goal

1. Make the two location sub-routes that use `{locationId}` deployable. API Gateway allows only one variable path part per level, and `/locations/{id}` already exists, so `DELETE /locations/{locationId}/dishes/{dishId}` and `GET /locations/{locationId}/tables/all` are never created by Syndicate. The problem exists since the first commit `1adf4c8` (project archive); it is not caused by later changes. Every Syndicate run (deploy 2026-10-27 13:22, update 2026-10-27 13:29, update 2026-11-01 13:14) logs `BadRequestException ... CreateResource: A sibling ({id}) of this resource already has a variable path part -- only one is allowed` for these two resources, then `Resource ... was configured` and finally `AWS resources were deployed successfully`. The deployed API (`yfdxslplfd`) has no such resources, so API Gateway answers both with `403` `{"message":"Missing Authentication Token"}` (checked on 2026-11-01 without a token; the same answer as for any path the API does not define).
2. Give every gateway resource touched by this task an `enable_cors` block. This is required, not cosmetic: in the first deploy (2026-10-27 13:26:27) Syndicate failed with `AttributeError: 'NoneType' object has no attribute 'get'` at `api_gateway_resource.py` line 1050 (`if enable_cors.get('state')`) while creating a resource without `enable_cors`. The only such resource created in that run was `/tables/{id}`: its `PUT` and `DELETE` were created (log lines 724–730), then the CORS step crashed. Later `update` runs found the resource existing and did not repeat the error. A new resource without `enable_cors` would hit the same crash. `/tables/{id}` itself stays without `enable_cors` (user's decision D1, see Risks).
3. Add `GET /locations/{id}/waiters`, which returns the waiters assigned to a location. The route is taken from the instructor's version of the API (user's decision; the instructor's resource tree shows `/locations/{id}/dishes/{dishId}` `DELETE`, `/locations/{id}/tables/all` `GET` and `/locations/{id}/waiters` `GET`); the response contract is a minimal one built from the existing `Waiters` table data, because the instructor's contract is not available.

## Scope

**In scope**

- Rename the path parameter `{locationId}` to `{id}` in `DELETE /locations/{id}/dishes/{dishId}` and `GET /locations/{id}/tables/all`: route registry, top-level `deployment_resources.json`, handlers that read the parameter, `docs/ApiRoutes.md`. The URL a client calls does not change.
- Add `"enable_cors": { "state": true }` to the two renamed resources and to the new `/locations/{id}/waiters` resource (see Goal 2).
- New endpoint `GET /locations/{id}/waiters` (handler, route, gateway resource and method, response DTO if needed, `docs/ApiRoutes.md`).
- Update `docs/architecture.md` line 242 (CORS bullet) and line 320 (open question 9), which name the `{locationId}` paths and the resources without CORS. The user explicitly allowed `backend-developer` to change these two places outside section 4.4.
- New unit tests for the two renamed handlers and the new handler (none of these handlers has tests today; `Tests/` has only `LocationServiceTests` and `WaiterServiceTests` for these areas).

**Out of scope**

- `/tables/{id}`: no change, including its missing `enable_cors` (decision D1; see Risks).
- Empty-string path parameters in `RemoveDishFromLocationHandler` and `GetLocationTablesHandler`: today they check only that the parameter exists, not that it is non-empty. Left as is (see Risks).
- Waiter names, emails or any data not stored in the `Waiters` table; joins with Cognito or other tables.
- Changes to `IWaiterService`/`IWaiterRepository` behaviour; the existing `GetWaiterLocationIdAsync` and `GetWaitersByLocationIdAsync` are reused.
- Frontend changes (the frontend does not call any of these routes today).
- The Swagger UI error in every Syndicate run (see Open questions) and Swagger UI regeneration.
- Compiler warnings in the build log (about 35 per build: `CS8618`, `CS0618`, `CS8604`, `CS8602`, `CS9113` and others), inherited from the archive.
- Deployment (only on a separate explicit request from the user).

## Affected Lambdas and files

`api-handler` only. Paths relative to `backend/restaurant-backend-app/` unless stated otherwise.

- `dnapp/lambdas/api-handler/Routing/RouteRegistry.cs`: lines 140 and 146 (renamed templates), new route for `GET /locations/{id}/waiters`.
- `dnapp/lambdas/api-handler/Handlers/Locations/RemoveDishFromLocationHandler.cs`: reads `id` instead of `locationId`.
- `dnapp/lambdas/api-handler/Handlers/Tables/Management/GetLocationTablesHandler.cs`: reads `id` instead of `locationId`.
- New handler for `GET /locations/{id}/waiters` (in `Handlers/Waiters/` or `Handlers/Locations/`, developer's choice), and a response DTO in `Models/` if needed to produce the field names below.
- `deployment_resources.json` (top level): resources at lines 250 and 259 renamed and given `enable_cors`; new resource `/locations/{id}/waiters` with `GET` and `enable_cors`. `/tables/{id}` (line 268) is not touched.
- `docs/ApiRoutes.md`: line 181 and line 714 headings; new `GET /locations/{id}/waiters` section (in the Locations or Waiters section).
- `../../docs/architecture.md`: line 242 and line 320 (see Scope).
- `Tests/Handlers/`: new handler test classes.

No change to `lambda_config.json`, env variables, IAM, tables or GSIs: the `Waiters` table, its `locationId-index` GSI, the `waitersTable` env var and the DynamoDB permissions already exist and are used by `WaiterRepository`.

## API contract changes

The two existing endpoints keep their behaviour; only the path parameter name changes from `locationId` to `id`. Their full contract (current code, `RemoveDishFromLocationHandler`, `GetLocationTablesHandler`, `LocationService.RemoveDishFromLocationAsync`, `TableService.GetAllTablesForLocationAsync`) is recorded here so that the tests and `docs/ApiRoutes.md` check against it.

### `GET /locations/{id}/tables/all` (renamed parameter)

Returns all tables of a location.

- Auth: `cognito_authorizer`; the caller must be in the Cognito group `Waiter` (`RoleHelper.IsWaiter`), otherwise `403` `{ "error": "Forbidden" }`. No own-location check (see Risks).
- Request: path parameter `id` (location id); no query parameters, no body.
- `200 OK`: JSON array of the `Table` items whose `locationId` equals `id`, read with `ITableService.GetAllTablesForLocationAsync(id)` → `ITableRepository.GetTablesByLocationIdAsync` (Tables GSI `ResourceNames.TablesLocationIdIndex`, eventually consistent). Field names come from the `[JsonPropertyName]` attributes on `Entities/Table.cs`:

  ```json
  [
    {
      "id": "string",
      "locationId": "string",
      "capacity": 4,
      "tableNumber": 10
    }
  ]
  ```

- `200 OK` with `[]` when the location has no tables or does not exist (no existence check).
- `400` `{ "error": "..." }` when the path parameter `id` is missing.
- Other errors propagate to the global handler (`500`).

### `DELETE /locations/{id}/dishes/{dishId}` (renamed parameter)

Removes a dish from the location's specialty dishes (`Location.SpecialtyDishIds`).

- Auth: `cognito_authorizer`; the caller's `cognito:groups` claim must contain `Waiter`, otherwise `403` `{ "error": "Forbidden: Only waiters can perform this action." }`. No own-location check (see Risks).
- Request: path parameters `id` (location id) and `dishId`; no body.
- `200 OK`: `{ "message": "Dish '<dishId>' successfully removed from location '<id>'." }` (current code; `docs/ApiRoutes.md` shows a different message, see Discrepancies).
- `400` `{ "error": "..." }` when `id` or `dishId` is missing; `400` with the `ValidationException` message when the location does not exist (`Location '<id>' not found.`) or the dish is not in its specialty dishes (`Dish '<dishId>' is not currently on the menu for this location.`).
- Other errors propagate to the global handler (`500`).

### `GET /locations/{id}/waiters` (new)

- Auth: `cognito_authorizer`. The caller must be in the Cognito group `Waiter` (`RoleHelper.IsWaiter`), as in `GET /locations/{id}/tables/all`; otherwise `403` `{ "error": "Forbidden" }`.
- Own location only: the handler takes the caller's `sub` (`UserContextHelper.GetAuthenticatedUserId`; `Waiters.userId` is the Cognito `sub`, see `WaiterService.AddWaiterToListAsync`), reads the caller's location with `IWaiterService.GetWaiterLocationIdAsync(sub)` and returns `403` `{ "error": "Forbidden" }` when the caller has no `sub`, has no item in `Waiters` (location `null`), or the location differs from `id` (ordinal comparison). The waiter list is not read in these cases.
- Order of checks: role (`403`) → `id` present and non-empty (`400`) → own location (`403`) → read the list.
- Request: path parameter `id` (location id); no query parameters, no body.
- `200 OK`: a JSON array of the waiters whose `locationId` equals `id`, read via `IWaiterService.GetWaitersByLocationIdAsync` (GSI `locationId-index`). Exactly these field names:

  ```json
  [
    {
      "userId": "string",
      "locationId": "string",
      "createdAt": "2026-10-27T12:00:00Z",
      "reservations": ["string"]
    }
  ]
  ```

  `createdAt` is the stored `WaiterListItem.CreatedAt` (`DateTime`) as an ISO 8601 string, as the response serializer writes a `DateTime`. `reservations` is `WaiterListItem.Reservations` (`List<string>`, non-nullable, defaults to `[]`), so it is an array, empty when the item has none.
- `200 OK` with `[]` when the service returns no items. The handler does not check that the location exists in `Locations` (user's decision; same as `tables/all`). With the own-location check, a caller only reaches the list for the location stored in their own `Waiters` item, which normally contains the caller, so `[]` is expected only if the GSI read lags behind (eventually consistent).
- `400` `{ "error": "Location id is required." }` when `id` is missing or empty.
- Other errors from the service propagate to the global handler (`500`).
- Order of the array: not defined.

## Data and DynamoDB changes

None. Read-only queries on the existing `Waiters` table (`GetItem` by `userId`, `Query` on GSI `locationId-index`, eventually consistent), existing repository code.

## Acceptance criteria

- **AC1.** `RouteRegistry.cs` maps `DELETE /locations/{id}/dishes/{dishId}` to `RemoveDishFromLocationHandler` and `GET /locations/{id}/tables/all` to `GetLocationTablesHandler`; no route template in `RouteRegistry.cs` contains `{locationId}`.
- **AC2.** `RemoveDishFromLocationHandler` reads the location id from the path parameter `id` and passes it to `ILocationService.RemoveDishFromLocationAsync` unchanged; with `id` or `dishId` missing it returns `400`; non-waiters still get `403`; the success response is unchanged.
- **AC3.** `GetLocationTablesHandler` reads the location id from the path parameter `id` and passes it to `ITableService.GetAllTablesForLocationAsync`; with `id` missing it returns `400`; non-waiters still get `403`; the success response is unchanged.
- **AC4.** The top-level `deployment_resources.json` has no resource path containing `{locationId}`; it defines `/locations/{id}/dishes/{dishId}` (`DELETE`, `cognito_authorizer`) and `/locations/{id}/tables/all` (`GET`, `cognito_authorizer`), each with `"enable_cors": { "state": true }`, and their other method settings unchanged.
- **AC5.** The top-level `deployment_resources.json` defines `/locations/{id}/waiters` with `"enable_cors": { "state": true }` and `GET` with `authorization_type` `cognito_authorizer`, `integration_type` `lambda`, `lambda_name` `api-handler`, `enable_proxy` `true`, in the same form as the other `api-handler` methods.
- **AC5a.** In the top-level `deployment_resources.json` API Gateway definition, the only resource without an `enable_cors` block is `/tables/{id}`, which is unchanged.
- **AC6.** `GET /locations/{id}/waiters` is registered in `RouteRegistry.cs` and is not shadowed by, and does not shadow, any other `/locations/...` route.
- **AC7.** A caller not in the `Waiter` group (no authorizer, no `cognito:groups` claim, or groups without `Waiter`) gets `403` `{ "error": "Forbidden" }` from the new handler, and the waiter service is not called.
- **AC8.** With `id` missing or empty, the new handler returns `400` `{ "error": "Location id is required." }` and does not call the waiter service.
- **AC8a.** A waiter caller whose `sub` is missing, who has no item in `Waiters`, or whose stored location differs from `id`, gets `403` `{ "error": "Forbidden" }`; `GetWaitersByLocationIdAsync` is not called. The caller's location is read with `GetWaiterLocationIdAsync(sub)`.
- **AC9.** A waiter caller whose stored location equals `id` gets `200` with a JSON array: one element per item returned by `IWaiterService.GetWaitersByLocationIdAsync(id)`, each with exactly the fields `userId`, `locationId`, `createdAt`, `reservations` (camelCase) and the item's values; `reservations` is `[]` when the item has none.
- **AC10.** When the service returns no items, the new handler returns `200` with `[]`.
- **AC11.** `docs/ApiRoutes.md` documents `DELETE /locations/{id}/dishes/{dishId}`, `GET /locations/{id}/tables/all` and the new `GET /locations/{id}/waiters` (auth, `200` body, `400`, `403`); method, path and `{param}` names agree between `RouteRegistry.cs`, `deployment_resources.json` and `docs/ApiRoutes.md`.
- **AC12.** `docs/architecture.md` line 242 (CORS bullet) and line 320 (open question 9) match the new configuration: no `{locationId}` paths; the resource count and the CORS statement are recomputed from the top-level `deployment_resources.json` (expected: 52 resources, 51 with `enable_cors`, the only exception `/tables/{id}`); open question 9 names only `/tables/{id}`; lines 229–230 state 70 routes and 70 methods (added by the user's decision after the code review).
- **AC13.** `dotnet build restaurant-backend-app.sln` and `dotnet test Tests/APIHandler.Tests.csproj` pass.

## Test scenarios

New handler test classes in `Tests/Handlers/` (existing helpers in `Tests/Handlers/HandlerTestHelpers.cs`).

| # | Scenario | AC |
|---|---|---|
| S1 | `GET /locations/{id}/waiters`, waiter caller, service returns two items with different values (one with reservations, one with an empty list) → `200`, array of two with exact camelCase fields and values | AC9 |
| S3 | Service returns an empty list → `200`, body `[]` | AC10 |
| S4 | The service is called once with the path `id` value | AC9 |
| S5 | No authorizer / no `cognito:groups` claim / groups `Customer` only → `403` `{ "error": "Forbidden" }`; service not called | AC7 |
| S6 | `PathParameters` null / without `id` / `id` empty → `400` `{ "error": "Location id is required." }`; service not called | AC8 |
| S6a | Waiter caller assigned to another location → `403`; `GetWaitersByLocationIdAsync` not called | AC8a |
| S6b | Waiter caller with no item in `Waiters` (`GetWaiterLocationIdAsync` returns `null`) → `403`; list not read | AC8a |
| S6c | Waiter caller without a `sub` claim → `403`; waiter service not called | AC8a |
| S6d | `GetWaiterLocationIdAsync` is called with the caller's `sub`; S1–S4 use a caller whose stored location equals the path `id` | AC8a, AC9 |
| S7 | Service throws → the exception propagates (no `200`) | contract |
| S8 | `GET /locations/{id}/tables/all`, waiter caller, path parameter `id` → service called with that value, `200` with the service result | AC3 |
| S9 | `tables/all` with only the old `locationId` parameter present (no `id`) → `400` | AC3 |
| S10 | `tables/all` non-waiter → `403` | AC3 |
| S11 | `DELETE /locations/{id}/dishes/{dishId}`, waiter caller → `RemoveDishFromLocationAsync(id, dishId)` called with the path values, `200` with the unchanged message | AC2 |
| S12 | `dishes/{dishId}` with `id` missing, or `dishId` missing, or only the old `locationId` parameter present → `400` | AC2 |
| S13 | `dishes/{dishId}` non-waiter → `403` | AC2 |
| S14 | Service throws `ValidationException` on remove → `400` (existing behaviour) | AC2 |

S2 (a `null` `Reservations`) was removed: `WaiterListItem.Reservations` is a non-nullable `List<string>` that defaults to `[]`.

Not unit-testable (checked by the Orchestrator or after a deploy): AC1, AC4–AC6, AC11 and AC12 by reading the files (Required checks); creation of the resources in API Gateway and CORS preflight only after a deploy.

## Risks

- After the next deploy, the two renamed endpoints appear in API Gateway for the first time. Their handlers have never run in AWS, so behaviour not covered by unit tests (for example, the `ITableService`/`ILocationService` data) is seen for the first time.
- Syndicate reports success even when resources fail: the `BadRequestException` and `AttributeError` appear only inside tracebacks under `[ERROR] ... An error occurred when processing the resource 'Unknown'`, followed by `Resource ... was configured` and `AWS resources were deployed successfully`. After the next deploy, the log in `.syndicate-config-restaurant-backend-app/logs/` must be searched for `BadRequestException`, `AttributeError` and `[ERROR]`, not judged by the final message.
- `/tables/{id}` stays without `enable_cors` (decision D1). The first deploy crashed on its CORS step with `AttributeError`; its current state in the deployed API (whether it has `OPTIONS`) was not checked. If the resource is ever deleted and recreated (for example after `syndicate clean`), the same crash is expected. Browser preflight for `PUT`/`DELETE /tables/{id}` may fail.
- `GET /locations/{id}/waiters` shows a waiter the reservation ids assigned to the other waiters of their own location. Accepted as part of the minimal contract (user's decision).
- `GET /locations/{id}/tables/all` and `DELETE /locations/{id}/dishes/{dishId}` check only the `Waiter` role, not the waiter's own location: a waiter calling the API directly can read the tables of, or remove a dish from, another location. The frontend shows a waiter only their own location, but the backend does not enforce it. Pre-existing gap, left as is (user's decision); recorded here, not fixed.
- `GET /locations/{id}/tables/all` and `DELETE /locations/{id}/dishes/{dishId}` accept an empty path parameter and pass it to the service; pre-existing, left as is (user's decision). API Gateway does not route an empty path segment to `{id}`, so this is reachable only from direct Lambda invocation or tests.
- The own-location check adds one `GetItem` on `Waiters` per call of the new endpoint.
- The contract of `GET /locations/{id}/waiters` is not taken from the instructor's version (not available); field names or status codes may differ from it.

## Open questions

1. Every Syndicate run logs `[ERROR] swagger_ui_resource.py:268:resolve_api_url ... swagger_ui_spec.json: Expecting value: line 1 column 1 (char 0)`. `swagger_src/swagger-ui/swagger_ui_config.json` points to a YAML file (`swagger-endpoints.yaml`), while Syndicate parses the spec as JSON, so the API URL is probably not resolved in the deployed Swagger UI. Not verified on the deployed Swagger UI. Out of scope; proposed as a separate task.
2. `DELETE /locations/{id}/dishes/{dishId}` success message: fix the documentation to match the code, the code to match the documentation, or leave it (see Discrepancies). Until decided, the code message stays and `docs/ApiRoutes.md` is updated to match the code.

3. **Resolved.** `docs/architecture.md` lines 229–230 said "69 routes" and "69 methods"; after this change there are 70 of each. The user explicitly asked to change them to 70 (decision log).

## Discrepancies between code and user stories

- `docs/ApiRoutes.md` (line 185) documents the `DELETE /locations/{locationId}/dishes/{dishId}` success message as `"Dish removed from location successfully."`; the code returns `"Dish '<dishId>' successfully removed from location '<locationId>'."`.
- `docs/architecture.md` lines 242 and 320 describe the two `{locationId}` resources as existing gateway resources without CORS and ask whether their preflight works; according to the Syndicate logs they have never been created in the deployed API. `docs/architecture.md` also does not mention the Syndicate `AttributeError` on `/tables/{id}` or the Swagger UI error.
- `GET /locations/{id}/waiters` has no user story in `docs/requirements-user-stories.md` and is not in `docs/project-overview.md`, `docs/architecture.md` or the frontend. It is added on the user's decision, following the route structure of the instructor's version of the API. Requirements are not changed.

## Decision log

| Date | Decision | Who |
|---|---|---|
| 2026-11-01 | Pre-flight: `.claude/agents/backend-developer.md`, `unit-test-developer.md` and `code-reviewer.md` exist | Orchestrator |
| 2026-11-01 | Cause of the missing location routes found after the 2026-11-01 deploy: sibling path variables `{id}` and `{locationId}` under `/locations` (Syndicate log, API Gateway resources). Not caused by the group pre-order changes | Orchestrator (check) |
| 2026-11-01 | Add `GET /locations/{id}/waiters`, following the instructor's version of the API | User |
| 2026-11-01 | Minimal contract from the existing `Waiters` data: `userId`, `locationId`, `createdAt`, `reservations` | User |
| 2026-11-01 | Access: `cognito_authorizer` + role `Waiter`, as `GET /locations/{id}/tables/all` | User |
| 2026-11-01 | Unknown location → `200` with `[]`, no existence check | User |
| 2026-11-01 | `enable_cors` on `/locations/{id}/dishes/{dishId}`, `/locations/{id}/tables/all` and `/locations/{id}/waiters` | User |
| 2026-11-01 | `backend-developer` may update `docs/architecture.md` lines 242 and 320 (outside section 4.4) | User |
| 2026-11-01 | One spec for both parts at `docs/specs/2026-11-01-location-routes-and-waiters.md` | User |
| 2026-11-01 | A waiter may see only their own location: `GET /locations/{id}/waiters` returns `403` unless the caller's location in `Waiters` (by `sub`) equals `id` (AC8a). `tables/all` and `dishes/{dishId}` keep the role-only check; the gap is recorded under Risks | User |
| 2026-11-01 | Full contracts of `GET /locations/{id}/tables/all` and `DELETE /locations/{id}/dishes/{dishId}` added from the current code, after the user pointed out that the table endpoint had no description | Orchestrator (per the user's comment) |
| 2026-11-01 | Keep `enable_cors` on the three resources, although the instructor's API has no `OPTIONS` on them | User |
| 2026-11-01 | The user stopped the review ("стоп - здається все не так") and ended the session; the spec stays `Draft`, no agent started | User |
| 2026-11-01 | New session: git history shows the `{locationId}` resources in the first commit `1adf4c8`; our later changes touched only group pre-order resources | Orchestrator (check) |
| 2026-11-01 | Full Syndicate log review: besides the sibling error, found the `AttributeError` on a resource without `enable_cors` (`/tables/{id}`, first deploy) and the Swagger UI spec error in every run. `enable_cors` on new resources is a technical requirement | Orchestrator (check) |
| 2026-11-01 | Open question 1 of the previous draft resolved: the gaps are the errors missed in the logs and the items below. The user accepted the Orchestrator's proposals: "давай виправимо і будемо рухатись далі" | User |
| 2026-11-01 | New endpoint error bodies: `403` `{ "error": "Forbidden" }`, `400` `{ "error": "Location id is required." }` | User (accepted proposal) |
| 2026-11-01 | Empty path parameters in the two existing handlers left as is; recorded under Risks | User (accepted proposal) |
| 2026-11-01 | Scenario S2 removed (`Reservations` is non-nullable); all handler tests are new | User (accepted proposal) |
| 2026-11-01 | Swagger UI error and compiler warnings out of scope (Open question 1) | User (accepted proposal) |
| 2026-11-01 | D1: `/tables/{id}` is not changed, `enable_cors` is not added to it; recorded under Risks | User |
| 2026-11-01 | `DELETE /locations/{id}/dishes/{dishId}` message: until decided (Open question 2), the code message stays and `docs/ApiRoutes.md` is updated to match the code | User (approved with the spec) |
| 2026-11-01 | Spec approved | User |
| 2026-11-01 | Baseline recorded (commit `8f99dea`); `backend-developer` started | User ("так") |
| 2026-11-01 | `backend-developer` code approved, `unit-test-developer` started | User ("запускай") |
| 2026-11-01 | The 400 messages of the two renamed handlers changed from `'locationId'` to `'id'` (the spec does not fix their text); left as is, tests assert the status code only | Orchestrator (user did not object) |
| 2026-11-01 | `docs/architecture.md` lines 229–230 still say 69 routes / 69 methods (now 70); outside the allowed lines, not changed; open question 3 | Orchestrator (user did not answer) |
| 2026-11-01 | `unit-test-developer` tests delivered (4 new classes, `HandlerTestHelpers.cs` extended). All task files found staged in the index; not staged by the Orchestrator or the agents, index left as is | Orchestrator (check) |
| 2026-11-01 | `/simplify` started by the user, run inline without changes (no review agents available, no direct edits), then interrupted by the user; no changes made | User |
| 2026-11-01 | Step 5: `dotnet build` succeeded (0 errors, 98 warnings, none in the changed files); `dotnet test` 354/354 passed; route check: registry 70 = gateway 70, `GET /locations/{id}/waiters` in all three sources, no `{locationId}` paths, only `/tables/{id}` without `enable_cors`. Pre-existing mismatch outside this task: `GET /guest/groups/{groupReservationId}/status` (registry, gateway) vs `GET /group-reservations/{groupReservationId}/status` (`docs/ApiRoutes.md`), already present at `8f99dea` | User ("запускай білд"); Orchestrator (run) |
| 2026-11-01 | The user committed the task as `08ac92d` ("feat: Refactor location routes and add waiters endpoint", 14:45) during step 6; the spec decision log entries after that point are uncommitted | Orchestrator (check) |
| 2026-11-01 | Step 6 `code-reviewer` (interrupted once by an API rate limit, resumed): no Blocker or Major findings; all AC implemented, AC2, AC3, AC7–AC10 covered by tests. Minor-1: `Tests/Mappers/WaiterMapperTests.cs` covers the removed S2 and uses a test folder not listed in `unit-test-developer.md`; decision pending. Outside the task: substring role check in `RemoveDishFromLocationHandler`, `Authorizer.Claims` null not guarded in `RoleHelper.IsWaiter` and `RemoveDishFromLocationHandler` (500 instead of 403, low risk), the `/status` path mismatch in `docs/ApiRoutes.md` | Orchestrator (findings checked against the code) |
| 2026-11-01 | Minor-1: delete `Tests/Mappers/WaiterMapperTests.cs` and the `Tests/Mappers/` folder (`unit-test-developer`) | User ("видаляємо") |
| 2026-11-01 | Scope extended: `backend-developer` changes `docs/architecture.md` lines 229–230 from "69 routes" / "69 methods" to 70; the user explicitly asked: "так заміни «69» на «70»". AC12 extended accordingly | User |
| 2026-11-01 | The three items outside the task are not fixed now ("все що поза задачею поки пропускаємо") | User |
| 2026-11-01 | Fixes applied (uncommitted on top of `08ac92d`): `Tests/Mappers/WaiterMapperTests.cs` and the folder deleted (`unit-test-developer`); `docs/architecture.md` lines 229–230 now say 70 routes / 70 methods (`backend-developer`). Rerun: `dotnet build` succeeded (0 errors); `dotnet test` 353/353 passed (354 minus the deleted test); `GetLocationWaitersHandlerTests` 24/24 passed | Orchestrator (check and run) |
| 2026-11-01 | Re-review by `code-reviewer` of the two fixes skipped; deploy requested ("пропускаємо повторне ревью, роби деплой") | User |
| 2026-11-01 | Deploy: account `131470772794`, region `eu-north-1` checked (`aws sts`, Syndicate config). `syndicate build` (bundle `restaurant-backend-app_261101.160406`) and `syndicate update --deploy_name dev` done. New log lines checked: no `BadRequestException`, no `AttributeError`; the three resources created with methods and CORS; only the known Swagger UI `[ERROR]` (Open question 1) and resolved `ResourceConflictException` retries on Lambda configuration. API Gateway `yfdxslplfd`: `/locations/{id}/dishes/{dishId}` DELETE+OPTIONS, `/locations/{id}/tables/all` GET+OPTIONS, `/locations/{id}/waiters` GET+OPTIONS; stage `develop` redeployed 16:10. Without a token all three return `401 Unauthorized` (before: `403 Missing Authentication Token`); CORS preflight returns 200 for all three. Calls with a real waiter token not run | User (deploy), Orchestrator (run and check) |
| 2026-11-01 | Read-only check of the deployed API Gateway (`aws apigateway get-resources`) not run, to save time | Orchestrator (user did not request it) |
