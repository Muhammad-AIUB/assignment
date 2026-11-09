# API Contract (frozen — do not rename fields or remove any)

Base URL: http://localhost:8080
All request/response bodies: JSON
Student identity header: `X-Student-Id` (17-digit registration number)

## 1. POST /classes
Request:
```json
{ "name": "Class 6 - Morning Shift", "totalSeats": 120 }
```
Response 201:
```json
{ "id": "c_123", "name": "Class 6 - Morning Shift", "totalSeats": 120 }
```

## 2. GET /classes
Response 200:
```json
[
  { "id": "c_123", "name": "Class 6 - Morning Shift", "totalSeats": 120,
    "heldSeats": 15, "confirmedSeats": 80, "availableSeats": 25 }
]
```

## 3. GET /classes/{classId}
Response 200: same shape as one item above.
404 if classId doesn't exist.

## 4. POST /classes/{classId}/holds
Header: X-Student-Id
Response 201:
```json
{ "holdId": "h_789", "classId": "c_123", "studentId": "20152691234567890",
  "status": "HELD", "expiresAt": "2026-11-01T04:10:00Z" }
```
- 409 if no seat available.
- 409 if student already has an active hold or confirmed admission in this class.
- 404 if classId doesn't exist.
- 400 if X-Student-Id missing or not 17 digits.

## 5. GET /holds/{holdId}
Response 200:
```json
{ "holdId": "h_789", "classId": "c_123", "status": "HELD",
  "expiresAt": "2026-11-01T04:10:00Z" }
```
status is one of: HELD, CONFIRMED, EXPIRED, CANCELLED
404 if holdId doesn't exist.

## 6. POST /holds/{holdId}/confirm
Header: X-Student-Id
Response 200:
```json
{ "holdId": "h_789", "status": "CONFIRMED" }
```
409 if hold belongs to another student, is expired, cancelled, or already confirmed
(idempotent: re-confirming an already-CONFIRMED hold by its owner returns 200,
confirmedSeats is NOT incremented again).
404 if holdId doesn't exist.

## 7. DELETE /holds/{holdId}
Header: X-Student-Id
204 on success.
409 if hold belongs to another student or is not in HELD status.
404 if holdId doesn't exist.

## 8. GET /me/holds
Header: X-Student-Id
Response 200:
```json
[
  { "holdId": "h_789", "classId": "c_123", "className": "Class 6 - Morning Shift",
    "status": "HELD", "expiresAt": "2026-11-01T04:10:00Z" }
]
```
