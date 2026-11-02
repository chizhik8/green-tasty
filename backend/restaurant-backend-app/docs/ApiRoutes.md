# API Routes

## Auth

### `POST` /register
```json
{
    "firstName": "John",
    "lastName": "Doe",
    "email": "john_doe@mail.com",
    "password": "1234SecurePassword!",
    "confirmPassword": "1234SecurePassword!"
}
```

Response: 
`200 OK`
```json
{
    "message":"User registered and confirmed successfully"
}
```

### `POST` /signin
```json
{
    "email": "john_doe@mail.com",
    "password": "1234SecurePassword!",
}
```

Response: `200 OK`
```json
{
    "message": "Sign in successful",
    "idToken": "token",
    "accessToken": "access token",
}
```

## Feedback

### `GET` /feedback
Query parameters:
- `locationId` (string, required)
- `type` (Service | CuisineExperience)
- `sort` (TopRatedFirst | LowRatedFirst | NewestFirst | OldestFirst)
- `page` (integer, required)

Response: `200 OK`
```json
{
  "items": [
    {
      "reservationId": "string",
      "locationId": "string",
      "userId": "string|null",
      "comment": "string|null",
      "rating": 5,
      "createdAt": "2026-01-01T12:00:00Z",
      "type": "string",
      "username": "string|null"
    }
  ],
  "totalPages": 1,
  "currentPage": 1
}
```

### `POST` /feedback
Request body:
```json
{
  "reservationId": "string",
  "userId": "string|null",
  "comment": "string|null",
  "rating": 5,
  "type": "Service | CuisineExperience"
}
```

Response: `200 OK`
```json
{
  "reservationId": "string",
  "locationId": "string",
  "userId": "string|null",
  "comment": "string|null",
  "rating": 5,
  "createdAt": "2026-01-01T12:00:00Z",
  "type": "string",
  "username": "string|null"
}
```

### `PUT` /feedback
Request body:
```json
{
  "reservationId": "string",
  "userId": "string|null",
  "comment": "string|null",
  "rating": 5,
  "type": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Feedback updated successfully."
}
```

## Locations

### `POST` /locations
Request body:
```json
{
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageBase64": "string|null",
  "rating": 4.5
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageUrl": "string|null",
  "rating": 4.5
}
```

### `GET` /locations
Response: `200 OK`
```json
[
  {
    "id": "string",
    "address": "string",
    "description": "string",
    "totalCapacity": 50,
    "averageOccupancy": "string",
    "imageUrl": "string|null",
    "rating": 4.5
  }
]
```

### `GET` /locations/{id}/dishes
Response: `200 OK`
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "string",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `DELETE` /locations/{id}/dishes/{dishId}
Removes the dish from the specialty dishes of the location `id`.

Authorization: Cognito ID token; only the `Waiter` group. The handler does not check that the location is the waiter's own location.

Response: `200 OK`
```json
{
  "message": "Dish '<dishId>' successfully removed from location '<id>'."
}
```

Errors, in the order they are checked:
- `403` `{ "error": "Forbidden: Only waiters can perform this action." }`: the caller is not in the `Waiter` group.
- `400` `{ "error": "..." }`: `id` or `dishId` path parameter is missing.
- `400` `{ "error": "..." }`: the location does not exist, or the dish is not in its specialty dishes.


### `GET` /locations/{id}
Response: `200 OK`
```json
{
  "id": "string",
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageUrl": "string|null",
  "rating": 4.5
}
```

### `PUT` /locations/{id}
Request body:
```json
{
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageBase64": "string|null",
  "rating": 4.5
}
```
Response: 200 OK
```json
{
  "id": "string",
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageUrl": "string|null",
  "rating": 4.5
}
```

### `DELETE` /locations/{id}
Response: 200 OK
```json
{
  "message": "Location deleted successfully"
}
```

## Waiters

### `POST` /waiters
Request body:
```json
{
  "email": "waiter@example.com",
  "locationId": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Email added successfully"
}
```

### `GET` /waiters/{userId}/location
Response: `200 OK`
```json
{
  "locationId": "string"
}
```

### `GET` /locations/{id}/waiters
Returns the waiters assigned to the location `id` (items of the `Waiters` table). A waiter may read only the location they are assigned to.

Authorization: Cognito ID token; only the `Waiter` group, and the caller's own location (the `locationId` of the caller's `Waiters` item, found by `sub`) must equal `id`.

Response: `200 OK`. `reservations` is `[]` when the waiter has none; the order of elements is not specified. The location is not checked for existence; an empty result is `[]`.
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

Errors, in the order they are checked:
- `403` `{ "error": "Forbidden" }`: the caller is not in the `Waiter` group.
- `400` `{ "error": "Location id is required." }`: `id` path parameter is missing or empty.
- `403` `{ "error": "Forbidden" }`: no `sub` claim, the caller has no `Waiters` item, or the caller's location is not `id`.

### `DELETE` /waiters
Request body:
```json
{
  "email": "waiter@example.com",
  "locationId": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Email deleted successfully"
}
```

## Dishes

### `POST` /dishes
Request body:
```json
{
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageBase64": "string|null",
  "state": "string",
  "locationIds": ["string"]
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageUrl": "string",
  "state": "string"
}
```

### `GET` /dishes
Optional query parameters:
- `dishType` (Appetizer | MainCourse | Dessert)
- `sortBy` (PriceAscending, PriceDescending, PopularityAscending, PopularityDescending)
- `name`

Response: `200 OK`
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "string",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `PUT` /dishes/{id}
Request body:
```json
{
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageBase64": "string|null",
  "state": "string"
}
```
Response: 200 OK
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageUrl": "string",
  "state": "string"
}
```

### `DELETE` /dishes/{id}
Query parameters:
  force (boolean, optional)
Response: 200 OK
```json
{
  "message": "Dish '{id}' deleted successfully."
}
```


### `GET` /dishes/popular
Response: `200 OK`
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "string",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `GET` /dishes/{id}
Response: `200 OK`
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageUrl": "string",
  "state": "string"
}
```

## Reservations

### `POST` /reservations
Request body:
```json
{
  "tableId": "string",
  "customerEmail": "string|null",
  "startTime": "2026-08-07T19:00:00Z",
  "endTime": "2026-08-07T20:30:00Z",
  "guestCount": 4,
  "customerId": "string|null"
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "tableId": "string",
  "startTime": "2026-08-07T19:00:00Z",
  "endTime": "2026-08-07T20:30:00Z",
  "guestCount": 4,
  "status": "Reserved",
  "customerId": "string|null",
  "waiterId": "string",
  "locationAddress": "string",
  "restaurantName": "Green & Tasty",
  "hasSubmittedPreOrder": false
}
```

### `GET` /reservations
Response: `200 OK`
```json
[
  {
    "id": "string",
    "tableId": "string",
    "startTime": "2026-08-07T19:00:00Z",
    "endTime": "2026-08-07T20:30:00Z",
    "guestCount": 4,
    "status": "Reserved",
    "customerId": "string|null",
    "waiterId": "string",
    "locationAddress": "string",
    "restaurantName": "Green & Tasty",
    "hasSubmittedPreOrder": false
  }
]
```

### `GET` /reservations/{reservationId}
Response: `200 OK`
```json
{
  "id": "string",
  "tableId": "string",
  "startTime": "2026-08-07T19:00:00Z",
  "endTime": "2026-08-07T20:30:00Z",
  "guestCount": 4,
  "status": "Reserved",
  "customerId": "string|null",
  "waiterId": "string",
  "locationAddress": "string",
  "restaurantName": "Green & Tasty",
  "hasSubmittedPreOrder": false
}
```

### `PATCH` /reservations/{reservationId}/status
Request body:
```json
{
  "newStatus": "Completed"
}
```

Response: `200 OK`
```json
{
  "message": "Reservation status updated successfully."
}
```

### `GET` /reservations/{reservationId}/invite-token
Response: `200 OK`
```json
{
  "token": "string"
}
```

### `GET` /reservations/{reservationId}/feedback
Response: `200 OK`
```json
{
  "service": {
    "reservationId": "string",
    "locationId": "string",
    "userId": "string|null",
    "comment": "string|null",
    "rating": 5,
    "createdAt": "2026-08-07T21:00:00Z",
    "type": "string",
    "username": "string|null"
  },
  "cuisineExperience": {
    "reservationId": "string",
    "locationId": "string",
    "userId": "string|null",
    "comment": "string|null",
    "rating": 5,
    "createdAt": "2026-08-07T21:00:00Z",
    "type": "string",
    "username": "string|null"
  }
}
```

### `GET` /reservations/{reservationId}/guest-session-id
Returns the id of the reservation owner's own session in the group pre-order. The id is always `owner-{groupReservationId}`: a group has at most one owner session. If the group has no owner session yet, it is created, but only while the group is `Open` and the reservation is `Reserved`. An existing owner session id is returned in any group or reservation status. Concurrent first calls create one session and all return its id.

Authorization: Cognito ID token; only the reservation owner (`reservation.customerId == sub`).

Response: `200 OK`
```json
{
  "guestSessionId": "owner-group-reservation-123"
}
```

Errors, in the order they are checked:
- `401` `{ "error": "Unauthorized." }`: no `sub` claim.
- `404` `{ "error": "..." }`: the reservation does not exist.
- `403` `{ "error": "..." }`: the caller does not own the reservation.
- `404` `{ "error": "..." }`: the reservation has no group reservation.
- `400` `{ "message": "...", "errorCode": "GroupReservationSubmitted" }`: no owner session yet and the group is submitted.
- `400` `{ "message": "...", "errorCode": "GroupReservationExpired" }`: no owner session yet and the group is closed or the reservation is not `Reserved`.


### `DELETE` /reservations
Request body:
```json
{
  "id": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Reservation cancelled successfully"
}
```

### `PUT` /reservations
Request body:
```json
{
  "reservationId": "string",
  "startTime": "2026-08-07T19:15:00Z",
  "endTime": "2026-08-07T20:45:00Z",
  "guestCount": 5
}
```

Response: `200 OK`
```json
{
  "id": "string",
  "tableId": "string",
  "startTime": "2026-08-07T19:15:00Z",
  "endTime": "2026-08-07T20:45:00Z",
  "guestCount": 5,
  "status": "Reserved",
  "customerId": "string|null",
  "waiterId": "string",
  "locationAddress": "string",
  "restaurantName": "Green & Tasty",
  "hasSubmittedPreOrder": false
}
```

### `PATCH` /reservations/{reservationId}/order/confirmations/{dishId}
Response: `200 OK`
```json
{
  "message": "Pre order confirmed successfully"
}
```

### `PATCH` /reservations/{reservationId}/order/cancellations/{dishId}
Response: `200 OK`
```json
"Pre order dish canceled successfully"
```

### `PATCH` /reservations/{reservationId}/table
Request body:
```json
{
  "tableId": "string"
}
```

Response: `200 OK`
```json
"Your reservation table was updated successfully."
```

## Tables

### `POST` /tables
Request body:
```json
{
  "locationId": "string",
  "capacity": 4,
  "tableNumber": 10
}
```

### `PUT` /tables/{id}
Request body:
```json
{
  "capacity": 4,
  "tableNumber": 10
}
```
Response: 200 OK
```json
{
  "id": "string",
  "locationId": "string",
  "capacity": 4,
  "tableNumber": 10
}
```

### `DELETE` /tables/{id}
Response: 200 OK
```json
{
  "message": "Table deleted successfully"
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "locationId": "string",
  "capacity": 4,
  "tableNumber": 10
}
```

### `GET` /available-tables
Query parameters:
- `locationId` (string, optional)
- `capacity` (integer, optional)
- `startTime` (ISO 8601 string, optional)

Response: `200 OK`
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

### `GET` /locations/{id}/tables/all
Returns all tables of the location `id`.

Authorization: Cognito ID token; only the `Waiter` group. The handler does not check that the location is the waiter's own location.

Response: `200 OK`. `[]` when the location has no tables or does not exist.
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

Errors, in the order they are checked:
- `403` `{ "error": "Forbidden" }`: the caller is not in the `Waiter` group.
- `400` `{ "error": "..." }`: `id` path parameter is missing.

### `GET` /tables/{id}/available-time-slots
Query parameters:
- `startTime` (ISO 8601 string, required)

Response: `200 OK`
```json
[
  {
    "start": "2026-08-07T19:00:00Z",
    "end": "2026-08-07T20:30:00Z"
  }
]
```

## User Profile

### `GET` /me
Response: `200 OK`
```json
{
  "id": "string",
  "firstName": "string",
  "lastName": "string",
  "email": "string",
  "role": "string"
}
```

### `PUT` /me
Request body:
```json
{
  "firstName": "string",
  "lastName": "string"
}
```

Response: `200 OK`
```json
"User information updated successfully."
```

### `PUT` /me/change-password
Request body:
```json
{
  "oldPassword": "string",
  "newPassword": "string",
  "accessToken": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Password changed successfully."
}
```

### `GET` /users
Query parameters:
- `fullName` (string, required)

Response: `200 OK`
```json
[
  {
    "id": "string",
    "firstName": "string",
    "lastName": "string",
    "email": "string",
    "role": "string"
  }
]
```

### `GET` /users/{id}/profile-image
Response: `200 OK`
```json
{
  "presignedUrl": "https://..."
}
```

If the user has not uploaded an image yet, the route returns:
```json
"Profile image is not uploaded so far."
```

### `PUT` /me/profile-image
Request body:
```json
{
  "base64Image": "string"
}
```

Response: `200 OK`
```json
"Photo was uploaded successfully!"
```

### `GET` /users/{id}/name
Response: `200 OK`
```json
{
  "firstName": "string",
  "lastName": "string"
}
```

## Orders

### `GET` /orders
Response: `200 OK`
```json
[
  {
    "orderId": "string",
    "reservationId": "string",
    "reservationAddress": "string",
    "reservationTime": "string",
    "orderItems": [
      {
        "dish": {
          "id": "string",
          "name": "string",
          "description": "string",
          "dishType": "string",
          "price": 12.34,
          "weight": "string",
          "calories": "string",
          "proteins": "string",
          "fats": "string",
          "carbohydrates": "string",
          "vitamins": "string",
          "imageUrl": "string",
          "state": "string"
        },
        "quantity": 2
      }
    ],
    "status": "Created"
  }
]
```

### `GET` /orders/{reservationId}
Response: `200 OK`
```json
{
  "orderId": "string",
  "reservationId": "string",
  "reservationAddress": "string",
  "reservationTime": "string",
  "orderItems": [
    {
      "dish": {
        "id": "string",
        "name": "string",
        "description": "string",
        "dishType": "string",
        "price": 12.34,
        "weight": "string",
        "calories": "string",
        "proteins": "string",
        "fats": "string",
        "carbohydrates": "string",
        "vitamins": "string",
        "imageUrl": "string",
        "state": "string"
      },
      "quantity": 2
    }
  ],
  "status": "Created"
}
```

### `POST` /orders
Request body:
```json
{
  "reservationId": "string",
  "orderItems": [
    {
      "dishId": "string",
      "quantity": 2
    }
  ]
}
```

Response: `201 Created`
```json
{
  "orderId": "string",
  "reservationId": "string",
  "reservationAddress": "string",
  "reservationTime": "string",
  "orderItems": [
    {
      "dish": {
        "id": "string",
        "name": "string",
        "description": "string",
        "dishType": "string",
        "price": 12.34,
        "weight": "string",
        "calories": "string",
        "proteins": "string",
        "fats": "string",
        "carbohydrates": "string",
        "vitamins": "string",
        "imageUrl": "string",
        "state": "string"
      },
      "quantity": 2
    }
  ],
  "status": "Created"
}
```

### `PUT` /orders/{reservationId}
Request body:
```json
{
  "orderItems": [
    {
      "dishId": "string",
      "quantity": 3
    }
  ]
}
```

Response: `200 OK`
```json
"Pre-order updated successfully."
```

### `DELETE` /orders/{reservationId}
Response: `200 OK`
```json
"Pre-order canceled successfully."
```

### `POST` /orders/{reservationId}/submit
Response: `200 OK`
```json
"Pre-order submitted successfully."
```

### `POST` /orders/{reservationId}/unsubmit
Response: `200 OK`
```json
"Pre-order unsubmitted successfully."
```

## Reports

### `GET` /reports/download
Query parameters:
- `format` (string, optional: Excel, Pdf, Csv)
- `type` (string, optional: Service, Sales)
- `startDate` (YYYY-MM-DD, optional)
- `endDate` (YYYY-MM-DD, optional)
- `locationId` (string, required)

Response: `200 OK` with file download content

### `GET` /reports/generate
Query parameters:
- `type` (string, optional: Service, Sales)
- `startDate` (YYYY-MM-DD, required)
- `endDate` (YYYY-MM-DD, required)
- `locationId` (string, required)

Response: `200 OK`
```json
[
  {
    "reportType": "Service",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "userId": "string",
    "locationId": "string",
    "locationName": "string",
    "waiterId": "string",
    "waiterName": "string",
    "waiterEmail": "string",
    "workingHours": 72.5,
    "totalOrders": 28,
    "avgServiceFeedback": 4.7,
    "minServiceFeedback": 3.8,
    "deltaAvgServiceFeedback": 1.2,
    "deltaTotalOrders": 5.0
  }
]
```

For `type=Sales`, the response is an array of sales report history objects:
```json
[
  {
    "reportType": "Sales",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "userId": "string",
    "locationId": "string",
    "locationName": "string",
    "totalOrders": 147,
    "avgCuisineFeedback": 4.3,
    "minCuisineFeedback": 2.5,
    "revenue": 32145.60,
    "deltaAvgCuisineFeedback": 2.1,
    "deltaRevenue": 8.4
  }
]
```

### `GET` /reports
Response: `200 OK`
```json
[
  {
    "id": "string",
    "reportType": "Service",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "locationId": "string",
    "createdAt": "2026-08-07T12:00:00Z"
  }
]
```

## Notifications

### `GET` /notifications
Response: `200 OK`
```json
[
  {
    "id": "string",
    "message": "string",
    "orderId": "string",
    "reservationId": "string",
    "createdAt": "2026-08-07T12:00:00Z",
    "isRead": false
  }
]
```

### `PATCH` /notifications/{id}/read
Response: `200 OK`
```json
"Notification marked as read"
```

## Group & Guest Reservations

### `GET` /guest/invites/{token}
Returns the invitation details for a group reservation token.

Authorization: none (`NONE`).

Response: `200 OK`. Field names are PascalCase (the handler uses `ResponseBuilder.CreateSuccessResponse`, which keeps C# property names).
```json
{
  "GroupReservationId": "string",
  "LocationId": "string",
  "RestaurantName": "Green & Tasty",
  "LocationAddress": "string",
  "StartTime": "2026-08-07T19:00:00",
  "EndTime": "2026-08-07T20:30:00",
  "MaxGuestCount": 4,
  "TableNumber": 1,
  "InviterName": "string"
}
```

- `RestaurantName`: always `Green & Tasty`.
- `LocationAddress`: `Unknown Address` if the location is not found.
- `StartTime`, `EndTime`: the reservation times as ISO 8601 strings (the default `System.Text.Json` `DateTime` format; whether an offset suffix is present depends on the `DateTime` kind loaded from DynamoDB).
- `MaxGuestCount`: the reservation's `guestCount`, the owner included.
- `InviterName`: the reservation owner's name, or `Unknown`.

Errors, in the order they are checked:
- `404` `{ "error": "..." }`: no group reservation has this token.
- `400` `{ "message": "...", "errorCode": "GroupReservationExpired" }`: the group is not `Open`.
- `400` `{ "message": "...", "errorCode": "Unknown" }`: the group's reservation does not exist.
- `400` `{ "message": "...", "errorCode": "GuestLimitExceeded" }`: joined guest sessions (owner sessions excluded) + 1 for the owner >= `guestCount`.
- `500` `{ "error": "Internal Server Error" }`: the reservation's table does not exist.

### `POST` /guest/sessions
Request body:
```json
{
  "groupReservationId": "string",
  "guestName": "string"
}
```
Response: 201 Created
```json
{
  "sessionToken": "string"
}
```

### `GET` /guest/sessions/{guestSessionId}
Response: `200 OK`
```json
{
  "id": "string",
  "groupReservationId": "string",
  "guestName": "string",
  "status": "string"
}
```
### `POST` /guest/sessions/{guestSessionId}/dishes
Request body:
```json
{
  "dishId": "string",
  "quantity": 1
}
```
Response: 200 OK
```json
{
  "message": "Dish added to guest session successfully."
}
```

### `DELETE` /guest/sessions/{guestSessionId}/dishes
Request body:
```json
{
  "dishId": "string"
}
```
Response: 200 OK
```json
{
  "message": "Dish removed from guest session successfully."
}
```

### `GET` /guest/sessions/{guestSessionId}/menu
Response: 200 OK
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "Available",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `POST` /guest/sessions/{guestSessionId}/submit
Response: 200 OK
```json
{
  "message": "Guest cart submitted successfully."
}
```

### `GET` /guest/groups/{groupReservationId}/status
Returns the group status and the participant count.

Authorization: none (`NONE`).

Response: `200 OK`
```json
{
  "currentParticipants": 3,
  "maxCapacity": 4,
  "status": "Open"
}
```

- `currentParticipants`: joined guest sessions (owner sessions excluded) + 1 for the owner, whether or not an owner session exists.
- `maxCapacity`: the reservation's `guestCount`.
- `status`: `Open`, `Submitted` or `Closed`.

Errors:
- `404` `{ "error": "..." }`: the group reservation does not exist.
- `500` `{ "error": "Internal server error: ..." }`: the group's reservation does not exist, or another unexpected error.

### `POST` /group-reservations/{groupReservationId}/submit
Submits the consolidated group pre-order to the reservation's order. Only sessions with `isSubmitted: true` are used (guest and owner sessions alike). Cart lines whose dish no longer exists or whose state is not `Available` are dropped, and lines with the same `dishId` are merged by summing `quantity`. The result is added to the active order of the reservation: the quantity is added to an existing order line with the same `dishId` (its state is kept), other dishes are appended as `Pending`. The order status becomes `SubmittedByCustomer` and the group reservation status becomes `Submitted`; both are written in one transaction, only if the group is still `Open`, so a second or concurrent submit of the same group does not add the dishes again. The submit is allowed only up to 30 minutes before the reservation start. Nothing is saved when any check fails.

Authorization: Cognito ID token; only the reservation owner (`reservation.customerId == sub`).

Request body:
```json
{
  "reservationId": "reservation-123"
}
```

Response: `200 OK`. The consolidated dishes from the submitted sessions (not the whole order); the order of elements is not specified.
```json
[
  {
    "dishId": "dish1",
    "quantity": 3
  },
  {
    "dishId": "dish2",
    "quantity": 2
  }
]
```

Errors, in the order they are checked:
- `401` `{ "error": "Unauthorized." }`: no `sub` claim.
- `400` `{ "error": "..." }`: missing or unparsable body, or empty `reservationId`.
- `404` `{ "error": "..." }`: the group reservation does not exist.
- `400` `{ "message": "...", "errorCode": "Unknown" }`: `reservationId` is not the reservation of this group.
- `404` `{ "error": "..." }`: the reservation does not exist.
- `403` `{ "error": "..." }`: the caller does not own the reservation.
- `400` `{ "message": "...", "errorCode": "GroupReservationSubmitted" }`: the group is already submitted.
- `400` `{ "message": "...", "errorCode": "GroupReservationExpired" }`: the group is closed, or the reservation is not `Reserved`.
- `400` `{ "message": "...", "errorCode": "GroupReservationExpired" }`: less than 30 minutes remain before the reservation start (a submit exactly at the cutoff is still allowed).
- `400` `{ "message": "...", "errorCode": "Unknown" }`: no session is submitted; the submitted sessions contain no available dishes; or the reservation has no active order.
- `400` `{ "message": "...", "errorCode": "GroupReservationSubmitted" }` or `"GroupReservationExpired"`: the group was submitted or closed by another request after the checks above (for example, a repeated submit, or one that loses to another submit); nothing is written. A truly concurrent submit may instead get `500` when DynamoDB cancels its transaction with a transaction conflict; nothing is written in that case either. A client that retries after a lost response or such a `500` gets `GroupReservationSubmitted`.

### `GET` /guest/groups/{groupReservationId}/sessions
Returns the group snapshot: every participant session of the group with its dishes and totals. Allowed in any group and reservation status.

Authorization: Cognito ID token; only the reservation owner (`reservation.customerId == sub`).

Response: `200 OK`
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

- `participants`: one entry per session of the group. The owner session, if it exists, comes first with `type: "Owner"` and the owner's Cognito `given_name` (the stored session name if the user or attribute is not found); guests follow with `type: "Guest"` and their guest name.
- `items`: cart lines with the full dish and `quantity`. A line whose dish no longer exists is omitted.
- `total` (participant): sum of `price × quantity` over items whose dish `state` is `Available`; items in another state are listed but not counted.
- `total` (top level): sum of the participant totals. A group without sessions returns `participants: []` and `total: 0`.

Errors, in the order they are checked:
- `401` `{ "error": "Unauthorized." }`: no `sub` claim.
- `404` `{ "error": "..." }`: the group reservation does not exist, or its reservation does not exist.
- `403` `{ "error": "..." }`: the caller does not own the reservation.

### `DELETE` /guest/groups/{groupReservationId}/sessions/{guestSessionId}
Removes a guest's session from the group and returns the updated group snapshot.

Authorization: Cognito ID token; only the reservation owner (`reservation.customerId == sub`).

Response: `200 OK`. The snapshot of the remaining sessions, in the same format and with the same rules as `GET /guest/groups/{groupReservationId}/sessions`.

Errors, in the order they are checked (nothing is deleted on an error):
- `401` `{ "error": "Unauthorized." }`: no `sub` claim.
- `404` `{ "error": "..." }`: the group reservation does not exist, or its reservation does not exist.
- `403` `{ "error": "..." }`: the caller does not own the reservation.
- `400` `{ "message": "...", "errorCode": "GroupReservationSubmitted" }`: the group is submitted.
- `400` `{ "message": "...", "errorCode": "GroupReservationExpired" }`: the group is closed, or the reservation is not `Reserved`.
- `404` `{ "error": "..." }`: the guest session does not exist.
- `400` `{ "message": "...", "errorCode": "Unknown" }`: the guest session belongs to another group, or it is the owner session.