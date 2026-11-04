# CAP-UI-TRP-004 — My trips: change fares, cancel

- Capability: CAP-UI-TRP-004
- Canonical spec: `spec-RouteGo.App` (routego-app 0.3.0) → `capabilities/trips/features/my-trips.md` (feature spec 0.2.0)
- Area: Operator
- Slice: `Features/Operator/Trips/` — `Trips.Handler.cs` (query, wire records, handler), `Trips.Input.cs` (fare and
  cancel inputs, checks), `Trips.Actions.cs` (dates, which actions a trip offers, refusal words), `Trips.cshtml.cs`
  (page model), partials `_FareForm.cshtml` and `_CancelForm.cshtml`; tests in `Trips.Tests.cs` (list) and
  `Trips.Actions.Tests.cs` (fare change, cancel)

**Text provenance:**

| Words | Source |
|---|---|
| "My trips", "Show", "All", "Showing {from}–{to} of {total} trips" | UI state "List" |
| From, To, Travel date, Departs, Arrives, Cancellation reason | `label.trip.from`, `label.trip.to`, `label.trip.service_date`, `label.trip.departure_at`, `label.trip.arrival_at`, `label.trip.cancel_reason` |
| Route, Bus, Status, Fares, Cancelled at | `label.route.name`, `label.bus.name` (shown "Bus"), derived `status`, `label.schedule_class_fare.fare`, derived `cancelled_at` |
| Scheduled / Departed / Cancelled | `state.scheduled`, `state.departed`, `state.cancelled` |
| "Mumbai → Pune" | AC-1 ("{first city} → {last city}") |
| "3 Oct 2026", "10:30 PM IST", "4 Oct 2026, 2:00 AM IST" | design system formats (`IndiaTime`); an arrival on a later day shows its date too (portal choice, so a night trip's arrival is not misread) |
| "Showing the trips of one schedule.", "Show all trips" | UI state "List" |
| "Change fares", "Cancel trip", "Fares are frozen (less than 6 hours to departure)." | business rule 4 |
| "No trips match the current filters." | `msg:empty.filtered` ({items} "trips") |
| "Choose a To date on or after the From date, at most 31 days apart." | `msg:validation.date_range` |
| "Change fares — {route} on {travel date} at {departs}", "{seat_class} (₹)", "Save fares", "Cancel" | UI state "Fare form" |
| `msg:validation.fare` | UI state "Fare invalid" |
| "The fares are the same as before. Nothing was changed." | UI state "Fares unchanged" |
| "Cancel the trip {route} on {travel date} at {departs}? Customers who booked it will get a refund. You cannot undo this.", "Reason", "Cancel trip", "Go back" | UI state "Confirm cancel" |
| `msg:validation.reason_length` | UI state "Reason invalid" |
| "Fares changed. New bookings pay the new fares." / "Trip cancelled." | UI states "Success — fares changed" / "Success — cancelled" |
| "Fares are frozen 6 hours before departure. This trip's fares froze at {cutoff_at}." | UI state "Cutoff passed" |
| `msg:problem.trip_departed`, `msg:problem.trip_not_scheduled` ({current_status} = the state word in lower case), `msg:problem.class_fares_mismatch`, `msg:problem.not_found` ({entity} "trip") | UI states |
| `msg:problem.operator_profile_not_ready`, `msg:problem.forbidden`, `msg:problem.upstream`, `msg:problem.internal_error` | UI states |

## Contract
- URL: `/operator/trips` (by convention) — page name `/Operator/Trips/Trips`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- Address: `?from=&to=&status=&busId=&scheduleId=&pageNumber=`. Empty dates = today (IST, portal clock) through
  today + 13; Fleet always gets `from` and `to`. A date that cannot be read, To before From, or more than 31 days
  (both ends counted) → the range message, no `listTrips` call.
- `listBuses` (`page=1&pageSize=200`) for the Bus filter on every draw; refused → no Bus filter, list still works.
- Fare form: `?fares={tripId}` opens it above the list for a listed trip that can change fares; posts
  `?handler=Fares` with the list's filters in the address. Cancel: `?cancel={tripId}` opens the question for a
  listed scheduled trip; posts `?handler=Cancel`.
- Every post reads the list again first (the posted trip must still be listed, else not found). Success redirects
  to the same list (PRG) with the flash. A refusal about the trip's state (cutoff passed, departed, not scheduled,
  fares mismatch, not found) closes the form and reads the list again; Fleet down or failing keeps the form open
  with what was typed.

## Form fields

| Field | Type | Required | Validation message |
|---|---|---|---|
| FaresInput.Fares[i].Fare (+ hidden SeatClass, TripId) | text, whole rupees 50–10,000 | each | `msg:validation.fare` |
| CancelInput.Reason (+ hidden TripId) | text, 10–500 characters (trimmed) | yes | `msg:validation.reason_length` |

## Screen states

1. **List (GET)** — filters, the one-schedule note, table with each trip's actions (rule 4), showing line, pages.
2. **Empty** — `msg:empty.filtered`.
3. **Date range invalid** — the message under the filters; no list.
4. **Fare form** / **Confirm cancel** — above the list.
5. **Fare invalid / fares unchanged / reason invalid** — as above; nothing sent.
6. **Success** — the same list with the flash.
7. **Trip moved on** (cutoff, departed, not scheduled, mismatch, not found) — the message; form closed; list read again.
8. **Profile not ready / not allowed** — the message instead of the content (FORBIDDEN on an action: the message only).
9. **Service unavailable / server error** — the message; an open form keeps what was typed.

## Acceptance criteria

- [x] AC-1: default list — `Get_Default_ListsTwoWeeksOfTripsWithDetails`
- [x] AC-2: only actions that work — `Get_TripsInEachState_OfferOnlyActionsThatWork`
- [x] AC-3: filters — `Get_FiltersChosen_SendsThemAndKeepsThemInAddress`, `Get_ScheduleIdFromSchedulePage_ListsThatScheduleWithWayBack`
- [x] AC-4: date range — `Get_DateRangeNotAllowed_ShowsRangeMessageWithoutListing`, `Get_FleetRefusesDates_ShowsRangeMessage`
- [x] AC-5: change fares — `GetFares_OpenTrip_OpensFormWithCurrentFares`, `PostFares_FaresChanged_SendsThemAndShowsListWithConfirmation`
- [x] AC-6: same fares — `PostFares_SameAsBefore_SendsNothingAndSaysSo`
- [x] Fare invalid — `PostFares_FareNotAllowed_ShowsFareMessageKeepsTyped`
- [x] AC-7: cutoff passed — `PostFares_CutoffPassed_ShowsWhenFaresFrozeAndListAgain`
- [x] AC-8: cancel — `GetCancel_ScheduledTrip_AsksWithTripAndReasonAndSendsNothing`, `PostCancel_ReasonGiven_CancelsAndShowsConfirmation`
- [x] AC-9: reason length — `PostCancel_ReasonTooShort_ShowsLengthMessageKeepsQuestion`, `PostCancel_FleetRefusesReason_ShowsLengthMessageKeepsQuestion`
- [x] AC-10: departed / not scheduled — `PostCancel_TripMovedOn_ShowsWhyAndListAgain`
- [x] AC-11: every trip (scheduled, departed, cancelled) links to its seats, "Seats" → `/operator/trips/{tripId}/seats?route={route}` — `Get_EveryTrip_LinksToItsSeatsWithRouteName`, `Get_TripsInEachState_OfferOnlyActionsThatWork`
- [x] Empty — `Get_NoTrips_ShowsFilteredEmptyMessage`; Bus filter not offered — `Get_BusesNotGiven_ListsTripsWithoutBusFilter`
- [x] Not ready / not allowed — `Get_FleetWillNotList_ShowsMessageInsteadOfContent`
- [x] Service unavailable — `PostFares_FleetUnreachable_KeepsFormWithTypedFares`
- [x] Operator only — `Get_Customer_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listTrips` | `routego-fleet:CAP-TRP-008@0.1.0` | `GET /trips?from&to&scheduleId&busId&status&page&pageSize=20` → 200 TripListResponse | The list | `VALIDATION_FAILED` (from/to → range message; other → title, default list), `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `replaceTripFares` | `routego-fleet:CAP-TRP-009@0.1.0` | `PUT /trips/{tripId}/fares` `{classFares}` → 200 Trip | Change fares | `VALIDATION_FAILED`, `FARE_CHANGE_CUTOFF_PASSED`, `TRIP_DEPARTED`, `TRIP_NOT_SCHEDULED`, `CLASS_FARES_MISMATCH`, `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `cancelTrip` | `routego-fleet:CAP-TRP-010@0.1.0` | `POST /trips/{tripId}/cancel` `{reason}` → 200 Trip | Cancel | `VALIDATION_FAILED` (reason), `TRIP_DEPARTED`, `TRIP_NOT_SCHEDULED`, `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `listBuses` | `routego-fleet:CAP-BUS-002@0.1.0` | `GET /buses?page=1&pageSize=200` | Bus filter | any refusal → no Bus filter |

All calls carry the operator's access token. Wire shapes from Fleet's contract (`contracts/trips/trip.yaml`);
not built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
