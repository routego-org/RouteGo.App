# CAP-UI-BKG-007 — Trip passengers

- Capability: CAP-UI-BKG-007
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/trip-passengers.md` (feature spec 0.1.0, AC-1 to AC-10)
- Area: Operator
- Slice: `Features/Operator/Passengers/` — `Passengers.Handler.cs` (wire records, call, heading and count words), `Passengers.cshtml.cs` (page model), `Passengers.cshtml`, tests in `Passengers.Tests.cs`
- Shared: `BookingTrip` (Common/UI/BookingModels.cs) for Booking's trip summary, `BookingDisplay.GenderLabel`.
- Also in this group: a "Passengers" link on every trip of `Features/Operator/Trips/` (my-trips rule 9), tested in `Trips.Tests.cs`.

**Text provenance:**

| Words | Source |
|---|---|
| "Passengers: {route}, {departure}", "Passengers: trip leaving {departure}" | business rule 5 |
| "{n} seats booked.", "1 seat booked." | UI states |
| "No seats are booked on this trip yet." | UI states, No passengers |
| "Passenger details for this trip are not available yet. If the trip was put on sale just now, try again in a minute." | UI states, Not available yet |
| Seat number, Seat class, Passenger name, Passenger age, Passenger gender, Booking ID | business rule 3 (`msg:label.*`) |
| "Try again", "Back to trips" | `msg:link.try_again`, UI states |

## Contract
- URL: `/operator/trips/{tripId}/passengers?route=Mumbai%20→%20Pune` (absolute `@page`, fixed). The route is only a label for the heading.
  The trip is not constrained to an identifier: an address that names no trip shows "not available yet" without calling Booking.
- Auth: **operators only** (`/Operator/` prefix); Booking decides whose trip it is (`FORBIDDEN`), the portal never does.
- The list is shown exactly as Booking gives it (seat order, no filtering). Booking never sends the contact's name or email or the amounts, so the page has nothing to hide.
- Read only; no script; no refresh (as loaded).
- Open question left as written: only "Back to trips" is offered, not a link to the trip's seat page.

## Form fields
None.

## Screen states
1. **Passengers** — heading, "{n} seats booked.", the table, "Back to trips".
2. **No passengers** — "0 seats booked.", "No seats are booked on this trip yet.", "Back to trips".
3. **Not available yet** — `NOT_FOUND`, a Booking answer of field errors, or an address that is no identifier: the message with "Try again" and "Back to trips".
4. **Forbidden** (`FORBIDDEN`) — the shared message with "Back to trips"; no heading, no passengers.
5. **Service unavailable / server error** — shared messages.

## Acceptance criteria

- [x] AC-1: the passengers in seat order — `Get_ConfirmedPassengers_ListsThemInBookingsOrderWithoutContactDetails`; the link from the trip list — `Get_EveryTrip_LinksToItsPassengersWithRouteName` (Trips)
- [x] AC-2: only confirmed bookings — Booking sends only those; the page lists exactly what it is given (same test as AC-1)
- [x] AC-3: a seat change is reflected — same: the page shows Booking's list as given
- [x] AC-4: no bookings yet — `Get_NoBookings_SaysSoWithTheSameHeadingAndBackLink`
- [x] AC-5: without a route — `Get_WithoutRoute_HeadsThePageWithTheDepartureAlone`
- [x] AC-6: another company's trip — `Get_AnotherCompanysTrip_ShowsForbiddenWithBackLinkAndNoPassengers`
- [x] AC-7: not available yet — `Get_TripBookingDoesNotKnowYet_SaysNotAvailableYetWithTryAgainAndBack`, `Get_AddressThatIsNoIdentifier_SaysNotAvailableYetWithoutCallingBooking`
- [x] AC-8: only operators — `Get_CustomerOrAdmin_ShowsNoPermissionWithoutCallingBooking`
- [x] AC-9: visitor — `Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress`
- [x] AC-10: Booking down — `Get_BookingDown_ShowsUpstreamMessageAndKeepsTheMenu`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listTripPassengers` | `routego-booking:CAP-BKG-007@0.1.3` | `GET /trips/{tripId}/passengers` → 200 TripPassengerList | The page | `NOT_FOUND`, `FORBIDDEN` |

Wire shape from Booking's `contracts/bookings/passenger.yaml` (no Booking API in `02-Code-Projects` yet). Tested against `FakeBackend` only; not live-tested.
