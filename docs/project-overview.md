# Green Tasty: project overview

Context for working on the **Group Pre-order** feature in the Green Tasty app. The feature's user stories are in [requirements-user-stories.md](requirements-user-stories.md).

## Product

Green Tasty is a chain of 3 restaurants with 10–20 tables each. The app already supports:

- restaurant search: location details, ratings, reviews;
- table reservations;
- menu browsing;
- pre-orders and order management;
- collecting guest feedback.

## Group Pre-order goal

**Problem.** People in the same party place pre-orders one by one, which causes confusion: the waiter has to merge several orders for one table by hand, and the kitchen receives them in parts.

**Solution.** Group Pre-order lets a party place one pre-order for the whole table at once. The reservation owner shares a link, each guest picks their own dishes, the owner sees the consolidated order and closes it, and the waiter receives one table order broken down by guest.

**Value:**

- faster service;
- less confusion;
- easier work for staff;
- more accurate data for planning.

The overall goal of the new functionality is to increase user satisfaction, business efficiency and planning. The feature is built as an MVP: only the essential functions plus feedback collection.

## User roles

| Role | Who | What they do in Group Pre-order | Stories |
| :- | :- | :- | :- |
| **Customer (Owner)**, reservation owner | Logged-in customer who created a reservation for 2+ guests | Invites guests via link, sees the consolidated pre-order, closes it, chooses the payment mode | US1, US12, US14 |
| **Guest (Visitor, unregistered)** | Member of the party without an account | Opens the link, enters a name, picks dishes, edits their selection until closure, pays their own share in Split the bill mode | US10, US11, US15, US17 |
| **Waiter** | Restaurant staff, logged in with the waiter role for their restaurant | Sees the table's group pre-order broken down by guest with payment statuses, marks in-restaurant payments | US16 |
| **System** | System actor | Creates the group session, generates and expires the link token, closes the order at the deadline, updates data in real time | US1, US10, US14 |

## Key business rules

- A group session is created automatically when a reservation gets the status "Reserved". Each reservation has exactly one group session.
- Group pre-order is available only if `guests_count` ≥ 2.
- The link is valid while the reservation has the status "Reserved". If the reservation is cancelled, completed or moved to a different restaurant, the token expires immediately.
- A guest joins without registration, with a name only.
- Guests can change their selection until the group order is closed.
- The owner closes the order. If they don't, the system closes it automatically at a default deadline before the reservation time. After closure, the items and totals are fixed.
- The owner's consolidated view updates immediately, without a manual refresh.
- Dishes with the status "On Stop" are marked as unavailable and are excluded from totals.
- The owner chooses the payment mode: "Single payment" (default) or "Split the bill". The mode can't be changed after the order is closed.
- In Split the bill mode each guest pays for their own dishes: online or at the restaurant. Online payment is available only after the order is closed. The waiter marks payments made at the restaurant.
- A waiter sees only orders of their own restaurant and can't change guests' dishes.

## Out of MVP scope

- Waiter confirming or rejecting each guest's selection (high cost, postponed).
- A comment on the pre-order.

## Open questions

- What is the value of the default deadline for automatic order closure?
- Which payment provider handles online payment?

## Business glossary

| Term | Meaning |
| :- | :- |
| **Reservation** | A table booking. Has a ReservationID, restaurant, date and time, table, owner, `guests_count` and status. |
| **Reservation status** | "Reserved" (active), "Cancelled", "Completed". |
| **guests_count** | Number of guests in the reservation. Group pre-order is available when it is ≥ 2. |
| **Group Pre-order** | One table order that each guest builds for themselves. |
| **Group Session / GroupSessionID** | The reservation's group session. Created automatically and mapped 1-to-1 to the ReservationID. |
| **Invite link, session token** | Invite link `https://greentasty.app/join/{secure_session_token}`. The token is cryptographically secure and non-guessable. |
| **Expired session page** | The page "This pre-order session has expired or is no longer active." shown to a guest who opens an inactive link. |
| **Owner** | The reservation owner (Customer) who invites guests and manages the group pre-order. |
| **Guest** | A party member without an account who joined via the link. |
| **Display name** | The name a guest enters when joining: mandatory, at least 1 character. The guest is shown to the owner and waiter under this name. |
| **Personal subcart** | A guest's own cart within the group pre-order: the dishes this guest selected. |
| **Consolidated view** | The owner's screen showing all guests, their dishes, each guest's total and the table total. Entry button: [ View Group Pre-order ]. |
| **Guest total** | Sum of the prices of all available dishes in the guest's personal subcart. |
| **Table total** | Sum of all guest totals. |
| **On Stop** | Status of a dish that is temporarily unavailable. It can't be added, and an already added one is excluded from totals. |
| **Closed group order** | State after [ Close Pre-order ] or the default deadline. Items and totals are fixed; guests can no longer change their selection. |
| **Default deadline** | The moment before the reservation time when the system closes the group order itself if the owner hasn't. |
| **Payment mode** | The owner's choice: "Single payment" (the table pays one total) or "Split the bill" (each guest pays for themselves). |
| **Payment method** | The guest's choice in Split the bill mode: "Pay online" or "Pay at the restaurant". The waiter sees "Online", "At the restaurant" or "Not selected". |
| **Payment status** | "Paid", "Unpaid", "Paying at the restaurant". |
| **Epic: Order Management** | The epic all stories of the feature belong to. |
| **Priorities** | Do first, Quick win, Next, Later. The order is set by Value ÷ Cost. |
