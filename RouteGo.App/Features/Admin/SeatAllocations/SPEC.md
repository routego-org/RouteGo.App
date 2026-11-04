# CAP-UI-ADM-008 — Admin trip seats

- Capability: CAP-UI-ADM-008
- Canonical spec: `spec-RouteGo.App` (routego-app 0.3.0) → `capabilities/administration/features/admin-trip-seats.md` (feature spec 0.1.0)
- Area: Admin
- Slice: `Features/Admin/SeatAllocations/` — `SeatAllocations.Handler.cs` (wire records, handler), `SeatAllocations.Holds.cs`
  (stuck-hold rule and words), `SeatAllocations.Input.cs` (cancel form + validator), `SeatAllocations.cshtml.cs` (page
  model); tests in `SeatAllocations.Tests.cs` (page states) and `SeatAllocations.Cancel.Tests.cs`
- Folder name: not `TripSeats`, because the architecture test treats a slice whose namespace starts (ignoring case)
  with another's — `Admin.Trips` — as part of it (user's choice 2026-10-21: rename, not change the test).
- Shared: `Common/UI/SeatDisplay.cs` and `Features/Shared/_SeatGrid.cshtml` (see the public seat map's SPEC.md).

**Text provenance:**

| Words | Source |
|---|---|
| "Seats: {route}, {departure}" / "Seats: trip leaving {departure}"; "Seats" (browser title when no seats are shown) | seats area shared rule 1; the "Seats" link text |
| "Lower deck", "Upper deck"; Available / On hold / Booked / Blocked | `msg:deck.*`, `msg:state.*` |
| "Booking ID: …", "Hold ends at: {IST}", "Blocked by: Operator / Trip cancelled" | `label.trip_seat.booking_id`, `label.trip_seat.hold_expires_at`, `label.trip_seat.block_reason`, `msg:block_reason.*` |
| "Available: {a} · On hold: {h} · Booked: {b} · Blocked: {k}" | business rule 2 |
| "{n} stuck hold on this trip." / "{n} stuck holds on this trip."; "Stuck: hold time ended at {hold_expires_at}." | business rule 3 |
| "Cancel hold", "Go back", "Reason" | UI states, `label.hold_cancellation.note` |
| "Cancel the hold of booking {booking_id} on seats {seats}? {outcome} This cannot be undone." with the three outcomes | UI state "Confirm cancel" |
| "Hold cancelled. Seats {seats} are back on sale." / "…are now blocked because the trip was cancelled." / "…are free again." | UI state "Success — hold cancelled" |
| "This booking no longer holds any seats on this trip. Nothing was changed." / "The customer has paid for seats {seats}. …" | UI states |
| "Reason must be between 10 and 500 characters." | `msg:validation.reason_length` |
| "See the public seat map", "Back to trips" (to `/admin/trips`) | `msg:link.public_seat_map`, UI state "Seats" |
| notices, not available + "Try again", forbidden, upstream, internal error, no permission | `msg:notice.*`, `msg:problem.*`, `msg:link.try_again`, `msg:access.no_permission` |

## Contract
- URL: `/admin/trips/{tripId}/seats` (absolute `@page`, fixed by the product), optional `?route=` label — page name
  `/Admin/SeatAllocations/SeatAllocations`. The cancel question opens with `?cancel={bookingId}`; the form posts to
  `?handler=Cancel`.
- Auth: **Admin only** (every `/Admin/` page). Every Seat Inventory call carries the admin's access token.
- The trip is not constrained to a GUID: an address that names no trip shows "Seat details … not available"
  **without** calling Seat Inventory.
- **Stuck hold** (portal clock, `IClock`): a seat On hold whose hold end is **more than 5 minutes** before now.
  "Cancel hold" is offered once per stuck booking, in a list above the seats ("Booking ID: … 3A, 3B Cancel hold");
  a `?cancel=` for a hold that is not stuck opens nothing.
- The cancel post loads the seats first (for the trip's state that decides the wording), checks the reason (trimmed,
  10–500), then sends the trimmed reason as `note`. It does **not** refuse locally when the hold stopped being stuck
  meanwhile — Seat Inventory answers `HOLD_NOT_ACTIVE` (AC-5).
- Success → PRG redirect to the page (same route label, no question) with the flash. Refusal → the page drawn
  again: the reason refused (page check or Seat Inventory naming `note`) or Seat Inventory out of reach keeps the
  question open with the typed reason; `NOT_FOUND` → not available; `FORBIDDEN` → no seats; anything else → the
  message above the seats loaded again.
- Known limit: if Seat Inventory is out of reach when the post loads the seats first, the page shows only the
  upstream message and the typed reason is not kept.

## Form fields

| Field | Label | Check before sending |
|---|---|---|
| `CancelInput.BookingId` (hidden) | — | — |
| `CancelInput.Reason` | Reason | 10–500 characters after trimming → `msg:validation.reason_length` |

## Screen states

1. **Seats** — heading, notice when cancelled/departed, counts, decks with Booking ID / Hold ends at / Blocked by.
2. **Stuck holds** — count line, stuck marks on their seats, "Cancel hold" once per stuck booking.
3. **Confirm cancel** — question with outcome, Reason, "Cancel hold", "Go back".
4. **Reason invalid** — message at Reason, question open with the typed reason.
5. **Success / hold ended meanwhile / customer paid meanwhile.**
6. **Seat details unavailable / not allowed / service unavailable / server error / no permission.**

## Acceptance criteria

- [x] AC-1: open the seats of any trip — `Get_AnyTrip_ShowsSeatsWithBookingIdsHoldEndsAndLinks`
- [x] AC-2: stuck holds stand out (not running, not ended ≤ 5 min) — `Get_StuckHold_StandsOutWithOneCancelHold`, `Get_NoStuckHold_ShowsNoStuckLine`, `Get_CancelLinkForRunningHold_AsksNothing`
- [x] AC-3: cancel a stuck hold — `Get_CancelHoldLink_AsksNamingBookingSeatsAndOutcome`, `Post_ValidReason_CancelsHoldAndSaysWhatHappened`
- [x] AC-4: reason length — `Post_ReasonTooShort_KeepsQuestionWithReasonAndSendsNothing`, `Post_SeatInventoryRefusesReason_KeepsQuestionWithReasonMessage`
- [x] AC-5: the hold ended meanwhile — `Post_HoldEndedMeanwhile_SaysNothingChangedAndLoadsSeatsAgain`
- [x] AC-6: the customer paid meanwhile — `Post_CustomerPaidMeanwhile_NamesSeatsAndLoadsSeatsAgain`
- [x] AC-7: stuck hold on a cancelled trip (question + flash) — the `true, false` cases of the two theories above
- [x] AC-8: stuck hold on a departed trip (question + flash) — the `false, true` cases of the two theories above
- [x] AC-9: seat details not available — `Get_UnknownTrip_ShowsUnavailableWithTryAgainAndBackToTrips`, `Post_TripNoLongerKnown_ShowsUnavailable`
- [x] AC-10: only admins — `Get_Operator_ShowsNoPermissionWithoutCallingSeatInventory`
- [x] UI state "Service unavailable" keeps the typed reason — `Post_SeatInventoryUnreachable_KeepsQuestionWithTypedReason`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listSeatAllocations` | `routego-seat-inventory:CAP-SEAT-002@0.1.0` | `GET /trips/{tripId}/seatAllocations` → 200 | The seats | `VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND` (`UNAUTHORIZED` ends the session) |
| `cancelHold` | `routego-seat-inventory:CAP-HOLD-004@0.1.0` | `POST /trips/{tripId}/holds/{bookingId}/cancel` `{note}` → 200 `{releasedSeatNos}` | Cancel hold | `VALIDATION_FAILED` (`note`), `FORBIDDEN`, `NOT_FOUND`, `HOLD_NOT_ACTIVE`, `HOLD_ALREADY_CONFIRMED` |

Wire shapes from RouteGo.SeatInventory.Api `Features/TripSeats/ListSeatAllocations` and `Features/Holds/CancelHold`
(built). Tested against `FakeBackend` only; not live-tested.
