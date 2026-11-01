# Spec: Group pre-order owner endpoints (homework levels 1–3)

Source task: [homework-backend.md](../../homework-backend.md) (in Ukrainian), all three levels. Related stories: US12 (owner consolidated view), US14 (owner closing the group pre-order), US1 and US10 (group session, guest limit) in [requirements-user-stories.md](../requirements-user-stories.md).

All backend paths below are relative to `backend/restaurant-backend-app/`; `api-handler/` means `dnapp/lambdas/api-handler/`.

## Goal

Give the reservation owner the server side of the group pre-order management flow:

1. See all participants of the group pre-order with their dishes, per-participant totals and the table total (level 1).
2. Get the id of their own owner session for a reservation, and submit the consolidated group pre-order to the restaurant's order (level 2).
3. Remove a participant's session from the group and get the updated snapshot (level 3; the endpoint exists and is brought to the contract).

## Scope

### In scope

- `GET /guest/groups/{groupReservationId}/sessions`: new handler, service method, route, gateway resource (renaming the existing singular `/guest/group/...` resource).
- `GET /reservations/{reservationId}/guest-session-id`: new handler, service method, route, gateway resource; get-or-create of the owner session (wires up the existing `GuestSessionService.CreateGroupOwnerSessionAsync` or equivalent).
- `POST /group-reservations/{groupReservationId}/submit`: new handler, request DTO, service method, route, gateway resource.
- `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}`: bring the existing implementation to the contract (camelCase response, shared snapshot builder, new guards, no 500 on missing dishes).
- One shared "group snapshot" builder used by both `GET .../sessions` and `DELETE .../sessions/{guestSessionId}`.
- Excluding the owner session from the guest-limit and participant counts in existing code paths (a direct consequence of creating owner sessions).
- `docs/ApiRoutes.md` entries for the four endpoints.
- Root `docs/architecture.md`: the section 4.4 endpoint status table and the other statements in it that this change makes false (listed under "Affected Lambdas and files").
- Unit tests: service tests and handler tests (see "Test scenarios").
- Fixes for findings 1–3 of the `code-review` skill run of 2026-11-01 (see Decision log):
  - the group submit writes the order and the group status in one DynamoDB transaction, with a condition that the group is `Open` (AC18);
  - the group submit applies the 30-minute pre-order cutoff (AC19);
  - the owner session gets a deterministic id and is created with a conditional write (AC20).

### Out of scope

- Frontend changes, including how the SPA sends a Cognito token to `/guest/...` owner endpoints (the interceptor skips `/guest/` URLs; `docs/architecture.md` section 12, question 6).
- Letting the owner add or remove dishes in, or submit, their own owner session (the owner session has no guest token; see Open questions).
- Freezing the snapshot at submit or closure (US12 AC10): the snapshot is always built from the current `Dishes` data (see Discrepancies).
- Real-time updates (US12 AC8, AC14–AC18), payment mode (US12 AC19–AC21), automatic closure at the deadline (US14 AC3), waiter view (US16).
- The 30-minute cutoff for anything other than the group submit: owner-session creation, `GET .../sessions`, `DELETE .../sessions/{guestSessionId}` and the guest cart endpoints.
- A condition on the order item in the submit transaction (concurrent order edits; see Risks).
- A write-time check of the group status in the guest cart endpoints (`code-review` finding 8; needs `dynamodb:ConditionCheckItem`, which the IAM policy does not grant; see Risks).
- `code-review` findings 4–10 of 2026-11-01: recorded under Risks and Open questions, not fixed.
- `swagger_src/` (generated Swagger artifacts), `docs/Diagrams.md`, `reports-handler`, `report-sender`, `infra/terraform`.
- Fixing other known gaps in `docs/architecture.md` section 12 (for example, the missing ownership check in `GET /reservations/{reservationId}/invite-token`).

## Affected Lambdas and files

Only `api-handler`. Expected files (the developer may add files under the same folders as needed; projects are SDK-style):

- `api-handler/Handlers/GuestSessions/`: new `GetGroupSessionsHandler` (name indicative), changed `DeleteGuestSessionHandler.cs`.
- `api-handler/Handlers/GroupReservations/` (or `GuestSessions/`): new handlers for `GET .../guest-session-id` and `POST .../submit`.
- `api-handler/Services/GuestSessions/GuestSessionService.cs`, `IGuestSessionService.cs`: snapshot builder, get sessions, delete changes, get-or-create owner session, owner exclusion from counts.
- `api-handler/Services/GroupReservations/GroupReservationService.cs`, `IGroupReservationService.cs` (or another service in `Services/`): group submit; owner exclusion from the guest count in `GetGroupReservationByTokenAsync`. New constructor dependencies (for example `IOrderRepository`, `IDishRepository`) are allowed.
- `api-handler/Repositories/GuestSessions/`: owner exclusion from counts; a strongly consistent load of a session by id and a conditional create of the owner session (AC20).
- `api-handler/Repositories/GroupReservations/` and/or `api-handler/Repositories/Orders/`: one method that writes the order and the group status in a single `TransactWriteItems` call (AC18). Where it lives is the developer's choice; `IAmazonDynamoDB` is already registered in `RouteRegistry.cs`.
- `api-handler/Services/Orders/OrderService.cs`: only if the 30-minute cutoff rule is extracted into a shared helper for AC19; the behaviour of `OrderService` must not change.
- `api-handler/Models/GuestSessions/` and/or `Models/GroupReservation/`: response DTO for the owner session id, request DTO for submit, response item DTO for submit. `Models/GuestSessions/GetAllResponse.cs` may change if needed for camelCase.
- `api-handler/Routing/RouteRegistry.cs`: three new routes; DI changes if a service gets new dependencies.
- Top-level `deployment_resources.json`: rename one resource, add two resources.
- `docs/ApiRoutes.md`.
- Root `docs/architecture.md`, only these parts:
  - section 4.4: the endpoint status table, and the paragraph "The owner has no session yet" (owner sessions are now created by `GET /reservations/{reservationId}/guest-session-id`);
  - section 7.1: the route and method counts, the sentence about the singular `/guest/group/...` gateway path, and the sentence listing planned group pre-order endpoints that exist in neither the gateway nor `RouteRegistry`;
  - sections 5.3 and 7.2: the gateway method and resource counts that this change alters;
  - section 12: questions 7 and 8, marked as resolved with a link to this spec;
  - section 12, question 6: add that `GET /guest/groups/{groupReservationId}/sessions` is also an owner-only endpoint under `/guest/` that needs a Cognito token (today the question names only the `DELETE`). The question itself stays open.
- Tests (owned by `unit-test-developer`): `Tests/Services/GuestSessionServiceTests.cs`, `Tests/Services/GroupReservationServiceTests.cs` (or the test class of whichever service hosts submit), new `Tests/Handlers/<HandlerName>Tests.cs` for each new or changed handler, `Tests/Repositories/GuestSessionRepositoryTests.cs` and a test class for the new transactional repository method (where they can be tested with a mocked `IAmazonDynamoDB`/`IDynamoDBContext`), and any existing test broken by constructor or signature changes.

## API contract changes

Common rules for all four endpoints:

- Gateway `authorization_type: cognito_authorizer`, `enable_cors: { "state": true }`, `lambda_name: api-handler`, proxy integration, same shape as the existing `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` resource.
- The handler reads the caller id from the `sub` claim. Missing claims or `sub` → `401` `{ "error": "Unauthorized." }`.
- "Owner" means `reservation.CustomerId == sub` for the reservation the group belongs to. Not the owner → `403` `{ "error": "..." }` (`ActionForbiddenException`).
- Not found → `404` `{ "error": "..." }` via `ResponseBuilder.CreateErrorResponse`.
- Domain validation errors → `400` `{ "message": "...", "errorCode": "..." }` via `ResponseBuilder.CreateErrorCodeResponse`. Group submitted → `errorCode: "GroupReservationSubmitted"`; group closed or reservation not `Reserved` → `"GroupReservationExpired"`; other domain 400s → `"Unknown"`.
- Input errors detected in the handler (missing path parameter, missing or unparsable body) → `400` `{ "error": "..." }`, as existing handlers do.
- Note for handlers: `GroupReservationNotFoundException`, `GuestSessionNotFoundException` and `ReservationNotFoundException` all derive from `ValidationException`, so they must be caught before `ValidationException` to return 404.
- Success bodies use camelCase field names (`ResponseBuilder.CreateJsonResponse`, or `[JsonPropertyName]` on every serialized property including nested ones). Enum `type` is serialized as a string.

### Group snapshot (shared response of level 1 and level 3)

```json
{
  "participants": [
    {
      "id": "session1",
      "type": "Guest",
      "name": "Andrii",
      "items": [
        {
          "dish": {
            "id": "dish1",
            "name": "Caesar Salad",
            "description": "string",
            "dishType": "string",
            "price": 180,
            "weight": "string",
            "calories": "string",
            "proteins": "string",
            "fats": "string",
            "carbohydrates": "string",
            "vitamins": "string",
            "imageUrl": "string|null",
            "state": "Available"
          },
          "quantity": 2
        }
      ],
      "total": 360,
      "isSubmitted": true
    }
  ],
  "total": 360
}
```

- `participants`: one entry per `GuestSession` of the group, including the owner session if it exists. The owner participant, if present, comes first; guests follow in repository order.
- `id`: the guest session id. `type`: `"Owner"` for a session with `IsOwnerSession == true`, otherwise `"Guest"`.
- `name`: for a guest, `GuestName`; for the owner, the Cognito `given_name` of `reservation.CustomerId`, falling back to the session's stored `GuestName` if the user or attribute is not found.
- `items`: each cart line with the full `DishResponse` (`DishMapper.ToDishResponse`) and `quantity`. A cart line whose dish no longer exists in the `Dishes` table is omitted (no 500).
- `total` (participant): sum of `price × quantity` over items whose dish state is `Available`. Items with any other state (`On stop`, `Out of stock`) stay in `items`, with their `state`, but are excluded from the total (US12 AC9).
- `isSubmitted`: the session's `IsSubmitted`.
- `total` (top level): sum of the participant totals. No sessions → `participants: []`, `total: 0` (US12 AC7). Empty cart → `items: []`, `total: 0` (US12 AC6).

The superset `DishResponse` (instead of only `id`, `name`, `price` from the homework example) is a recorded decision.

### Level 1. `GET /guest/groups/{groupReservationId}/sessions`

- Gateway: rename the resource `/guest/group/{groupReservationId}/sessions` to `/guest/groups/{groupReservationId}/sessions` (keep `GET`, `cognito_authorizer`, CORS). The singular path stops existing.
- Check order: group reservation not found → 404; its reservation not found → 404; caller not the owner → 403.
- Allowed in any group and reservation status (read-only; US12 AC10).
- `200` with the group snapshot.

### Level 2.1. `GET /reservations/{reservationId}/guest-session-id`

- Gateway: new resource `/reservations/{reservationId}/guest-session-id`, `GET`.
- Check order: reservation not found → 404; caller not the owner → 403; no group reservation for the reservation → 404.
- The owner session id is deterministic: `owner-{groupReservationId}`. It has no `#`, because the id is used in URL paths.
- Look up the owner session by this id with a strongly consistent read (`GetItem` with `ConsistentRead = true`); the `groupReservationId` GSI is not used for this lookup. If it exists, return its id, whatever the group or reservation status.
- Otherwise, if the group status is `Open` and the reservation status is `Reserved`, create the owner session (`Id = owner-{groupReservationId}`, `IsOwnerSession = true`, `OwnerUserId = sub`, `GroupReservationId` = the group, `Dishes = []`, no token hash) with a conditional put (`attribute_not_exists(guestSessionId)`) and return its id. If the condition fails because another call created the session in between, return `owner-{groupReservationId}` with `200`; this is not an error.
- No fallback lookup for owner sessions with random ids: no deployed version created them.
- Otherwise do not create anything: group `Submitted` → 400 `GroupReservationSubmitted`; group `Closed` or reservation not `Reserved` → 400 `GroupReservationExpired`.
- `200`:

```json
{ "guestSessionId": "owner-session-123" }
```

### Level 2.2. `POST /group-reservations/{groupReservationId}/submit`

- Gateway: new resource `/group-reservations/{groupReservationId}/submit`, `POST`.
- Request:

```json
{ "reservationId": "reservation-123" }
```

  Missing or unparsable body, or empty `reservationId` → 400.
- Check order (each failure stops processing; nothing is written on any failure):
  1. group reservation not found → 404;
  2. `reservationId` from the body differs from the group's `ReservationId` → 400 (`Unknown`);
  3. reservation not found → 404;
  4. caller not the owner → 403;
  5. group `Submitted` → 400 `GroupReservationSubmitted`; group `Closed` → 400 `GroupReservationExpired`; reservation status not `Reserved` → 400 `GroupReservationExpired`;
  5a. the current UTC time is later than the reservation start time (UTC) minus 30 minutes → 400 `GroupReservationExpired`. Same rule and boundary as `OrderService.EnsurePreOrderEditable`: a submit exactly at the cutoff is still allowed;
  6. no session of the group with `IsSubmitted == true` → 400;
  7. consolidate: take the cart lines of submitted sessions only (guest and owner alike), drop lines whose dish no longer exists in `Dishes` or whose state is not `Available`, merge lines with the same `dishId` by summing `quantity`. Result empty → 400;
  8. active order for the reservation not found (`IOrderRepository.GetOrderByReservationIdAsync` returns null) → 400.
- On success:
  - add the consolidated lines to `order.Dishes`: for a `dishId` already in the order, add the quantity to the existing line (keep its `State`); otherwise append a new `OrderItem` with `State = Pending`;
  - set `order.Status = SubmittedByCustomer` and the group reservation status to `Submitted`;
  - write both in one `TransactWriteItems` call: a `Put` of the order item (same table, key and attribute mapping as `IOrderRepository.SaveOrderAsync`) and an `Update` of the group reservation that sets `status` to `Submitted` with the condition `status = Open`. The condition value must use the same representation in which `DynamoDBContext` stores `GroupReservationStatus`;
  - if the transaction is canceled because the group condition failed, nothing is written: re-read the group reservation; `Submitted` → 400 `GroupReservationSubmitted`; `Closed` → 400 `GroupReservationExpired`; any other status, or the group no longer found on the re-read → the error propagates (500). Any other transaction failure propagates (500); DynamoDB guarantees that nothing is partially written.
- `200` with the consolidated lines from step 7 (not the whole order), camelCase, order of elements not specified:

```json
[
  { "dishId": "dish1", "quantity": 3 },
  { "dishId": "dish2", "quantity": 2 }
]
```

### Level 3. `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}`

- Gateway: unchanged.
- Check order (nothing is deleted on any failure):
  1. group reservation not found → 404;
  2. its reservation not found → 404;
  3. caller not the owner → 403;
  4. group `Submitted` → 400 `GroupReservationSubmitted`; group `Closed` → 400 `GroupReservationExpired`; reservation status not `Reserved` → 400 `GroupReservationExpired` (US12 AC11);
  5. guest session not found → 404;
  6. guest session belongs to another group → 400;
  7. guest session is the owner session → 400.
- On success: delete the session and return `200` with the group snapshot of the remaining sessions (same format as level 1, now camelCase).

## Data and DynamoDB changes

- No new tables, GSIs or attributes. IAM is unchanged: the shared `lambda-basic-execution` policy (`Resource: "*"`) grants `dynamodb:PutItem` and `dynamodb:UpdateItem`, which a `TransactWriteItems` call with a `Put` and an `Update` is authorized against, and covers Cognito. It does not grant `dynamodb:ConditionCheckItem`, so the transaction must not use a `ConditionCheck` item.
- `GuestSessions`: owner sessions are now written (existing attributes `isOwnerSession`, `ownerUserId`) with the deterministic hash key `guestSessionId = owner-{groupReservationId}`. The owner session is read by key with `ConsistentRead = true` and created with a conditional `PutItem` (`attribute_not_exists(guestSessionId)`). The `groupReservationId` GSI is still used to list the group's sessions.
- Owner sessions are excluded from:
  - the guest-limit check in `GuestSessionService.CreateGuestSessionAsync`;
  - the guest-limit check in `GroupReservationService.GetGroupReservationByTokenAsync`;
  - the participant count in `GuestSessionService.GetGroupReservationStatusAsync` (which already adds 1 for the owner).
- `Orders` (hash key `orderId`, range key `customerId`): the active order is read through the existing `GSI_ReservationId` path (`IOrderRepository.GetOrderByReservationIdAsync`) and written as a whole item by the `Put` of the submit transaction.
- `GroupReservations` (hash key `groupReservationId`): `status` set to `Submitted` by the `Update` of the submit transaction, conditioned on `status = Open`. `GroupReservation.Status` has no converter, so it is stored by the default `DynamoDBContext` enum conversion; the condition value must match it.
- The submit no longer calls `IOrderRepository.SaveOrderAsync` or `IGroupReservationRepository.SaveGroupReservationAsync`.

## Acceptance criteria

- **AC1.** `GET /guest/groups/{groupReservationId}/sessions`, `GET /reservations/{reservationId}/guest-session-id` and `POST /group-reservations/{groupReservationId}/submit` are registered in `RouteRegistry.cs` and exist in the top-level `deployment_resources.json` with `cognito_authorizer` and CORS; method, path and `{param}` names agree with `docs/ApiRoutes.md`. The gateway no longer has `/guest/group/{groupReservationId}/sessions`.
- **AC2.** `GET .../sessions`: missing `sub` → 401; unknown group → 404; group whose reservation is missing → 404; caller not the owner → 403.
- **AC3.** `GET .../sessions` returns 200 with the group snapshot: one participant per session with `id`, `type`, `name`, `items` (full dish and `quantity`), `total`, `isSubmitted`, and a top-level `total`; all field names camelCase and `type` as a string.
- **AC4.** Totals: a participant's `total` is the sum of `price × quantity` of their items whose dish state is `Available`; items in another state are listed with their `state` but excluded; the top-level `total` is the sum of participant totals; a participant with an empty cart has `items: []` and `total: 0`; a group without sessions returns `participants: []` and `total: 0`.
- **AC5.** A cart line whose dish is missing from `Dishes` is omitted from the snapshot, and the request still returns 200.
- **AC6.** The owner session, if present, appears first with `type: "Owner"` and the owner's Cognito `given_name` (fallback: the stored `GuestName`); guests have `type: "Guest"` and their `GuestName`.
- **AC7.** `GET .../guest-session-id`: missing `sub` → 401; unknown reservation → 404; caller not the owner → 403; no group reservation for the reservation → 404.
- **AC8.** `GET .../guest-session-id`: an existing owner session's id is returned without creating another one; with no owner session, an `Open` group and a `Reserved` reservation, an owner session (`IsOwnerSession = true`, `OwnerUserId` = caller) is created and its id returned as `{ "guestSessionId": "..." }`; with no owner session and a group not `Open` or a reservation not `Reserved`, 400 with the error code from the contract and nothing is created.
- **AC9.** An owner session in the group is not counted as a joined guest in `CreateGuestSessionAsync`, `GetGroupReservationByTokenAsync` or `GetGroupReservationStatusAsync`.
- **AC10.** `POST .../submit`: missing `sub` → 401; missing or invalid body or empty `reservationId` → 400.
- **AC11.** `POST .../submit`: unknown group → 404; body `reservationId` not the group's reservation → 400; reservation missing → 404; caller not the owner → 403.
- **AC12.** `POST .../submit`: group `Submitted` → 400 `GroupReservationSubmitted`; group `Closed` → 400 `GroupReservationExpired`; reservation not `Reserved` → 400 `GroupReservationExpired`.
- **AC13.** `POST .../submit`: no submitted session → 400; no dishes left after consolidation → 400; no active order → 400. On every error in AC10–AC13, neither the order nor the group reservation is saved.
- **AC14.** `POST .../submit` success: only submitted sessions are used; unavailable and missing dishes are dropped; lines are merged by `dishId`; they are added to the order (quantities summed into existing lines, new lines `Pending`); the order is written with `SubmittedByCustomer` and the group reservation with `Submitted`, in one transaction (AC18); 200 with the consolidated `[{ "dishId", "quantity" }]` list in camelCase.
- **AC15.** `DELETE .../sessions/{guestSessionId}`: missing `sub` → 401; unknown group → 404; reservation missing → 404; caller not the owner → 403; group `Submitted` or `Closed`, or reservation not `Reserved` → 400 with the error code from the contract; unknown session → 404; session of another group → 400; owner session → 400. On every error, nothing is deleted.
- **AC16.** `DELETE .../sessions/{guestSessionId}` success: the session is deleted and 200 returns the snapshot of the remaining sessions in the same format and with the same rules as AC3–AC6.
- **AC17.** `docs/ApiRoutes.md` describes the four endpoints as in "API contract changes" (request, response, error statuses), and `docs/architecture.md` is updated in the parts listed under "Affected Lambdas and files".
- **AC18.** `POST .../submit` writes the order and the group status in one `TransactWriteItems` call (a `Put` of the order and an `Update` of the group with the condition `status = Open`); no code path of the submit saves them with separate writes. If the group condition fails, nothing is written, and the request returns 400 `GroupReservationSubmitted` when the re-read group is `Submitted`, or 400 `GroupReservationExpired` when it is `Closed`. A second submit of the same group therefore does not add the dishes to the order again.
- **AC19.** `POST .../submit` later than 30 minutes before the reservation start returns 400 `GroupReservationExpired`, and nothing is written. A submit before the cutoff proceeds. The check runs after the group and reservation status checks (step 5a).
- **AC20.** `GET .../guest-session-id` uses the owner session id `owner-{groupReservationId}`: the lookup is a strongly consistent read by this key (no GSI lookup); a new owner session is created with this id by a conditional put; if the conditional put fails because the session already exists, the request returns 200 with `{ "guestSessionId": "owner-{groupReservationId}" }` and no second session exists. AC8 still holds.

## Test scenarios

Service tests mock repositories (and `IUserRepository` for the owner name); handler tests mock the service interfaces and build `APIGatewayProxyRequest` with path parameters, body and `RequestContext.Authorizer.Claims`.

Snapshot (level 1 and level 3 share these):

- S1 (AC3, AC4): two guests, one with two lines, one with one; totals and top total correct; `isSubmitted` passed through.
- S2 (AC4): a guest with one `Available` and one `OnStop` dish: both in `items`, only the available one in `total`; also `OutOfStock`.
- S3 (AC4): guest with an empty cart → `items` empty, `total` 0; group without sessions → empty list, 0.
- S4 (AC5): a cart line whose dish is not returned by `GetDishesByIdsAsync` is omitted, no exception.
- S5 (AC6): owner session first, `type` Owner, name from Cognito `given_name`; user not found → stored `GuestName`.
- S6 (AC3, handler): response body is camelCase (`participants`, `total`, `isSubmitted`, `dish`, `quantity`), `type` is `"Guest"`/`"Owner"`.

`GET .../sessions`:

- S7 (AC2): unknown group → `GroupReservationNotFoundException`/404; missing reservation → 404; non-owner → `ActionForbiddenException`/403; handler without `sub` → 401; missing path parameter → 400.

`GET .../guest-session-id`:

- S8 (AC8, AC20): the session `owner-{groupReservationId}` exists → its id returned, nothing created, even if the group is `Submitted`.
- S9 (AC8, AC20): no owner session, group `Open`, reservation `Reserved` → one session created with `Id = owner-{groupReservationId}`, `IsOwnerSession = true`, `OwnerUserId` = caller, correct `GroupReservationId`, through the conditional create; returned id equals the created id.
- S9a (AC20): the conditional create reports that the session already exists → 200 with `owner-{groupReservationId}`, no exception, no other create attempt.
- S9b (AC20): the service does not read the group's session list (`GetAllByGroupReservationIdAsync`) to find the owner session.
- S10 (AC8): no owner session and group `Submitted` → 400 `GroupReservationSubmitted`; `Closed` → `GroupReservationExpired`; reservation `Cancelled` → `GroupReservationExpired`; no create attempted.
- S11 (AC7): unknown reservation → 404; non-owner → 403; no group → 404; handler without `sub` → 401; response body `{ "guestSessionId": ... }`.

Owner exclusion:

- S12 (AC9): with N guest sessions plus one owner session, `CreateGuestSessionAsync` and `GetGroupReservationByTokenAsync` apply the limit as for N guests (a guest who fits by the guest count is admitted; the limit is still enforced at the boundary); `GetGroupReservationStatusAsync` reports N + 1 participants, not N + 2.

`POST .../submit`:

- S13 (AC14): two submitted sessions sharing `dish1` and one unsubmitted session → response contains `dish1` with summed quantity and the other dishes; the unsubmitted session's dishes are absent.
- S14 (AC14, AC18): order already has `dish1` (quantity 1, state `Confirmed`) and `dish9` → after submit `dish1` quantity increased and state kept, `dish9` unchanged, new dishes appended as `Pending`; the transactional write receives the order with `SubmittedByCustomer` and the group with `Submitted`.
- S15 (AC14): a submitted session with an `OnStop` dish and a dish missing from `Dishes` → both dropped from the response and the order.
- S16 (AC13): no submitted sessions → 400; submitted sessions with empty carts → 400; submitted sessions whose dishes are all unavailable → 400; no active order → 400; in each case the transactional write, `SaveOrderAsync` and `SaveGroupReservationAsync` are never called.
- S17 (AC11, AC12): unknown group → 404; body `reservationId` mismatch → 400; missing reservation → 404; non-owner → 403; group `Submitted` / `Closed`, reservation not `Reserved` → 400 with the right error codes; nothing written.
- S18 (AC10, handler): no `sub` → 401; no body, invalid JSON, empty `reservationId` → 400; success body is a camelCase array of `dishId`/`quantity`.
- S22 (AC18): success → the transactional write is called exactly once; `SaveOrderAsync` and `SaveGroupReservationAsync` are never called.
- S23 (AC18): the transactional write reports a failed group condition → the group is re-read; `Submitted` → `GroupReservationSubmittedException` (400 `GroupReservationSubmitted`); `Closed` → `GroupReservationExpiredException` (400 `GroupReservationExpired`); no other write happens.
- S24 (AC19): reservation starts in 20 minutes → 400 `GroupReservationExpired`, nothing written; starts in 40 minutes → submit proceeds. Do not test the exact boundary against the real clock.
- S25 (AC18, AC20, repository, where testable with a mocked `IAmazonDynamoDB`): the `TransactWriteItems` request has a `Put` on the orders table and an `Update` on the group reservations table with the `status = Open` condition and no `ConditionCheck` item; a canceled transaction with a failed group condition is reported to the service as a condition failure; the owner session create uses `attribute_not_exists(guestSessionId)` and a `ConditionalCheckFailedException` is reported as "already exists"; the owner lookup by id uses `ConsistentRead = true`.

`DELETE .../sessions/{guestSessionId}`:

- S19 (AC16): deleting a guest session calls `DeleteAsync` with that session and returns the snapshot of the remaining sessions.
- S20 (AC15): unknown group → 404; missing reservation → 404; non-owner → 403; group `Submitted`/`Closed` → 400; group `Open` but reservation `Cancelled` (or another non-`Reserved` status) → 400 `GroupReservationExpired`; unknown session → 404; session of another group → 400; owner session → 400; `DeleteAsync` never called in any of these.
- S21 (AC15, handler): no `sub` → 401; exception-to-status mapping (404 for the three not-found exceptions, 403, 400 with `errorCode`).

## Risks

- **The submit transaction has no condition on the order** (AC18). The order item is written as a whole, as `SaveOrderAsync` does, so a customer or waiter edit of the order between the read and the transaction is lost. Out of scope.
- **A concurrent submit can get 500 instead of 400** (AC18). When two submit transactions touch the same group item at the same time, DynamoDB may cancel the losing one with the reason `TransactionConflict` rather than `ConditionalCheckFailed`; that is "any other transaction failure", so it propagates as 500. Nothing is written, and a retry gets 400 `GroupReservationSubmitted`. Not verified against a deployed table (re-review Minor 2, 2026-11-01).
- **A retry after a successful submit gets 400 `GroupReservationSubmitted`.** If the first submit succeeded but its response was lost, the retry is rejected instead of returning the consolidated list again; the client should treat this code as "already submitted".
- **Enum representation in the transaction condition.** `GroupReservation.Status` has no converter, so its stored form comes from the default `DynamoDBContext` enum conversion. If the condition value uses another form (for example a string instead of a number), every submit fails the condition. Unit tests with a mocked `IAmazonDynamoDB` check only the request shape; a check against a deployed table is not run.
- **IAM for the transaction is not verified against a deployment.** The `Put` and the `Update` are authorized by `dynamodb:PutItem` and `dynamodb:UpdateItem`, which the policy grants; a `ConditionCheck` item would need `dynamodb:ConditionCheckItem`, which it does not.
- **The cutoff applies to the submit only** (AC19). After the cutoff, guests can still edit and submit their carts and the owner can still remove guests, but the group can no longer be submitted, so those carts never reach the order.
- **Guest cart changes during the group submit are lost** (`code-review` finding 8). Guest submit and cart edits check the group status only when they read it; a guest who submits after the group submit has read the sessions but before its transaction gets 200, and their dishes are not in the order. Out of scope.
- **Owner session for a one-guest reservation** (`code-review` finding 9). A group exists only if the reservation had `GuestCount >= 2` when it was created or updated; if `EditReservationHandler` later lowers it to 1, `GET .../guest-session-id` still creates an owner session, while `GET .../invite-token` returns 400. No guest can join such a group (the guest limit rejects every guest). Recorded only.
- **Session counting reads full items** (`code-review` finding 10). `CountJoinedGuestsByGroupReservationIdAsync` loads every session of the group, carts included, and counts in memory, as the earlier `CountByGroupReservationId` did. A `FilterExpression` or `Select = COUNT` would reduce the transferred data and latency, but not the consumed read capacity. Recorded only.
- **The owner session cannot be used yet.** It has no guest token, and guest endpoints authorize by token, so the owner cannot add dishes to it or submit it; its cart stays empty unless this is addressed later.
- **Frontend cannot call the owner `/guest/...` endpoints** with a Cognito token today, because the interceptor skips `/guest/` URLs (`docs/architecture.md` section 12, question 6).
- **Response format change of the existing DELETE**: its keys change from PascalCase to camelCase. Nothing in the frontend calls it today (checked: no reference in `frontend/restaurant-frontend-app/src`).
- **Renaming the gateway resource** removes `/guest/group/{groupReservationId}/sessions` on the next deployment; it never had a Lambda route.
- **Exception hierarchy:** not-found exceptions derive from `ValidationException`; catching `ValidationException` first would turn 404s into 400s.
- **Submit merges into `Canceled` and `Confirmed` order lines.** Per the merge rule (keep the existing line's `State`), group dishes added to a line the waiter has canceled stay `Canceled`, so they are effectively lost; quantity added to a `Confirmed` line becomes confirmed without the waiter confirming it. Appending a separate `Pending` line is not a fix: waiter confirm/cancel (`OrderService.ConfirmDishInOrder` / `CancelDishInOrder`) takes the first line with the `dishId`, so the order model assumes one line per `dishId`. Kept as specified by the user's decision; see Open questions.
- **Handler check order: path parameter before `sub`.** The four handlers of this spec return 400 for a missing path parameter before checking `sub` (401); a request missing both gets 400. This follows the pre-existing `DeleteGuestSessionHandler`, while other Cognito handlers in the project (for example `CancelPreOrderDishHandler`, `GetReservationByIdHandler`) check `sub` first. Not pinned by tests; unreachable through API Gateway, which always passes path parameters and rejects requests without a valid token at the `cognito_authorizer`. Recorded only.
- **Cognito failure in the snapshot returns 500.** The owner's `given_name` falls back to the stored `GuestName` only when the user or attribute is not found. A Cognito error (network, throttling) is not caught and becomes 500 for `GET .../sessions`, and for `DELETE` after the session has already been deleted; a retry of that `DELETE` then gets 404, so the client does not get the updated snapshot from it (`code-review` finding 7). Kept as is by the user's decision, consistent with other endpoints.

## Open questions

1. Resolved 2026-11-01: the group submit respects the 30-minute cutoff and returns 400 `GroupReservationExpired` after it (AC19). Without it, lines added as `Pending` after the cutoff could never be confirmed or canceled by the waiter, because `ConfirmPreOrderDishAsync` and `CancelPreOrderDishAsync` enforce the cutoff (`code-review` finding 2).
2. How should the owner add dishes to and submit their owner session (a token for the owner session, or Cognito-authorized variants of the cart endpoints)? Out of scope.
3. Resolved 2026-11-01: the submit is atomic, with one DynamoDB transaction conditioned on the group being `Open` (AC18; `code-review` finding 1).
4. "Unavailable" in this spec means any dish state other than `Available` (`On stop` and `Out of stock`); US12 AC9 names only "On Stop". Confirm that `Out of stock` is treated the same way.
5. US14 AC2 says the closed order gets the status "Closed", while the homework sets the group reservation to `Submitted`; this spec follows the homework. How `Submitted` and `Closed` relate in the product (see Discrepancies)?
6. How should the group submit treat an order line with the same `dishId` that the waiter has already `Canceled` or `Confirmed` (for example, reset it to `Pending`)? Not changed in this spec (see Risks; also `code-review` finding 4).
7. Should a Cognito failure while reading the owner's `given_name` fall back to the stored name instead of returning 500? Not changed in this spec (see Risks; also `code-review` finding 7).
8. Should the group submit set the order to `SubmittedByCustomer` only when it added at least one `Pending` line (`code-review` finding 5)? Today, per AC14, it always does: if every group dish merges into existing `Confirmed` lines, a `Submitted` order goes back to `SubmittedByCustomer` with nothing pending. The waiter can bring it back to `Submitted` only by confirming a dish again (`ConfirmDishInOrder` does not check the line's state). The individual `SubmitPreOrderAsync` also sets the status unconditionally. Not changed in this spec.
9. Should the group submit create the order when the owner has no active order (`code-review` finding 6)? Today it returns 400 (step 8), and orders are created only through `CreateOrderHandler`, so a group whose owner never placed an individual pre-order cannot be submitted. Not changed in this spec.

## Discrepancies between code and user stories

- US14 AC2: the closed group order gets the status "Closed". The code has `GroupReservationStatus.Submitted` and `Closed`, where `Closed` is set when the reservation leaves `Reserved`; the homework submit sets `Submitted`. Recorded only.
- US12 AC10: after closure, the consolidated view must show items and totals fixed as of closure. The snapshot in this spec is built from the current `Dishes` data (price, state, existence), so after submit or closure a price change, a state change (for example to `On stop`) or a deleted dish changes the items and totals shown. Freezing the snapshot is out of scope. Recorded only.
- US12 AC8, AC14–AC18 require real-time updates of the consolidated view; the backend offers only request/response endpoints. Recorded only.
- US12 AC9: the existing `DELETE` snapshot counted unavailable dishes in totals; this spec fixes it for the snapshot.
- `docs/ApiRoutes.md` today describes `GET /guest/groups/{groupReservationId}/sessions` as an array of sessions and `POST /group-reservations/{groupReservationId}/submit` without a body and with a `message` response; the homework contract replaces both.
- `swagger_src/swagger-ui/swagger-endpoints.yaml` describes the submit as `POST /guest/groups/{groupReservationId}/submit`, which differs from the homework path. Out of scope; recorded only.
- `docs/ApiRoutes.md` lists `GET /group-reservations/{groupReservationId}/status`, while the implemented endpoint is `GET /guest/groups/{groupReservationId}/status` (`docs/architecture.md` section 7.1). Out of scope.

## Decision log

| Date | Decision | Decided by |
|---|---|---|
| 2026-10-31 | Pre-flight: all three agent definitions (`backend-developer`, `unit-test-developer`, `code-reviewer`) exist | Orchestrator (check) |
| 2026-10-31 | Task: all three levels of `homework-backend.md` | User |
| 2026-10-31 | Owner session is created on the first call of `GET /reservations/{reservationId}/guest-session-id` (get-or-create) | User |
| 2026-10-31 | The owner session is shown in the snapshot as `type: "Owner"` with the Cognito `given_name`, and is not counted toward the guest limit | User |
| 2026-10-31 | Submit adds the consolidated dishes to the existing order items, merging by `dishId`; the response is the consolidated list from the sessions | User |
| 2026-10-31 | Unavailable dishes follow US12: listed but excluded from totals; skipped by submit; 400 if nothing remains | User |
| 2026-10-31 | Snapshot items use the full `DishResponse`; top-level keys are camelCase | User |
| 2026-10-31 | Extra guards: group status (`Submitted`/`Closed`) checks for submit, delete and owner-session creation; reservation status (`Reserved`) checks for submit and owner-session creation; body `reservationId` must match the group; the owner session cannot be deleted; missing dishes are skipped instead of returning 500 | User |
| 2026-10-31 | Spec review: freezing the snapshot (US12 AC10) is out of scope and recorded under Discrepancies; save method names corrected in "Data and DynamoDB changes" | User (review comments 1 and 3) |
| 2026-10-31 | Spec review: `DELETE .../sessions/{guestSessionId}` also returns 400 `GroupReservationExpired` when the reservation is not `Reserved` (US12 AC11); contract, AC15 and S20 updated | User |
| 2026-10-31 | Spec approved | User |
| 2026-10-31 | Rename the gateway resource `/guest/group/{groupReservationId}/sessions` to `/guest/groups/{groupReservationId}/sessions` | User |
| 2026-10-31 | Unit tests at service and handler level | User |
| 2026-10-31 | Spec path `docs/specs/2026-10-31-group-preorder-owner-endpoints.md` | User |
| 2026-10-31 | `/simplify` run on the staged backend changes: review by 4 parallel read-only `code-reviewer` instances (Reuse, Simplification, Efficiency, Altitude); agreed fixes go to `backend-developer` as review feedback | User |
| 2026-10-31 | Simplify fixes agreed (behaviour-preserving, within scope): (A) one shared `EnsureGroupIsActive` guard in `Services/GroupReservations/`; (D) one shared reservation-owner check; (E) drop the redundant `dishIds.Length > 0` guard and manual loops around `GetDishesByIdsAsync`; (F) owner-first ordering via `OrderByDescending`; (B) owner exclusion moved into the repository (`CountJoinedGuestsByGroupReservationIdAsync`, replaces `CountByGroupReservationId`), per "Affected Lambdas and files"; (C) the four handlers use `UserContextHelper.GetAuthenticatedUserId` unchanged; (G1) dishes and owner `given_name` read in parallel in the snapshot | User |
| 2026-10-31 | Simplify findings not taken: parallelising the other independent reads (extra reads on 404/403 paths); a shared exception-to-status mapper for handlers; reusing the new snapshot logic in `GetGuestSessionInfoAsync` (behaviour change, out of scope); `[Required]` on the submit DTO (changes the error contract); changing `UserContextHelper` (out of scope) | User |
| 2026-10-31 | Review notes analysed: submit merge into `Canceled`/`Confirmed` order lines kept as specified, recorded under Risks and Open question 6 | User |
| 2026-10-31 | Cognito failure while reading the owner's `given_name` keeps returning 500; recorded under Risks and Open question 7 | User |
| 2026-10-31 | Scope extended: `docs/architecture.md` section 12, question 6 also names `GET /guest/groups/{groupReservationId}/sessions` as an owner-only `/guest/` endpoint needing a Cognito token. Status set back to Draft for re-approval | User |
| 2026-10-31 | The note "`EnsureGroupIsActive` duplicated in two services" is resolved by simplify fix A (`GroupPreOrderGuards`) | Orchestrator (check) |
| 2026-10-31 | Updated spec (section 12 question 6 in scope, new Risks and Open questions 6–7) re-approved | User |
| 2026-10-31 | `/simplify` on the staged unit tests: review by the Orchestrator (read-only), agreed fixes go to `unit-test-developer` as review feedback | User |
| 2026-10-31 | Test simplify fix agreed: remove the unused session-list setup from the AC9 (S12) owner-exclusion service tests (production reads only `CountJoinedGuestsByGroupReservationIdAsync`); note that owner exclusion itself is covered by `GuestSessionRepositoryTests` | User |
| 2026-10-31 | Test simplify findings not taken: shared `GroupPreOrderTestData.cs` builders for the two service test classes; one `[Theory]` per handler test class for exception mapping; `[MemberData]` for the 401 cases | User |
| 2026-10-31 | The shared test helper `Tests/Handlers/HandlerTestHelpers.cs` (request builders and response checks used by the four handler test classes; not named in "Affected Lambdas and files") is accepted as is | User |
| 2026-10-31 | Handler check order (path parameter 400 before `sub` 401) kept as is and recorded under Risks only; no code or test change | User |
| 2026-10-31 | The AC9 test cleanup by `unit-test-developer` is approved; build and tests to be run | User |
| 2026-10-31 | After the AC9 test cleanup: `dotnet build` 0 errors (no warnings in this task's test files); `dotnet test Tests/APIHandler.Tests.csproj` 289/289 passed; `GuestSessionServiceTests` 90/90, `GroupReservationServiceTests` 39/39 | Orchestrator (run) |
| 2026-11-01 | Step 6: the user ran the `code-review` skill on the backend changes. It reported 10 findings. The Orchestrator checked them against the code: none deviates from this spec; findings 1–8 confirmed, 9 confirmed only for a reservation whose guest count was lowered to 1 after the group was created, 10 confirmed as a pre-existing pattern, with its "same GSI query runs more than once" and read-capacity claims not confirmed | User (run), Orchestrator (check) |
| 2026-11-01 | Fix `code-review` findings 1–3 (the high ones): atomic submit (AC18), the 30-minute cutoff for the submit (AC19), a deterministic owner session id with a conditional create (AC20). Findings 4–10 recorded under Risks and Open questions 6–9, not fixed | User |
| 2026-11-01 | The submit after the cutoff returns 400 `GroupReservationExpired` | User |
| 2026-11-01 | The owner session id is `owner-{groupReservationId}`; no fallback for owner sessions with random ids, because no deployed version created them | User |
| 2026-11-01 | After the fixes, re-review by the `code-reviewer` agent of findings 1–3 and the fix diff only | User |
| 2026-11-01 | Scope extended (findings 1–3 in scope, AC18–AC20, S9a, S9b, S22–S25). Status set back to Draft for re-approval | Orchestrator (per the user's decision) |
| 2026-11-01 | Updated spec (findings 1–3, AC18–AC20) re-approved | User |
| 2026-11-01 | `/simplify` on the staged fixes for findings 1–3: review by the Orchestrator (read-only, no agents), agreed fixes go to `backend-developer` as review feedback | User |
| 2026-11-01 | Simplify fixes agreed (behaviour-preserving, within scope): (1) one shared `IDynamoDBContext` conversion helper (`ToDocument` with `IgnoreNullValues` → `ToAttributeMap`) used by `GuestSessionRepository.TryCreateAsync` and `GroupReservationRepository`; (2) a group-status-only guard in `GroupPreOrderGuards` (`Submitted` → `GroupReservationSubmittedException`, `Closed` → `GroupReservationExpiredException`) reused by `EnsureGroupIsActive` and `GroupReservationService.ThrowGroupNoLongerOpenAsync`; (3) the `OrderService.EnsurePreOrderEditable` message uses `PreOrderCutoffHelper.MinutesBeforeReservationStart` (text unchanged); (4) `GuestSessionService.CreateGroupOwnerSessionAsync` drops the derivable `ownerSessionId` parameter | User |
| 2026-11-01 | Simplify findings not taken: an explicit converter for `GroupReservation.Status` (changes the stored format; the spec requires the context's default conversion); moving the submit transaction to `OrderRepository` (developer's choice per the spec); merging `GetByGuestSessionIdConsistentAsync` into `GetByGuestSessionId` (interface and behaviour change for existing callers) | Orchestrator (proposed skipping; shown to the user, not offered for selection) |
| 2026-11-01 | Simplify fixes (1)–(4) applied by `backend-developer` (new `Helpers/DynamoDbContextExtensions.cs`); the user moved on to unit tests without further comments. Unit tests for AC18–AC20 (S8, S9, S9a, S9b, S14, S16, S17, S22–S25) and updates of existing tests broken by the AC18–AC20 changes go to `unit-test-developer` | User |
| 2026-11-01 | Unit tests for AC18–AC20 approved (new `Tests/Repositories/GroupReservationRepositoryTests.cs`; the test `TwoOwnerSessionsAfterConcurrentCreation_ReturnsFirstFoundWithoutCreating` removed as superseded by AC20). Not covered: the exact cutoff boundary (needs a clock seam in production code, out of scope) and the propagation of other errors from `TryCreateAsync` (not defined by the spec). Build and tests to be run | User |
| 2026-11-01 | First step 5 build failed: CS0104 (`Match` ambiguous between `Moq` and `System.Text.RegularExpressions`) in `GroupReservationRepositoryTests.cs` lines 156 and 162; fix agreed: a fully qualified type name (applied by `unit-test-developer` as the alias `RegexMatch`) | User |
| 2026-11-01 | Step 5 after the fix: `dotnet build` succeeded, 0 errors, no warnings in this task's files; `dotnet test Tests/APIHandler.Tests.csproj` 306/306 passed; `GroupReservationRepositoryTests` 5/5, `GuestSessionRepositoryTests` 9/9, `GroupReservationServiceTests` 48/48, `GuestSessionServiceTests` 90/90. Read-only checks: no route or Lambda configuration changes in this round; `ordersTable`, `groupReservationsTable`, `guestSessionTable` are in `env_variables`; the IAM policy grants `PutItem`, `UpdateItem` and `GetItem` (enough for the transaction, the conditional put and the consistent read). The user committed the work as `befea2f` during step 5; the alias fix is uncommitted | Orchestrator (run) |
| 2026-11-01 | The user committed the alias fix as `a7eba20`; working tree clean. Step 6: re-review by `code-reviewer` of `code-review` findings 1–3 and the fix diff `9299593..HEAD` | User |
| 2026-11-01 | Re-review result: findings 1–3 `Addressed` (confirmed by the Orchestrator against the code); no Blocker or Major; two Minor findings, both confirmed. The reviewer's claim that `TransactionConflict` → 500 is already under Risks was not confirmed (it was not) | Orchestrator (check) |
| 2026-11-01 | Minor 1: the submit contract now states that a group not found on the re-read after a failed condition propagates as 500 (code and the existing test already do this; no code or test change) | User |
| 2026-11-01 | Minor 2: `TransactionConflict` on a concurrent submit → 500 recorded under Risks; `backend-developer` updates the concurrent-submit wording in `docs/ApiRoutes.md` (no code change) | User |
| 2026-11-01 | Spec edits for Minor 1 (contract, submit "On success") and Minor 2 (Risks) approved; status stays `Approved (2026-11-01)` | User |
| 2026-11-01 | Minor 2 doc fix applied by `backend-developer`: `docs/ApiRoutes.md` line 1231 (submit errors) now says a truly concurrent submit may get 500 on a transaction conflict, nothing is written, and a retry gets `GroupReservationSubmitted`; checked by the Orchestrator against `GroupReservationRepository.IsConditionalCheckFailed` and the global 500 mapping. Documentation only; build and tests not rerun | Orchestrator (check) |
| 2026-11-01 | The user committed the doc and spec changes as `8f99dea` and asked for the deploy ("роби деплой"). Account `131470772794` and region `eu-north-1` checked against the Syndicate config. `syndicate build` (bundle `restaurant-backend-app_261101.131457`) and `syndicate update -d dev` succeeded (`swagger-ui` skipped by Syndicate as not updatable). Checked after the update: the three Lambdas updated, `reports-handler` handler `ReportsHandler::ReportsHandler.Function::FunctionHandler`; `GET /reservations/{reservationId}/guest-session-id` and `POST /group-reservations/{groupReservationId}/submit` exist with `COGNITO_USER_POOLS` and return 401 without a token; the old path `/guest/group/{groupReservationId}/sessions` is gone; stage `develop` redeployed. Manual tests with real data not run yet | User (deploy), Orchestrator (run and check) |

## Status

Approved (2026-11-01)
