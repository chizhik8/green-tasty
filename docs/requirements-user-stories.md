# Green Tasty: Group Pre-order. User stories

All user stories of the Group Pre-order feature. Product context, roles and glossary: [project-overview.md](project-overview.md).

All stories belong to the **Order Management** epic. The order in this file follows the user journey: invitation, guest joining, dish selection, owner's consolidated view, closing, guest editing, waiter view, payment.

| US | Name | Role | Priority |
| :- | :- | :- | :- |
| [US1](#us1) | Initiation of Group Pre-order Session | Customer (Owner) | Do first |
| [US10](#us10) | Guest Joining a Group Pre-order Session via Link | Guest (Visitor, unregistered) | Do first |
| [US11](#us11) | Guest Reservation and Menu View | Guest (Visitor, unregistered) | Do first |
| [US12](#us12) | Owner Viewing Consolidated Group Pre-order | Customer (Owner) | Do first |
| [US14](#us14) | Owner Closing Group Pre-order | Customer (Owner) | Do first |
| [US15](#us15) | Guest Editing Own Pre-order | Guest (Visitor, unregistered) | Do first |
| [US16](#us16) | Waiter Viewing Table Group Pre-order | Waiter | Do first |
| [US17](#us17) | Guest Paying Own Share (Split the bill) | Guest (Visitor, unregistered) | Do first |

Cross-references between stories are written as `(USxx)`. US10 and US11 use legacy references: `US_Owner_1` means US1, `US_G10` means US10.

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

**Owner Viewing Consolidated Group Pre-order**

- **Epic:** Order Management
- **Role:** Customer (Owner)
- **Priority:** Do first
- **Description:** As the group owner, I want to see on one screen the pre-orders of all guests in my group with their dishes, each guest's total and the table total, so that I can check who chose what before closing the order and don't have to collect orders by hand.

### Pre-conditions

- The Customer is logged in.
- The Customer is the reservation owner, the reservation has the status "Reserved".
- The reservation's guests_count is ≥ 2, the group session has been created (US1).

### Acceptance Criteria

**AC1**

- **Given** the reservation has the status "Reserved" and guests_count ≥ 2
- **When** the owner opens this reservation in "Reservations"
- **Then** they see the [ View Group Pre-order ] button to open the consolidated view.

**AC2**

- **Given** guests have joined the group session via the link (US10)
- **When** the owner opens the consolidated view
- **Then** they see each guest under the name the guest entered when joining (US10)
- **And** the number of guests on the screen equals the number of guests who joined.

**AC3**

- **Given** a guest has added dishes to their personal subcart from US11
- **When** the owner opens the consolidated view
- **Then** under the guest's name they see each of the guest's dishes with its name and price from the restaurant menu.

**AC4**

- **Given** a guest's personal subcart contains dishes
- **When** the owner opens the consolidated view
- **Then** the guest total is shown next to the guest's name and equals the sum of the prices of all their available dishes.

**AC5**

- **Given** at least one guest has dishes in their personal subcart
- **When** the owner opens the consolidated view
- **Then** they see the table total, which equals the sum of all guest totals.

**AC6**

- **Given** a guest has joined but their personal subcart is empty
- **When** the owner opens the consolidated view
- **Then** the guest is listed with the label "No dishes selected yet" and their total is 0.

**AC7**

- **Given** no guest has joined yet
- **When** the owner opens the consolidated view
- **Then** they see the message "No guests have joined yet" and the table total is 0.

**AC8**

- **Given** the owner has the consolidated view open
- **When** a guest adds or removes a dish
- **Then** the guest's personal subcart, the guest total and the table total update immediately on the owner's screen, without a manual refresh or reopening the screen.

**AC9**

- **Given** the group order is not closed yet
- **And** a dish in a guest's personal subcart got the status "On Stop" after it was added
- **When** the owner opens the consolidated view
- **Then** the dish is marked as unavailable
- **And** its price is included in neither the guest total nor the table total.

**AC10**

- **Given** the group order is closed (US14)
- **When** the owner opens the consolidated view
- **Then** they see the final order items and totals with the label "Pre-order closed"
- **And** the items and totals are fixed as of closure and no longer change
- **And** there is no close order button.

**AC11**

- **Given** the reservation is cancelled, completed or moved to a different restaurant
- **When** the owner tries to open the consolidated view
- **Then** they see a message that the group session is inactive and can't change the order.

**AC12**

- **Given** the reservation's guests_count = 1
- **When** the owner opens the reservation in "Reservations"
- **Then** there is no [ View Group Pre-order ] button.

**AC13**

- **Given** the user is not the reservation owner, including a guest who joined via the link
- **When** they try to open the consolidated view
- **Then** they have no access to the consolidated view.

**AC14**

- **Given** the owner is viewing the consolidated view
- **When** a new guest joins the session
- **Then** the guest immediately appears in the list without a manual screen refresh.

**AC15**

- **Given** the owner is viewing the consolidated view and the group order is not closed yet
- **When** a dish in a guest's personal subcart gets the status "On Stop"
- **Then** the dish is immediately marked as unavailable and excluded from totals.

**AC16**

- **Given** the owner is viewing the consolidated view
- **When** the system closes the order at the default deadline
- **Then** the screen immediately shows "Pre-order closed" and the close button disappears.

**AC17**

- **Given** the owner is viewing the consolidated view
- **When** the reservation is cancelled, completed or moved to a different restaurant
- **Then** the screen immediately shows that the group session is inactive.

**AC18**

- **Given** automatic updates are temporarily unavailable, for example the internet connection is lost
- **When** the owner is viewing the consolidated view
- **Then** they see a warning that the data may be out of date and can refresh the screen manually.

**AC19**

- **Given** the group order is open
- **When** the owner opens the consolidated view
- **Then** they see the payment mode toggle "Single payment / Split the bill", with "Single payment" selected by default.

**AC20**

- **Given** the group order is open
- **When** the owner selects "Split the bill"
- **Then** the mode is saved, and each guest pays for their own dishes: online or at the restaurant (US17).

**AC21**

- **Given** the group order is closed (US14)
- **When** the owner opens the consolidated view
- **Then** they see the selected payment mode, which can no longer be changed.

---

## US14

**Owner Closing Group Pre-order**

- **Epic:** Order Management
- **Role:** Customer (Owner)
- **Priority:** Do first
- **Description:** As the group owner, I want to close the group pre-order once everyone has chosen their dishes, so that the restaurant receives the final table order before we arrive.

### Pre-conditions

- The Customer is logged in and is the reservation owner.
- The reservation has the status "Reserved", the group session has been created (US1).
- The group order is not closed yet.

### Acceptance Criteria

**AC1**

- **Given** the group order is open
- **When** the owner opens the consolidated view (US12)
- **Then** they see the [ Close Pre-order ] button.

**AC2**

- **Given** the group order is open
- **When** the owner clicks [ Close Pre-order ]
- **Then** the order gets the status "Closed"
- **And** the consolidated view shows the final order items and totals with the label "Pre-order closed".

**AC3**

- **Given** the owner hasn't closed the order
- **When** the default deadline before the reservation time is reached
- **Then** the system automatically closes the order with the same result as closure by the owner.

**AC4**

- **Given** the group order is closed
- **When** a guest opens the link or their personal subcart
- **Then** they see their dishes and the message "Pre-order is closed, changes are no longer possible"
- **And** they can't add, remove or change dishes.

**AC5**

- **Given** the group order is closed
- **When** the waiter opens the table order (US16)
- **Then** they see it as final.

**AC6**

- **Given** the group order is closed
- **When** the owner opens the consolidated view
- **Then** there is no [ Close Pre-order ] button.

**AC7**

- **Given** the reservation is cancelled, completed or moved to a different restaurant
- **When** the owner opens the reservation
- **Then** there is no [ Close Pre-order ] button, because the group session is inactive.

---

## US15

**Guest Editing Own Pre-order**

- **Epic:** Order Management
- **Role:** Guest (Visitor, unregistered)
- **Priority:** Do first
- **Description:** As a guest, I want to view and change my pre-order until the group order is closed, so that the table gets exactly what I want and the waiter doesn't have to fix the order on site.

### Pre-conditions

- The guest has joined the group session (US10).
- The reservation has the status "Reserved".
- The group order is not closed yet.

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

- **Given** the group order was closed by the owner or at the deadline (US14)
- **When** the guest tries to change the personal subcart
- **Then** changes are not available and the guest sees the message "Pre-order is closed, changes are no longer possible".

**AC7**

- **Given** the guest opened the personal subcart while the order was open
- **And** the order was closed after that
- **When** the guest adds or removes a dish
- **Then** the change is not saved
- **And** the guest sees the message "Pre-order is closed, changes are no longer possible" and their personal subcart as it was at closure.

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
