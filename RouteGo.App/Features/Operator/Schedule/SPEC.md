# CAP-UI-TRP-003 — Schedule page: put trips on sale, stop, restart

- Capability: CAP-UI-TRP-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/trips/features/my-schedules.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/Schedule/` — `Schedule.Input.cs` (input, date check), `Schedule.cshtml.cs` (page model), `Schedule.Handler.cs`
  (wire records, handler), tests in `Schedule.Tests.cs` (showing) and `Schedule.Actions.Tests.cs` (actions)

**Text provenance:**

| Words | Source |
|---|---|
| "{route name}, {departure time}" (page title), "Mumbai → Pune via Lonavala, 10:30 PM IST" | trips area shared rule 2 (`TripDisplay.ScheduleName`) |
| "{city name} — {time from start}" | UI state "Schedule page"; durations by `FleetDisplay.Duration` |
| Route, Bus, Departure time, Status, Created at, Updated at | `label.route.name`, `label.bus.name` (shown "Bus"), derived from Fleet's `departure_time`, `status`, `created_at`, `updated_at` |
| Days, Trips on sale until, Fares | `label.schedule.days_of_week`, `label.schedule.trips_generated_through`, `label.schedule_class_fare.fare` |
| "Monday, Wednesday, Friday" / "Every day" | `day.*`, trips area shared rule 3 |
| "Lower berth: ₹1,200, Upper berth: ₹1,000" | trips area shared rule 4 (bus's seat-class order) |
| "10:30 PM IST", "16 Oct 2026", "1 Oct 2026, 10:00 AM IST" | design system formats (`TripDisplay.DepartureTime`, `IndiaTime`) |
| "Unknown route" / "Unknown bus" | trips area shared rule 2 |
| Active / Inactive | `state.active`, `state.inactive` |
| "Change schedule", "Put more trips on sale", "Put trips on sale", "Stop schedule", "Restart schedule", "See trips", "Go back", "Back to my schedules", "Date" | UI states, AC-5, AC-9, UI state "Date missing" |
| "You can put trips on sale up to 60 days ahead (until {latest_date})." | UI state "Schedule page" |
| `msg:confirm.trips_will_be_cancelled` | UI state "Confirm stop" |
| "{n} more trips are now on sale. Trips are on sale until {date}." / "No new trips were needed. Trips are on sale until {date}." | UI states "Success — trips on sale" / "Success — nothing new" |
| "Schedule stopped. {n} upcoming trips were cancelled." / "Schedule restarted. No trips are on sale yet. Choose a date and put trips on sale." | UI states "Success — stopped" / "Success — restarted" |
| "Choose a date from today to {latest_allowed_date}." / "Date is required." | UI states "Date out of range" / "Date missing" (`msg:validation.required`) |
| `msg:problem.bus_trip_overlap` ("{date} (a trip leaving {time})"), `msg:problem.bus_inactive` + "Open the bus", `msg:problem.schedule_inactive` | UI states "Bus busy", "Bus inactive", "Schedule stopped meanwhile" |
| `msg:problem.not_found` ({entity} "schedule"), `msg:problem.forbidden`, `msg:problem.operator_profile_not_ready`, `msg:problem.upstream`, `msg:problem.internal_error` | UI states |

## Contract
- URL: `/operator/schedules/{scheduleId}` (absolute `@page`, fixed by the product) — page name `/Operator/Schedule/Schedule`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- Load: `getSchedule`, then `getRoute` and `getBus` together. A route or bus Fleet will not give is shown as
  "Unknown route" (no stops) / "Unknown bus" (no link); fares then keep Fleet's order.
- Put on sale: `?handler=Extend` with `Input.EndDate` (a date box, `min` today, `max` today + 60, IST by the
  portal's clock). Checked before sending.
- Stop: `?stop=true` (the "Stop schedule" link) shows the question; "Go back" links to the page; the question
  posts `?handler=Stop`. Restart: `?handler=Restart`, no question.
- Every success redirects to the page (PRG) with the flash; the schedule is read again.

## Form fields

| Field (Input.*) | Type | Required | Validation message |
|---|---|---|---|
| EndDate | date (yyyy-MM-dd) | yes | "Date is required." (`msg:validation.required`); outside today..today+60: "Choose a date from today to {latest}." |

## Screen states

1. **Schedule page (GET)** — title, details, actions by status (active: change, stop, see trips + the put-on-sale
   card; inactive: restart, see trips), "Back to my schedules".
2. **Confirm stop** — the catalog question, "Stop schedule" / "Go back"; the action links are hidden.
3. **Working** — buttons disabled while sent (site-wide `site.js`).
4. **Success** — flash on the page read again.
5. **Date refused** — page check or Fleet (`TRIP_END_DATE_OUT_OF_RANGE` uses Fleet's `latestAllowedDate`;
   `VALIDATION_FAILED` naming `endDate`) — message at the date, typed value kept.
6. **Bus busy / bus inactive / schedule stopped meanwhile** — the message on top, schedule read again.
7. **Not found / not allowed / profile not ready** — the message instead of the schedule.
8. **Service unavailable / server error** — on GET: the message, no schedule. On an action: the message, the
   schedule read again (the stop question kept open).

## Acceptance criteria

- [x] AC-4: open one schedule — `Get_ActiveSchedule_ShowsDetailsAndActions`
- [x] AC-7: date limits (page offers only today..+60, hint) — `Get_ActiveSchedule_OffersDatesFromTodayToSixtyDaysAhead`,
  `PostExtend_DateMissingOrOutOfRange_ShowsMessageSendsNothing`, `PostExtend_FleetSaysOutOfRange_ShowsFleetsLatestDate`
- [x] AC-5 / AC-6: put trips on sale / nothing new — `PostExtend_FleetAnswers_ShowsWhatWasPutOnSale`
- [x] Date missing (Fleet) — `PostExtend_FleetSaysDateMissing_ShowsRequiredAtDate`
- [x] AC-8: bus busy — `PostExtend_BusTripOverlap_ShowsConflictsInIndiaTime`
- [x] AC-11: bus inactive — `PostExtend_BusInactive_ShowsMessageWithLinkToBus`
- [x] Schedule stopped meanwhile — `PostExtend_ScheduleStoppedMeanwhile_ShowsMessageAndScheduleAsItNowIs`
- [x] AC-9: stop (ask, then stop) — `GetStop_ActiveSchedule_AsksFirstAndSendsNothing`, `PostStop_Confirmed_StopsAndOffersRestartOnly`
- [x] AC-10: restart — `PostRestart_InactiveSchedule_RestartsWithoutAsking`; inactive offers restart only —
  `Get_InactiveSchedule_OffersRestartAndTripsOnly`
- [x] Rule 2 / names unknown, "Every day" — `Get_EveryDayAndBusNotGiven_ShowsEveryDayUnknownBusAndFleetFareOrder`,
  `Get_RouteNotGiven_ShowsUnknownRouteWithoutStops`
- [x] AC-12: not found / not his / not ready — `Get_ScheduleNotFound_ShowsNotFoundWithLinkBack`,
  `Get_FleetRefuses_ShowsMessageInsteadOfSchedule`, `PostRestart_ScheduleNotFound_ShowsNotFoundWithoutSchedule`
- [x] Service unavailable — `PostStop_FleetUnreachable_KeepsScheduleAndQuestion` (+ GET row of the theory above)
- [x] Shared rule 8 / operator only — `Get_FleetAnswersUnauthorized_EndsSession`, `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getSchedule` | `routego-fleet:CAP-TRP-003@0.1.0` | `GET /schedules/{scheduleId}` → 200 Schedule | The page | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `createScheduleTrips` | `routego-fleet:CAP-TRP-005@0.1.0` | `POST /schedules/{scheduleId}/trips` `{endDate}` → 201 TripExtension | Put on sale | `VALIDATION_FAILED` (endDate), `TRIP_END_DATE_OUT_OF_RANGE`, `BUS_TRIP_OVERLAP`, `BUS_INACTIVE`, `SCHEDULE_INACTIVE`, `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `deactivateSchedule` | `routego-fleet:CAP-TRP-006@0.1.0` | `POST /schedules/{scheduleId}/deactivate` → 200 ScheduleChange | Stop | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `activateSchedule` | `routego-fleet:CAP-TRP-007@0.1.0` | `POST /schedules/{scheduleId}/activate` → 200 Schedule | Restart | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `getRoute` | `routego-fleet:CAP-RTE-004@0.1.0` | `GET /routes/{routeId}` | Route name and stops | any refusal → "Unknown route" |
| `getBus` | `routego-fleet:CAP-BUS-003@0.1.0` | `GET /buses/{busId}` | Bus name, seat-class order | any refusal → "Unknown bus" |

All calls carry the operator's access token. Wire shapes from Fleet's contract (`contracts/trips/schedule.yaml`,
`trip.yaml`); none of these operations is built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
