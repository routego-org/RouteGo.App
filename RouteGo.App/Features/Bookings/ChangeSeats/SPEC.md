# CAP-UI-BKG-005 — Change seats

- Capability: CAP-UI-BKG-005
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/change-seats.md` (feature spec 0.1.0)
- Area: Bookings
- Slice: `Features/Bookings/ChangeSeats/` — `ChangeSeats.Handler.cs` (wire records, load, send), `ChangeSeats.Choices.cs` (which seats are offered, checks), `ChangeSeats.Refusals.cs` (the words for every stop), `ChangeSeats.cshtml.cs` (page model), tests in `ChangeSeats.Tests.cs`
- Shared with the other booking pages: `Common/UI/BookingDisplay.cs`, `Common/UI/BookingProblems.cs`, `Common/UI/BookingModels.cs`, `Common/UI/SeatDisplay.cs`.

**Text provenance:**

| Words | Source |
|---|---|
| "Change seats: {route}, {departure}" | UI states, Choices |
| "Passengers move with their seats. Fares stay the same. No money is charged or refunded." | UI states, Choices |
| "Seat changes: {n} of 3 used" | `msg:label.booking.seat_change_count` |
| "Change seats", `msg:link.view_booking`, `msg:link.view_ticket` | UI states |
| "Choose a new seat for at least one of your seats." | UI states, Nothing chosen |
| "Your seats were changed: {moves}. Your ticket is being updated and will be sent to you again." | UI states, Success |
| "{seats} was/were just taken. Nothing was changed. We loaded the seats again.", class mismatch, "Some seats do not match…", both service failures | UI states |
| "You have used all {n} seat changes for this booking." (+ " Nothing was changed.") | UI states, Limit used |
| booking closed / trip left / trip cancelled / not confirmed / seat details unavailable / not found / forbidden / upstream | `msg:problem.*` |
| **"Keep this seat"** (the empty choice), **"New seat"** (choice label), **"Seats changed"** (title after a change), **"Choose a different new seat for each of your seats."** | not in the spec — **new wording, add to change-seats.md** |

## Contract
- URL: `/my-bookings/{bookingId}/change-seats` (absolute `@page`, fixed). An address that is no booking identifier shows "couldn't find that booking" without any call.
- Auth: **customers only** (`/Bookings/` prefix). Booking's owner check answers `FORBIDDEN`.
- Load: `getBooking` with the token; only for a confirmed booking, whose trip is at least 30 minutes away, with fewer than 3 changes used, then the trip's
  public seat map (`getSeatMap`, no token). Otherwise the matching message and no seat map call. The portal's 30 minutes only decides whether choices
  are offered (`BookingDisplay.CutoffMinutes`); Booking still decides and the refusal's own departure and cutoff are what a refusal shows.
- Offered seats per booking seat: Available, same seat class, not the booking's own, not already picked for another seat. A pick that is no longer on offer is dropped on redraw.
- `?handler=Change` (POST) sends 1 to 6 moves once (never retried). Success redirects to `…?changed=true`; the moves ride one redirect in TempData.
  After a refusal the booking and seats are loaded again and the refusal's words win over any state the reload found.
- `wwwroot/js/change-seats.js` leaves a picked seat out of the other choices while picking (script-only; not covered by the .NET tests — the server rule on redraw is).

## Form fields
`Moves[{seat number}]` — a select per booking seat (a free seat of its class, or empty = keep this seat).

## Screen states
1. **Choices**, **Success**, **Nothing chosen / same seat twice**.
2. **Seat taken**, **Class mismatch**, **Seats do not match**, **Limit used**, **Too late**, **Trip left**, **Trip cancelled**, **Not confirmed**.
3. **Change failed — service / slow**, **Seat details unavailable**, **Not found**, **Forbidden**, **Service unavailable**.

## Acceptance criteria

- [x] AC-1: only the moves that can work — `Get_Confirmed_OffersOnlyFreeSeatsOfTheSameClassAndSaysNoMoneyMoves`
- [x] AC-2: a seat picked for one seat is not offered for another — `Get_AfterARefusal_KeepsStillValidPicksAndLeavesThemOutOfTheOtherSeatsChoices` (live filtering is script-only)
- [x] AC-3, AC-4: move one seat, both — `Change_OneSeat_…`, `Change_BothSeats_SendsBothMoves`
- [x] AC-5: a change already done — `Change_WhenEveryMoveWasAlreadyDone_ShowsTheChosenMovesAsSuccess`
- [x] AC-6: nothing chosen — `Change_NothingChosen_AsksForAChoiceAndSendsNothing`
- [x] AC-7: a new seat taken — `Change_WhenANewSeatWasTakenMeanwhile_NamesItLoadsAgainAndKeepsTheOtherPick`
- [x] AC-8: class mismatch, seats not matching — `Change_Refused_ShowsTheWordsAndLoadsBookingAndSeatsAgain`
- [x] AC-9: all 3 changes used — `Get_AllChangesUsed_SaysSoWithoutCallingSeatInventory`, `Change_LimitReachedFromAnOlderPage_SaysNothingWasChanged`
- [x] AC-10: too close to departure — `Get_LessThanThirtyMinutesBeforeDeparture_…`, `Change_BookingClosedMeanwhile_…`
- [x] AC-11: trip left or cancelled — `Get_TripHasLeft_…`, `Change_TripCancelledMeanwhile_ShowsTripCancelled`
- [x] AC-12: only a confirmed booking — `Get_BookingNotConfirmed_NamesItsStatusWithoutCallingSeatInventory`
- [x] AC-13: Booking could not move the seats — the two service words in `Change_Refused_…`
- [x] AC-14: visitor, wrong role — `Get_Visitor_…`, `Get_OperatorOrAdmin_…`
- [x] AC-15: another customer's booking, unknown booking — `Get_AnotherCustomersBooking_…`, `Get_UnknownBookingOrNotAnIdentifier_…`
- [x] AC-16: seat map or Booking down — `Get_SeatInventoryOrBookingDown_…`, `Change_WhenBookingCannotBeReached_ShowsUpstreamMessageAndSendsTheChangeOnce`, `Get_SeatInventoryDoesNotKnowTheTrip_…`

Booking's own `UPSTREAM_UNAVAILABLE` / `UPSTREAM_TIMEOUT` (Seat Inventory was down) is told apart from the portal being unable to reach Booking by the
`unreachable` marker the portal's `UpstreamFailureHandler` puts on its own outage answers (`BookingProblems.IsUnreachable`).

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBooking` | `routego-booking:CAP-BKG-002@0.1.3` | `GET /bookings/{bookingId}` → 200 Booking | State, seats, departure, changes used | `NOT_FOUND`, `FORBIDDEN` |
| `getSeatMap` | `routego-seat-inventory:CAP-SEAT-001@0.1.0` | `GET /trips/{tripId}/seats` (no token) → 200 | The free seats | `NOT_FOUND`, `VALIDATION_FAILED` |
| `createSeatChange` | `routego-booking:CAP-BKG-005@0.1.3` | `POST /bookings/{bookingId}/seatChanges` → 201 / 200 | The change | all codes of the spec's API surface |

Wire shapes from Booking's `contracts/bookings/seat-change.yaml`. Tested against `FakeBackend` only; not live-tested.
