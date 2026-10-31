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

### Out of scope

- Frontend changes, including how the SPA sends a Cognito token to `/guest/...` owner endpoints (the interceptor skips `/guest/` URLs; `docs/architecture.md` section 12, question 6).
- Letting the owner add or remove dishes in, or submit, their own owner session (the owner session has no guest token; see Open questions).
- Freezing the snapshot at submit or closure (US12 AC10): the snapshot is always built from the current `Dishes` data (see Discrepancies).
- Real-time updates (US12 AC8, AC14–AC18), payment mode (US12 AC19–AC21), automatic closure at the deadline (US14 AC3), waiter view (US16).
- The 30-minute pre-order edit cutoff (`OrderService.EnsurePreOrderEditable`) for the group submit (see Open questions).
- DynamoDB transactions for the submit (see Risks).
- `swagger_src/` (generated Swagger artifacts), `docs/Diagrams.md`, `reports-handler`, `report-sender`, `infra/terraform`.
- Fixing other known gaps in `docs/architecture.md` section 12 (for example, the missing ownership check in `GET /reservations/{reservationId}/invite-token`).

## Affected Lambdas and files

Only `api-handler`. Expected files (the developer may add files under the same folders as needed; projects are SDK-style):

- `api-handler/Handlers/GuestSessions/`: new `GetGroupSessionsHandler` (name indicative), changed `DeleteGuestSessionHandler.cs`.
- `api-handler/Handlers/GroupReservations/` (or `GuestSessions/`): new handlers for `GET .../guest-session-id` and `POST .../submit`.
- `api-handler/Services/GuestSessions/GuestSessionService.cs`, `IGuestSessionService.cs`: snapshot builder, get sessions, delete changes, get-or-create owner session, owner exclusion from counts.
- `api-handler/Services/GroupReservations/GroupReservationService.cs`, `IGroupReservationService.cs` (or another service in `Services/`): group submit; owner exclusion from the guest count in `GetGroupReservationByTokenAsync`. New constructor dependencies (for example `IOrderRepository`, `IDishRepository`) are allowed.
- `api-handler/Repositories/GuestSessions/` (only if a repository change is the chosen way to exclude owner sessions from counts).
- `api-handler/Models/GuestSessions/` and/or `Models/GroupReservation/`: response DTO for the owner session id, request DTO for submit, response item DTO for submit. `Models/GuestSessions/GetAllResponse.cs` may change if needed for camelCase.
- `api-handler/Routing/RouteRegistry.cs`: three new routes; DI changes if a service gets new dependencies.
- Top-level `deployment_resources.json`: rename one resource, add two resources.
- `docs/ApiRoutes.md`.
- Root `docs/architecture.md`, only these parts:
  - section 4.4: the endpoint status table, and the paragraph "The owner has no session yet" (owner sessions are now created by `GET /reservations/{reservationId}/guest-session-id`);
  - section 7.1: the route and method counts, the sentence about the singular `/guest/group/...` gateway path, and the sentence listing planned group pre-order endpoints that exist in neither the gateway nor `RouteRegistry`;
  - sections 5.3 and 7.2: the gateway method and resource counts that this change alters;
  - section 12: questions 7 and 8, marked as resolved with a link to this spec.
- Tests (owned by `unit-test-developer`): `Tests/Services/GuestSessionServiceTests.cs`, `Tests/Services/GroupReservationServiceTests.cs` (or the test class of whichever service hosts submit), new `Tests/Handlers/<HandlerName>Tests.cs` for each new or changed handler, and any existing test broken by constructor or signature changes.

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
- If the group already has an owner session (a session with `IsOwnerSession == true`), return its id, whatever the group or reservation status.
- Otherwise, if the group status is `Open` and the reservation status is `Reserved`, create an owner session (`IsOwnerSession = true`, `OwnerUserId = sub`, `GroupReservationId` = the group, `Dishes = []`, no token hash) and return its id.
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
  6. no session of the group with `IsSubmitted == true` → 400;
  7. consolidate: take the cart lines of submitted sessions only (guest and owner alike), drop lines whose dish no longer exists in `Dishes` or whose state is not `Available`, merge lines with the same `dishId` by summing `quantity`. Result empty → 400;
  8. active order for the reservation not found (`IOrderRepository.GetOrderByReservationIdAsync` returns null) → 400.
- On success:
  - add the consolidated lines to `order.Dishes`: for a `dishId` already in the order, add the quantity to the existing line (keep its `State`); otherwise append a new `OrderItem` with `State = Pending`;
  - set `order.Status = SubmittedByCustomer` and save the order;
  - set the group reservation status to `Submitted` and save it.
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

- No new tables, GSIs or attributes. IAM is unchanged (the shared `lambda-basic-execution` policy covers DynamoDB and Cognito).
- `GuestSessions`: owner sessions are now written (existing attributes `isOwnerSession`, `ownerUserId`); the owner session is found by querying the `groupReservationId` GSI and filtering on `IsOwnerSession`.
- Owner sessions are excluded from:
  - the guest-limit check in `GuestSessionService.CreateGuestSessionAsync`;
  - the guest-limit check in `GroupReservationService.GetGroupReservationByTokenAsync`;
  - the participant count in `GuestSessionService.GetGroupReservationStatusAsync` (which already adds 1 for the owner).
- `Orders`: the active order is read through the existing `GSI_ReservationId` path (`IOrderRepository.GetOrderByReservationIdAsync`) and saved with `IOrderRepository.SaveOrderAsync`.
- `GroupReservations`: status set to `Submitted` and saved with `IGroupReservationRepository.SaveGroupReservationAsync`.
- The submit writes two items without a transaction or conditional write (see Risks).

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
- **AC14.** `POST .../submit` success: only submitted sessions are used; unavailable and missing dishes are dropped; lines are merged by `dishId`; they are added to the order (quantities summed into existing lines, new lines `Pending`); the order is saved with `SubmittedByCustomer`; the group reservation is saved with `Submitted`; 200 with the consolidated `[{ "dishId", "quantity" }]` list in camelCase.
- **AC15.** `DELETE .../sessions/{guestSessionId}`: missing `sub` → 401; unknown group → 404; reservation missing → 404; caller not the owner → 403; group `Submitted` or `Closed`, or reservation not `Reserved` → 400 with the error code from the contract; unknown session → 404; session of another group → 400; owner session → 400. On every error, nothing is deleted.
- **AC16.** `DELETE .../sessions/{guestSessionId}` success: the session is deleted and 200 returns the snapshot of the remaining sessions in the same format and with the same rules as AC3–AC6.
- **AC17.** `docs/ApiRoutes.md` describes the four endpoints as in "API contract changes" (request, response, error statuses), and `docs/architecture.md` is updated in the parts listed under "Affected Lambdas and files".

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

- S8 (AC8): owner session exists → its id returned, `SaveAsync` never called, even if the group is `Submitted`.
- S9 (AC8): no owner session (guest sessions only), group `Open`, reservation `Reserved` → one session saved with `IsOwnerSession = true`, `OwnerUserId` = caller, correct `GroupReservationId`; returned id equals the saved id.
- S10 (AC8): no owner session and group `Submitted` → 400 `GroupReservationSubmitted`; `Closed` → `GroupReservationExpired`; reservation `Cancelled` → `GroupReservationExpired`; `SaveAsync` never called.
- S11 (AC7): unknown reservation → 404; non-owner → 403; no group → 404; handler without `sub` → 401; response body `{ "guestSessionId": ... }`.

Owner exclusion:

- S12 (AC9): with N guest sessions plus one owner session, `CreateGuestSessionAsync` and `GetGroupReservationByTokenAsync` apply the limit as for N guests (a guest who fits by the guest count is admitted; the limit is still enforced at the boundary); `GetGroupReservationStatusAsync` reports N + 1 participants, not N + 2.

`POST .../submit`:

- S13 (AC14): two submitted sessions sharing `dish1` and one unsubmitted session → response contains `dish1` with summed quantity and the other dishes; the unsubmitted session's dishes are absent.
- S14 (AC14): order already has `dish1` (quantity 1, state `Confirmed`) and `dish9` → after submit `dish1` quantity increased and state kept, `dish9` unchanged, new dishes appended as `Pending`; order saved with `SubmittedByCustomer`; group saved with `Submitted`.
- S15 (AC14): a submitted session with an `OnStop` dish and a dish missing from `Dishes` → both dropped from the response and the order.
- S16 (AC13): no submitted sessions → 400; submitted sessions with empty carts → 400; submitted sessions whose dishes are all unavailable → 400; no active order → 400; in each case `SaveOrderAsync` and `SaveGroupReservationAsync` are never called.
- S17 (AC11, AC12): unknown group → 404; body `reservationId` mismatch → 400; missing reservation → 404; non-owner → 403; group `Submitted` / `Closed`, reservation not `Reserved` → 400 with the right error codes; nothing saved.
- S18 (AC10, handler): no `sub` → 401; no body, invalid JSON, empty `reservationId` → 400; success body is a camelCase array of `dishId`/`quantity`.

`DELETE .../sessions/{guestSessionId}`:

- S19 (AC16): deleting a guest session calls `DeleteAsync` with that session and returns the snapshot of the remaining sessions.
- S20 (AC15): unknown group → 404; missing reservation → 404; non-owner → 403; group `Submitted`/`Closed` → 400; group `Open` but reservation `Cancelled` (or another non-`Reserved` status) → 400 `GroupReservationExpired`; unknown session → 404; session of another group → 400; owner session → 400; `DeleteAsync` never called in any of these.
- S21 (AC15, handler): no `sub` → 401; exception-to-status mapping (404 for the three not-found exceptions, 403, 400 with `errorCode`).

## Risks

- **Submit is not atomic.** The order and the group reservation are saved by two separate writes. If the second write fails, a retry can add the dishes to the order a second time (the group is still `Open`). Two concurrent submits can both pass the status check. A DynamoDB transaction with a condition on the group status would close this; it is out of scope.
- **Concurrent owner-session creation.** Two simultaneous first calls to `GET .../guest-session-id` can create two owner sessions. The snapshot and the lookup must still behave (the lookup takes the first owner session found).
- **The owner session cannot be used yet.** It has no guest token, and guest endpoints authorize by token, so the owner cannot add dishes to it or submit it; its cart stays empty unless this is addressed later.
- **Frontend cannot call the owner `/guest/...` endpoints** with a Cognito token today, because the interceptor skips `/guest/` URLs (`docs/architecture.md` section 12, question 6).
- **Response format change of the existing DELETE**: its keys change from PascalCase to camelCase. Nothing in the frontend calls it today (checked: no reference in `frontend/restaurant-frontend-app/src`).
- **Renaming the gateway resource** removes `/guest/group/{groupReservationId}/sessions` on the next deployment; it never had a Lambda route.
- **Exception hierarchy:** not-found exceptions derive from `ValidationException`; catching `ValidationException` first would turn 404s into 400s.

## Open questions

1. Should the group submit respect the 30-minute pre-order edit cutoff before the reservation start, like `OrderService` does for individual pre-orders? Not applied in this spec.
2. How should the owner add dishes to and submit their owner session (a token for the owner session, or Cognito-authorized variants of the cart endpoints)? Out of scope.
3. Should the submit be made atomic with a DynamoDB transaction (see Risks)?
4. "Unavailable" in this spec means any dish state other than `Available` (`On stop` and `Out of stock`); US12 AC9 names only "On Stop". Confirm that `Out of stock` is treated the same way.
5. US14 AC2 says the closed order gets the status "Closed", while the homework sets the group reservation to `Submitted`; this spec follows the homework. How `Submitted` and `Closed` relate in the product (see Discrepancies)?

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

## Status

Approved (2026-10-31)
