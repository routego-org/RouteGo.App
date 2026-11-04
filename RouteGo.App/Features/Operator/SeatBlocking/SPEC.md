# CAP-UI-SEAT-002 — Operator trip seats

- Capability: CAP-UI-SEAT-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.3.0) → `capabilities/seats/features/operator-trip-seats.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/SeatBlocking/` — `SeatBlocking.Handler.cs` (wire records, handler), `SeatBlocking.Refusals.cs`
  (pick checks, refusal words), `SeatBlocking.View.cs` (how a seat is drawn and picked), `SeatBlocking.cshtml.cs`
  (page model); tests in `SeatBlocking.Tests.cs` (page states), `SeatBlocking.Block.Tests.cs`, `SeatBlocking.Unblock.Tests.cs`
- Folder name: not `TripSeats`, because the architecture test treats a slice whose namespace starts (ignoring case)
  with another's — `Operator.Trips` — as part of it (user's choice 2026-10-21: rename, not change the test).
- Shared: `Common/UI/SeatDisplay.cs` and `Features/Shared/_SeatGrid.cshtml` (see the public seat map's SPEC.md).

**Text provenance:**

| Words | Source |
|---|---|
| "Seats: {route}, {departure}" / "Seats: trip leaving {departure}"; "Seats" (browser title when no seats are shown) | seats area shared rule 1; the "Seats" link text |
| "Lower deck", "Upper deck"; Available / On hold / Booked / Blocked | `msg:deck.*`, `msg:state.*` |
| "Blocked by: Operator" / "Blocked by: Trip cancelled" | `label.trip_seat.block_reason`, `msg:block_reason.operator`, `msg:block_reason.trip_cancelled` |
| "Available: {a} · On hold: {h} · Booked: {b} · Blocked: {k}" | business rule 2 |
| "See the public seat map", "Back to my trips" (to `/operator/trips`) | `msg:link.public_seat_map`, UI state "Seats" |
| "Block seats", "Unblock seats", "Go back" | UI states |
| "This trip was cancelled. Its seats will not be sold." / "This trip has left." + "Seats can no longer be blocked or unblocked." | `msg:notice.*`, UI states "Cancelled trip" / "Departed trip" |
| "Pick at least one available seat to block." / "Pick at least one seat you blocked to unblock." / "You can block or unblock at most 60 seats at a time." | UI states |
| "Put {seats} back on sale? Customers can book {it\|them} straight away." | UI state "Confirm unblock" |
| "Seats blocked: {seats}. Customers cannot book them." / "These seats were already blocked. Nothing was changed." / "Seats unblocked: {seats}. They are back on sale." | UI states |
| "Some seats are no longer free: {seats}. …" / "These seats are no longer blocked by you: {seats}. …" / "Some seats are not on this trip: {seats}. …" | UI states |
| "This trip was cancelled. Nothing was changed." / "This trip has already left. Nothing was changed." | `msg:problem.trip_cancelled`, `msg:problem.trip_departed` |
| "Seat details for this trip are not available. …", "Try again" | `msg:problem.seat_details_unavailable`, `msg:link.try_again` |
| "You don't have access to this." / upstream / internal error / no permission | `msg:problem.forbidden`, `msg:problem.upstream`, `msg:problem.internal_error`, `msg:access.no_permission` |

## Contract
- URL: `/operator/trips/{tripId}/seats` (absolute `@page`, fixed by the product), optional `?route=` label — page name
  `/Operator/SeatBlocking/SeatBlocking`. Steps: `?handler=Block`, `?handler=Unblock`, `?handler=Back`.
- Auth: **Operator only** (every `/Operator/` page except Apply/Reapply). Every Seat Inventory call carries the
  operator's access token (`WithUserToken`).
- The trip is not constrained to a GUID: an address that names no trip shows "Seat details … not available"
  **without** calling Seat Inventory.
- **No page script.** Seats are picked with tick boxes in one form. A ticked seat is posted as `AvailablePicks`
  when it was Available as shown, or `BlockedPicks` when the operator had blocked it as shown — so "Block seats"
  sends only the first and "Unblock seats" only the second, judged by the page the operator saw (a seat taken
  meanwhile is still sent, and Seat Inventory's refusal names it — AC-10).
- Unblock asks first, inside the same form: the question carries the seats it names (`ConfirmSeatNos`,
  `Confirmed=true`); "Go back" (`?handler=Back`) redraws the page with the seats still picked.
- Success → PRG redirect to the page (same route label) with the flash; the seats are loaded again by that GET.
  Refusal → the page drawn again (200) with the seats loaded again and nothing picked, except `NOT_FOUND`
  (not available) and `FORBIDDEN` (no seats shown).
- `DELETE /trips/{tripId}/seatBlocks?seatNos=L1,L2` — Seat Inventory takes the seats as one comma-separated value.

## Form fields

| Field | Posted as | Check before sending |
|---|---|---|
| Ticked Available seats | `AvailablePicks` (repeated) | at least one for Block; at most 60 |
| Ticked seats blocked by the operator | `BlockedPicks` (repeated) | at least one for Unblock; at most 60 |
| Seats named by the unblock question | `ConfirmSeatNos` (repeated, hidden) + `Confirmed` | as above |

## Screen states

1. **Seats** — heading, counts, decks; Available seats and the operator's blocks can be ticked; Block / Unblock.
2. **Cancelled / departed trip** — notice + "Seats can no longer be blocked or unblocked."; nothing to tick, no actions.
3. **Nothing to block / unblock, too many seats** — message, nothing sent, picks kept.
4. **Confirm unblock** — the question with "Unblock seats" and "Go back".
5. **Success** — flash after redirect.
6. **Refusals** — the spec's words above the seats loaded again; `VALIDATION_FAILED` shows the problem's title.
7. **Seat details unavailable / not allowed / service unavailable / server error / no permission.**

## Acceptance criteria

- [x] AC-1: open the seats (heading, decks, Blocked by, counts, links, token) — `Get_OwnTrip_ShowsSeatsCountsAndLinksWithToken`
- [x] AC-2: no booking IDs or hold end times — `Get_OwnTrip_ShowsNoBookingIdsOrHoldEndTimes`
- [x] AC-3: only free seats and own blocks can be picked — `Get_OwnTrip_OnlyFreeSeatsAndOwnBlocksCanBePicked`
- [x] AC-4: block seats, without asking — `Block_AvailableSeats_BlocksWithoutAskingAndSaysSo`
- [x] AC-5: unblock after confirming — `Unblock_OwnBlock_AsksFirstAndSendsNothing`, `Unblock_Confirmed_UnblocksAndSaysSo`
- [x] AC-6: going back changes nothing — `Unblock_GoBack_SendsNothingAndKeepsSeatsPicked`
- [x] AC-7: each action uses only the seats it can change — `Block_FreeSeatAndOwnBlockPicked_SendsOnlyTheFreeSeat`, `Unblock_FreeSeatAndOwnBlockPicked_AsksAboutAndSendsOnlyOwnBlock`
- [x] AC-8: nothing suitable picked — `Block_NoAvailableSeatPicked_SaysSoAndSendsNothing`, `Unblock_NoOwnBlockPicked_SaysSoAndSendsNothing`
- [x] AC-9: at most 60 seats — `Block_MoreThanSixtySeats_SaysSoAndSendsNothing`
- [x] AC-10: a seat was taken meanwhile — `Block_SeatTakenMeanwhile_NamesItLoadsSeatsAgainAndUnpicks`
- [x] AC-11: a seat is no longer his block — `Unblock_SeatNoLongerOwnBlock_NamesItAndLoadsSeatsAgain`
- [x] AC-12: already done — `Block_SeatsAlreadyBlocked_SaysNothingChanged`
- [x] AC-13: seats not on the trip — `Block_SeatNotOnTrip_NamesItAndLoadsSeatsAgain`
- [x] AC-14: cancelled or departed trip — `Get_CancelledOrDepartedTrip_ShowsNoticeAndNothingToPick`
- [x] AC-15: cancelled or left meanwhile — `Block_TripCancelledOrLeftMeanwhile_SaysSoAndLoadsSeatsAgain`
- [x] AC-16: another company's trip — `Get_AnotherCompanysTrip_ShowsForbiddenAndNoSeats`
- [x] AC-17: seat details not available — `Get_UnknownTrip_ShowsUnavailableWithTryAgainAndBackToMyTrips`, `Get_AddressNamesNoTrip_ShowsUnavailableWithoutCallingSeatInventory`, `Block_TripNoLongerKnown_ShowsUnavailable`
- [x] AC-18: only operators — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingSeatInventory` (Customer, Admin)
- [x] UI state "Request refused" — `Block_RequestRefused_ShowsTitleAndLoadsSeatsAgain`; "Service unavailable" — `Get_SeatInventoryUnreachable_ShowsUpstreamMessage`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listSeatAllocations` | `routego-seat-inventory:CAP-SEAT-002@0.1.0` | `GET /trips/{tripId}/seatAllocations` → 200 | The seats | `VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND` (`UNAUTHORIZED` ends the session) |
| `createSeatBlock` | `routego-seat-inventory:CAP-SEAT-003@0.1.0` | `POST /trips/{tripId}/seatBlocks` `{seatNos}` → 201 / 200 (nothing changed: `blockedSeatNos` empty) | Block seats | `VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND`, `UNKNOWN_SEATS`, `SEATS_UNAVAILABLE`, `TRIP_CANCELLED`, `TRIP_DEPARTED` |
| `deleteSeatBlock` | `routego-seat-inventory:CAP-SEAT-004@0.1.0` | `DELETE /trips/{tripId}/seatBlocks?seatNos=…` → 204 | Unblock seats | `VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND`, `UNKNOWN_SEATS`, `SEATS_NOT_OPERATOR_BLOCKED`, `TRIP_CANCELLED`, `TRIP_DEPARTED` |

Wire shapes from RouteGo.SeatInventory.Api `Features/SeatBlocking/*` (built). Booking IDs and hold end times are in
the answer but not read. Tested against `FakeBackend` only; not live-tested.
