# Green Tasty: Group Pre-order. User stories

All user stories of the Group Pre-order feature. Product context, roles and glossary: [project-overview.md](project-overview.md).

All stories belong to the **Order Management** epic. The order in this file follows the user journey: invitation, guest joining, dish selection, owner's consolidated view, closing, guest editing, waiter view, payment.

| US | Name | Role | Priority |
| :- | :- | :- | :- |
| [US1](#us1) | Initiation of Group Pre-order Session | Customer (Owner) | Do first |
| [US10](#us10) | Guest Joining a Group Pre-order Session via Link | Guest (Visitor, unregistered) | Do first |
| [US11](#us11) | Guest Reservation and Menu View | Guest (Visitor, unregistered) | Do first |
| [US12](#us12) | Owner Reviewing Group Pre-order | Customer (Owner) | Do first |
| [US13](#us13) | Owner Removing a Guest from Group Pre-order | Customer (Owner) | Good fit |
| [US14](#us14) | Owner Submitting Group Pre-order to the Restaurant | Customer (Owner) | Do first |
| [US15](#us15) | Guest Editing Own Pre-order | Guest (Visitor, unregistered) | Do first |
| [US16](#us16) | Waiter Viewing Table Group Pre-order | Waiter | Do first |
| [US17](#us17) | Guest Paying Own Share (Split the bill) | Guest (Visitor, unregistered) | Do first |

Cross-references between stories are written as `(USxx)`. US10 and US11 use legacy references: `US_Owner_1` means US1, `US_G10` means US10.


> US12–US15 are aligned with the Figma mock-up (FOR DEVELOPERS → Owner Flow), `homework-frontend.md` and `homework-backend.md`. Where they differ from earlier versions of these stories, the mock-up and the homework take priority. In US16–US17, "closed" means the group status "Submitted" (US14).

---

## US1

**Initiation of Group Pre-order Session**

- **Epic:** Order Management
- **Role:** Customer (Owner)
- **Priority:** Do first
- **Description:** As a Reservation Owner, I want to easily initiate a group pre-order session and share a unique link with my companions so that they can select their own dishes and I do not have to manually input orders for everyone.

### Pre-conditions

- The Customer is logged in.
- The Customer has successfully created a table reservation (Reservation status = "Reserved").

### Acceptance Criteria

- **AC1.** Immediately upon successful reservation creation (when Reservation status transitions to "Reserved"), the system must automatically instantiate a unique GroupSessionID mapped 1-to-1 to the ReservationID.
- **AC2.** This session generation must happen in the background and does not require any explicit initiation trigger from the Owner.
- **AC3.** The system must display the group pre-order controls (such as the [ Invite Guests ] button "Reservations", copying options) only if the table reservation’s guests_count is ≥2. The system must place the [ Invite Guests ] button in "Reservations".
- **AC4.** Upon clicking the invite button, the system must generate a cryptographically secure, non-guessable tokenized URL: https://greentasty.app/join/{secure_session_token}.
- **AC5.** Clicking the "Copy" action must automatically copy the tokenized URL to the device’s clipboard and show a notification on-screen: "Link copied! Share it with your friends."
- **AC6.** The sharing link and the invite panel must only be active while the parent Reservation has the status "Reserved".
- **AC7.** If the reservation is modified to a different restaurant location, cancelled (Cancelled), or completed (Completed), the session token must immediately expire. Anyone trying to access the link after expiration must be redirected to a static page: "This pre-order session has expired or is no longer active."

---

## US10

**Guest Joining a Group Pre-order Session via Link**

- **Epic:** Order Management
- **Role:** Guest (Visitor, unregistered)
- **Priority:** Do first
- **Description:** As a Guest without an account, I want to open the shared link and enter my name, so that I can start ordering immediately without registering.

### Pre-conditions

- The Owner has generated and shared a valid, active tokenized link.
- The parent Reservation status is "Reserved" (per US_Owner_1).

### Acceptance Criteria

- **AC1.** Opening the link does not require login or registration.
- **AC2.** The system must validate the token against the parent Reservation status; if the Reservation is not in an active/joinable status, the Guest must be redirected to a static page: "This pre-order session has expired or is no longer active."
- **AC3.** Guest should see details of reservation (who invited, restaurant name, location, date&time, table).
- **AC4.** Guest must enter a display name (mandatory, min. 1 character) before accessing the menu.
- **AC5.** Upon successful name entry, the Guest can click Browse Menu and get redirected to the menu screen.

---

## US11

**Guest Reservation and Menu View**

- **Epic:** Order Management
- **Role:** Guest (Visitor, unregistered)
- **Priority:** Do first
- **Description:** As a Guest, I want to see the full restaurant menu, so that I know what options are available and can decide what to choose.

### Pre-conditions

- Guest has successfully joined the session (US_G10).
- The parent reservation or group session status is in Created/"Reserved".

### Acceptance Criteria

- **AC1.** The Guest can view the full restaurant menu structured logically by category (e.g., Appetizers, Main Course, Desserts).
- **AC2.** Each menu item must clearly display name, price, serving size in grams, image and current availability status.
- **AC3.** The Guest can add an available dish directly to their personal subcart by interacting with a clear action button (e.g., "Pre-order").
- **AC4.** Items currently marked "On Stop" are clearly indicated as unavailable and cannot be selected or added to the cart.
- **AC5.** The menu reflects the current data at the time the Guest opens or refreshes the screen.

---

## US12

**Owner Reviewing Group Pre-order**

- **Epic:** Order Management
- **Role:** Customer (Owner)
- **Priority:** Do first
- **Sources:** Figma EPAM IT Marathon 6.0 → FOR DEVELOPERS → Owner Flow (`homework-design/01-owner-review.png`), `homework-frontend.md` levels 1–3, `homework-backend.md` level 1 (`GET /guest/groups/{groupReservationId}/sessions`).
- **Description:** As the reservation owner, I want to see on one screen what every participant of my group has chosen, whether they have submitted their cart, and the total that will go to the restaurant, so that I can check the group order before sending it.

### Pre-conditions

- The Customer is logged in and is the reservation owner.
- The reservation has the status "Reserved" and guests_count ≥ 2; the group reservation has been created (US1).

### Acceptance Criteria

**AC1. Entry point**

- **Given** the reservation has the status "Reserved" and guests_count ≥ 2
- **When** the owner opens "Reservations"
- **Then** the reservation card shows the [ Group Pre-Order ] button
- **And** the button opens the page `/reservations/{reservationId}/group-order`.
- For guests_count = 1 the button is not shown.

**AC2. Header**

- **Given** the owner has opened the Group Pre-Order page
- **Then** they see the title "Group Pre-Order" with the group status badge (US14 AC5), the restaurant address, date, time range and "N of M guests" with icons
- **And** N is the number of participants including the owner, M is the reservation's guest count.

**AC3. Participant cards**

- **Given** participants have joined the group via the link (US10)
- **When** the owner opens the page
- **Then** they see one card per participant
- **And** the owner's own card is titled "My Pre-Order", every other card "Guest: {name}" with the name the guest entered when joining.

**AC4. Dish rows and subtotal**

- **Given** a participant has dishes in their cart
- **Then** each dish row shows the dish photo (or the first letter of the dish name on a green circle), the name, the quantity as text ("× 2") and the line price = price × quantity
- **And** the card shows "Subtotal" = sum of the line prices.
- Editing controls from the mock-up (−/+, trash icon, "Edit my Pre-Order") are not part of this story: the owner can't change other participants' carts.

**AC5. Cart status**

- **Given** the page is open
- **Then** every card shows the cart status as text: "Submitted" if the participant has submitted their cart (US15 AC8), otherwise "Still choosing"
- **And** the status is not conveyed by colour only.

**AC6. Empty cart**

- **Given** a participant has joined but has no dishes
- **Then** their card shows "No dishes yet" and "Subtotal $0.00".

**AC7. Not-submitted participants warning**

- **Given** K ≥ 1 participants haven't submitted their cart
- **Then** a yellow banner shows "K participants haven't submitted their cart yet — their dishes won't be included in the order" ("1 participant hasn't submitted…" for K = 1).

**AC8. Consolidated pre-order**

- **Given** the page is open
- **Then** the "Consolidated pre-order" block lists the dishes of submitted carts only, with the same dishes merged by dish id: "{name} × {total quantity}" and the line total
- **And** if nobody has added dishes, it shows "Participants haven't added any dishes yet"
- **And** if dishes exist only in not-submitted carts, it shows "No submitted carts yet".

**AC9. Grand Total**

- **Given** the page is open
- **Then** "Grand Total" (green) equals the sum of the consolidated pre-order, i.e. submitted carts only
- **And** it is not the `total` field of the API, which counts all carts. Example: submitted carts $540, all carts $760 → Grand Total $540.

**AC10. Loading, error and not found states**

- While data is loading, the page shows a loading state.
- If loading fails, the page shows "Couldn't load the group pre-order" and a [ Try again ] button; Try again does not start a second background timer.
- If the reservation or the group reservation doesn't exist (404), the page shows "Group reservation not found".

**AC11. Background sync**

- **Given** the group status is "Open"
- **Then** the page refreshes the participants and the status every 15 seconds without a full-page loader
- **And** shows "Updated at HH:mm:ss" and a [ Refresh ] button that syncs immediately
- **And** if a background refresh fails, the page keeps the last data and shows "Couldn't refresh. Showing data from HH:mm:ss" without an error toast
- **And** a new request doesn't start while the previous one is still running
- **And** the sync stops after the pre-order is submitted (US14) and when the owner leaves the page.

**AC12. Access**

- Only the reservation owner can see the page and call `GET /guest/groups/{groupReservationId}/sessions`.
- A user who is not logged in is redirected to sign in.

**AC13. Layout**

- The page doesn't break at 375 px width.
- Semantics: one `<h1>`, participants as a list, each card as an `<article>`.

---

## US13

**Owner Removing a Guest from Group Pre-order**

- **Epic:** Order Management
- **Role:** Customer (Owner)
- **Priority:** Good fit (level 3 bonus)
- **Sources:** Figma `homework-design/03-remove-guest.png`, `homework-frontend.md` level 3 bonus, `homework-backend.md` level 3 (`DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}`).
- **Description:** As the reservation owner, I want to remove a participant and their pre-order from my group, so that dishes of someone who isn't coming don't get into the table order.

### Pre-conditions

- The owner has the Group Pre-Order page open (US12).
- The group status is "Open".

### Acceptance Criteria

**AC1. Remove action**

- Every "Guest: {name}" card has a [ Remove Guest ] link with the accessible name "Remove {name}".
- The owner's "My Pre-Order" card has no Remove Guest link.
- After the pre-order is submitted (US14), Remove Guest is not shown.

**AC2. Confirmation**

- **When** the owner clicks [ Remove Guest ]
- **Then** a "Remove Guest" dialog opens with the text "Are you sure you want to remove **{name}** and their pre-order from this reservation? This action cannot be undone." and the buttons [ Cancel ] and [ Remove Guest ].

**AC3. Cancel**

- **When** the owner clicks [ Cancel ] or closes the dialog
- **Then** nothing changes.

**AC4. Successful removal**

- **When** the owner confirms
- **Then** the guest session is deleted
- **And** the participant list, "N of M guests", the warning banner, the consolidated pre-order and the Grand Total are updated from the snapshot returned by the API (participants and total).

**AC5. Errors**

- Only the owner can delete a guest session.
- If the guest session doesn't belong to this group reservation, the API returns 400; if it doesn't exist, 404.
- On an error the data on the screen doesn't change, the error is shown once as a toast, and the owner can try again.

---

## US14

**Owner Submitting Group Pre-order to the Restaurant**

- **Epic:** Order Management
- **Role:** Customer (Owner)
- **Priority:** Do first
- **Sources:** Figma `homework-design/01-owner-review.png`, `02-sent-to-restaurant.png`, `homework-frontend.md` level 2, `homework-backend.md` level 2 (`GET /reservations/{reservationId}/guest-session-id`, `POST /group-reservations/{groupReservationId}/submit`).
- **Description:** As the reservation owner, I want to send the consolidated pre-order to the restaurant once the participants have submitted their carts, so that the restaurant receives one final order for the table.

### Pre-conditions

- The owner has the Group Pre-Order page open (US12).
- The group status is "Open".

### Acceptance Criteria

**AC1. Submit button**

- **Given** the group status is "Open"
- **Then** below the Grand Total there is a wide green [ Submit Pre-Order ] button.

**AC2. Disabled button**

- The button is disabled (grey) when the submitted carts contain no dishes, and while the submit request is running.

**AC3. Confirmation**

- **When** the owner clicks [ Submit Pre-Order ]
- **Then** a "Submit Pre-Order" dialog opens with the text "Send the consolidated pre-order of {Grand Total} to the restaurant? Carts that are not submitted won't be included." and the buttons [ Cancel ] and [ Submit ]
- **And** [ Cancel ] closes the dialog without changes.

**AC4. Submitting**

- **When** the owner clicks [ Submit ]
- **Then** the system takes only submitted guest sessions, merges the same dishes by dish id, adds them to the reservation's order, sets the order status to "SubmittedByCustomer" and the group status to "Submitted"
- **And** a double click sends the request only once.

**AC5. Status badge**

- Group status "Open" is shown as the badge "Collecting pre-orders", "Submitted" as "Sent to Restaurant".

**AC6. After submitting**

- **Then** the owner sees the toast "Pre-order sent to the restaurant" and the badge "Sent to Restaurant"
- **And** the page becomes read-only: no Remove Guest, no Refresh, the background sync stops
- **And** the [ Submit Pre-Order ] button stays visible but disabled (grey), as in the mock-up
- **And** when the owner opens the page again, they see the same read-only state.

**AC7. Errors**

- Only the owner can submit.
- Reservation or group reservation not found → 404.
- No submitted guest sessions → 400; submitted sessions contain no dishes → 400; the reservation's order is not found → 400.
- The API error message is shown once as a toast; the page doesn't break and the owner can press Submit again.

### Out of scope for this release

- Automatic closing at a default deadline, the "Closed" / "Pre-order closed" state, the payment mode toggle (US17) and dishes "On Stop" on the owner's page are not in the mock-up or the homework and are moved to later releases.
- Statuses from the Figma page that are not in the homework ("New pre-order added", "Confirmed" with dishes rejected by the waiter) belong to later stories (US16).

---

## US15

**Guest Editing Own Pre-order**

- **Epic:** Order Management
- **Role:** Guest (Visitor, unregistered)
- **Priority:** Do first
- **Description:** As a guest, I want to view and change my pre-order until the owner submits the group pre-order to the restaurant (US14), so that the table gets exactly what I want and the waiter doesn't have to fix the order on site.

### Pre-conditions

- The guest has joined the group session (US10).
- The reservation has the status "Reserved".
- The group pre-order hasn't been submitted to the restaurant yet (group status "Open").

### Acceptance Criteria

**AC1**

- **Given** the guest has added dishes to their personal subcart (US11)
- **When** they open their personal subcart
- **Then** they see each dish with its name and price and the personal subcart total.

**AC2**

- **Given** the personal subcart contains a dish
- **When** the guest removes it
- **Then** the dish disappears from the personal subcart and the personal subcart total is recalculated.

**AC3**

- **Given** the guest is viewing their personal subcart
- **When** they go back to the menu and add an available dish
- **Then** the dish appears in their personal subcart and the total is recalculated.

**AC4**

- **Given** a dish in the personal subcart got the status "On Stop" after it was added
- **When** the guest opens the personal subcart
- **Then** the dish is marked as unavailable and is not included in the total
- **And** the guest can remove it.

**AC5**

- **Given** the guest previously joined the session and closed the page
- **When** they open the link again from the same device and browser
- **Then** they see their personal subcart and can change it without entering their name again.

**AC6**

- **Given** the owner has submitted the group pre-order to the restaurant (group status "Submitted", US14)
- **When** the guest tries to change the personal subcart
- **Then** changes are not available and the guest sees the message "Pre-order is closed, changes are no longer possible".

**AC7**

- **Given** the guest opened the personal subcart while the order was open
- **And** the owner submitted the group pre-order after that
- **When** the guest adds or removes a dish
- **Then** the change is not saved
- **And** the guest sees the message "Pre-order is closed, changes are no longer possible" and their personal subcart as it was at submission.

**AC8**

- **Given** the guest's personal subcart contains at least one dish
- **When** the guest clicks [ Submit Pre-Order ] in the cart
- **Then** the cart gets the status "Submitted" and the owner sees "Submitted" on the guest's card (US12 AC5)
- **And** only submitted carts are included in the order sent to the restaurant (US14 AC4).

---

## US16

**Waiter Viewing Table Group Pre-order**

- **Epic:** Order Management
- **Role:** Waiter
- **Priority:** Do first
- **Description:** As a waiter, I want to see one group pre-order for the table broken down by guest, so that I can send the full order to the kitchen and serve each guest their own dishes without collecting the order on site.

### Pre-conditions

- The waiter is logged in with the waiter role for their restaurant.
- A group pre-order has been created for a reservation in this restaurant.

### Acceptance Criteria

**AC1**

- **Given** a group pre-order has been created for the reservation
- **When** the waiter opens the orders for the reservation date
- **Then** they see one group pre-order for the table with the date and time, table number and the reservation owner's name.

**AC2**

- **Given** the waiter has opened the group pre-order
- **When** they view it
- **Then** they see each guest under their name, each guest's dishes with name and price, each guest's total and the table total.

**AC3**

- **Given** the group order is not closed yet
- **When** the waiter opens it
- **Then** they see the label "Not final: guests can still change their selection".

**AC4**

- **Given** the group order is closed (US14)
- **When** the waiter opens it
- **Then** they see the label "Final" and items that no longer change.

**AC5**

- **Given** the group order is not closed yet
- **And** a dish in a guest's order has the status "On Stop"
- **When** the waiter views the order
- **Then** the dish is marked as unavailable and is not included in totals.

**AC6**

- **Given** the waiter works in one restaurant
- **When** they try to open the group pre-order of a reservation in another restaurant
- **Then** they have no access.

**AC7**

- **Given** the reservation is cancelled
- **When** the waiter opens the orders for the date
- **Then** this reservation's group pre-order is not among the active ones.

**AC8**

- **Given** the waiter is viewing the group pre-order
- **When** they look for actions to change guests' dishes
- **Then** there are no such actions: the waiter can't change guests' dishes.

**AC9**

- **Given** the owner selected "Split the bill"
- **When** the waiter opens the group pre-order
- **Then** next to each guest they see the selected payment method: "Online", "At the restaurant" or "Not selected".

**AC10**

- **Given** the owner selected "Split the bill"
- **When** the waiter views the order
- **Then** the dishes of guests who paid online (US17) are marked as "Paid"
- **And** the dishes of the other guests are marked as "Unpaid".

**AC11**

- **Given** the owner kept the "Single payment" mode
- **When** the waiter opens the group pre-order
- **Then** they see one amount due for the table, without a per-guest payment breakdown.

**AC12**

- **Given** the owner selected "Split the bill", the group order is closed, and a guest hasn't paid online
- **When** the guest paid at the restaurant and the waiter clicks "Mark as paid" for this guest
- **Then** the guest's dishes are marked as "Paid"
- **And** the guest sees the status "Paid" (US17).

**AC13**

- **Given** the guest has already paid online
- **When** the waiter views their dishes
- **Then** there is no "Mark as paid" action for this guest.

---

## US17

**Guest Paying Own Share (Split the bill)**

- **Epic:** Order Management
- **Role:** Guest (Visitor, unregistered)
- **Priority:** Do first
- **Description:** As a guest, I want to pay for my own dishes online or at the restaurant, so that I don't have to settle up with the group owner for my order.

### Pre-conditions

- The guest has joined the group session (US10).
- The owner selected the "Split the bill" payment mode (US12).

### Acceptance Criteria

**AC1**

- **Given** the owner selected "Split the bill" and the group order is not closed yet
- **When** the guest opens their order
- **Then** they see their current total and a message that payment will become available after the order is closed.

**AC2**

- **Given** the owner selected "Split the bill" and the group order is closed (US14)
- **When** the guest opens their order
- **Then** they see the amount due for their dishes and two payment methods: "Pay online" and "Pay at the restaurant".

**AC3**

- **Given** the guest sees the amount due
- **When** they select "Pay online" and complete the payment successfully
- **Then** they see the status "Paid"
- **And** the waiter sees their dishes as paid (US16).

**AC4**

- **Given** the guest selected "Pay online"
- **When** the payment fails
- **Then** they see an error message and the status stays "Unpaid"
- **And** they can try again or select "Pay at the restaurant".

**AC5**

- **Given** the guest sees the amount due
- **When** they select "Pay at the restaurant"
- **Then** they see the status "Paying at the restaurant"
- **And** the waiter sees this choice (US16).

**AC6**

- **Given** the guest has already paid online
- **When** they open their order again
- **Then** they see the status "Paid" and can't pay again.

**AC7**

- **Given** the owner kept the "Single payment" mode
- **When** the guest opens their order
- **Then** they see only their own total, with no payment methods.

**AC8**

- **Given** the guest selected "Pay at the restaurant"
- **When** the waiter marked their dishes as paid (US16)
- **Then** the guest sees the status "Paid".
