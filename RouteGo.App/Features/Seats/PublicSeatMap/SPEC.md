# CAP-UI-SEAT-001 — Public seat map

- Capability: CAP-UI-SEAT-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.3.0) → `capabilities/seats/features/public-seat-map.md` (feature spec 0.1.0)
- Area: Seats
- Slice: `Features/Seats/PublicSeatMap/` — `PublicSeatMap.cshtml.cs` (wire records, handler, page model), tests in `PublicSeatMap.Tests.cs`
- Shared with the operator's and the admin's seat pages: `Common/UI/SeatDisplay.cs` (heading, seat words, layout,
  "not available" alert) and `Features/Shared/_SeatGrid.cshtml` (the seats drawn deck by deck).

**Text provenance:**

| Words | Source |
|---|---|
| "Seats: {route}, {departure}" / "Seats: trip leaving {departure}" (departure in IST) | seats area shared rule 1 |
| "Seats" (browser title when no seats are shown) | the "Seats" link text that leads here (my-trips, trip-search) |
| "Lower deck", "Upper deck" | `msg:deck.lower`, `msg:deck.upper` |
| Seat class, fare "₹1,200" | seats area shared rule 3 |
| Available / On hold / Booked / Blocked | `msg:state.available`, `msg:state.held`, `msg:state.booked`, `msg:state.blocked` |
| "{available} of {total} seats available." | business rule 4 |
| "This trip was cancelled. Its seats will not be sold." | `msg:notice.trip_cancelled` |
| "This trip has left." | `msg:notice.trip_departed` |
| "Seat details for this trip are not available. …", "Try again", "Go to Home" (to `/`) | `msg:problem.seat_details_unavailable`, `msg:link.try_again`, `msg:access.go_home` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |

## Contract
- URL: `/trips/{tripId}/seats` (absolute `@page`, fixed by the product), optional `?route=` label — page name
  `/Seats/PublicSeatMap/PublicSeatMap`
- Auth: **everyone** — visitors and every signed-in role (`PageAccessLevel.Everyone`; not visitor-only).
- Seat Inventory call **without** the person's access token (the request is not marked `WithUserToken`).
- The trip is not constrained to a GUID in the route: an address that names no trip shows "Seat details … not
  available" (same as Seat Inventory's `VALIDATION_FAILED`) **without calling Seat Inventory**, so nothing typed
  into the address is ever put into a Seat Inventory path.
- View only: no seat can be picked, no booking action.

## Form fields

None — a read-only page.

## Screen states

1. **Seat map** — heading; "{a} of {t} seats available."; decks with each seat's number, class, fare and status.
2. **Cancelled trip / departed trip** — the notice above the seats, no "seats available" line, seats still shown.
3. **Seat details unavailable** — `NOT_FOUND`, `VALIDATION_FAILED` or an address naming no trip: the message
   with "Try again" (same address) and "Go to Home".
4. **Service unavailable / server error** — the message; the role menu keeps working.

## Acceptance criteria

- [x] AC-1: a visitor opens the seat map (heading, decks, seat words, counts, no token, nothing to pick) — `Get_Visitor_ShowsSeatsDeckByDeckWithoutToken`
- [x] AC-2: signed-in people see the same page, no token, no booking ID or hold end — `Get_SignedIn_ShowsSamePageWithoutToken` (Customer, Operator, Admin)
- [x] AC-3: no route in the address — `Get_NoRouteInAddress_HeadingNamesDepartureOnly`
- [x] AC-4: cancelled trip — `Get_CancelledTrip_ShowsNoticeAndNoAvailableLine`
- [x] AC-5: departed trip — `Get_DepartedTrip_ShowsNoticeAndNoAvailableLine`
- [x] AC-6: seat details not available (`NOT_FOUND`; `VALIDATION_FAILED`) — `Get_UnknownTrip_ShowsUnavailableWithTryAgainAndGoHome`, `Get_SeatInventoryRefusesTripIdentifier_ShowsUnavailable`, `Get_AddressNamesNoTrip_ShowsUnavailableWithoutCallingSeatInventory`
- [x] AC-7: Seat Inventory is down — `Get_SeatInventoryUnreachable_ShowsUpstreamMessage`
- [x] Home-and-navigation AC-12 (everyone opens `/trips/{tripId}/seats`) — page access table row in `Foundation/SessionRulesTests.cs`, plus AC-1/AC-2 above

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getSeatMap` | `routego-seat-inventory:CAP-SEAT-001@0.1.0` | `GET /trips/{tripId}/seats` (no token) → 200 SeatMap | The page | `NOT_FOUND`, `VALIDATION_FAILED` |

Wire shape from RouteGo.SeatInventory.Api `Features/TripSeats/GetSeatMap` (built): camelCase string enums
(`deck` lower/upper, `status` available/held/booked/blocked). Tested against `FakeBackend` only; not live-tested.

## Spec 0.6.0 addition — "Book these seats" (AC-8, AC-9)
- A visitor or a signed-in Customer is offered **Book these seats** (to `/trips/{tripId}/book`, passing `route` on) when the trip is neither cancelled nor departed and has
  at least one Available seat. Operators and admins never see it. Tested in `PublicSeatMap.Book.Tests.cs`. Words: "Book these seats" (spec).
