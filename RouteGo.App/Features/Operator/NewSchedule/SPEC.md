# CAP-UI-TRP-002 — Add a schedule

- Capability: CAP-UI-TRP-002 (shared with `Features/Operator/EditSchedule/`)
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/trips/features/schedule-form.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/NewSchedule/` — `NewSchedule.cshtml.cs` (wire records, handler, page model), tests in
  `NewSchedule.Tests.cs` (opening, fare boxes) and `NewSchedule.Save.Tests.cs` (saving)
- Shared with the change page: `Common/UI/ScheduleForm.cs` (input, checks, request), `ScheduleFormFleet.cs`
  (route/bus choices, seat classes), `ScheduleFormRefusals.cs` (where Fleet's refusals land) and the markup
  `Features/Shared/_ScheduleForm.cshtml`.

**Text provenance:**

| Words | Source |
|---|---|
| "Add a schedule", "Trips for the next 14 days go on sale as soon as you save.", "Choose a bus to enter its fares.", "{seat_class} (₹)", "Save schedule", "Cancel" | UI state "New form" |
| Route, Bus, Departure time, Days, Fares | `label.route.name`, `label.bus.name` (shown "Bus"), derived `departure_time`, `label.schedule.days_of_week`, `label.schedule_class_fare.fare` |
| Monday … Sunday | `day.*` |
| "Show fares for this bus" | **agreed with the product owner 2026-10-10 — not yet in the spec** (the no-JavaScript way to draw the chosen bus's fare boxes; add to schedule-form.md, UI state "Seat classes loading") |
| "To add a schedule you need at least one active route and one active bus.", "Add a route", "Add a bus" | UI state "Nothing to schedule" |
| "Route is required.", "Bus is required.", "Days is required." | `msg:validation.required` |
| "Enter a time from 00:00 to 23:59, like 22:30." | UI state "Field invalid" |
| "Fare must be a whole number of rupees from ₹50 to ₹10,000." | `msg:validation.fare` |
| "We could not load this bus's seat classes. Choose the bus again." | UI state "Seat classes loading" |
| "Schedule created. Trips are on sale until {date}." | UI state "Success — created" (shown on the schedule page) |
| `msg:problem.bus_trip_overlap`, `msg:problem.class_fares_mismatch`, `msg:problem.bus_inactive` + "Open the bus" | UI states "Bus busy", "Fares do not match", "Bus inactive" |
| "This route is inactive. Choose another route.", "We could not find this bus among your buses. …", "We could not find this route among your routes. …" | UI states "Route inactive", "Bus not found", "Route not found" |
| `msg:problem.operator_profile_not_ready`, `msg:problem.forbidden`, `msg:problem.upstream`, `msg:problem.internal_error` | UI states |

## Contract
- URL: `/operator/schedules/new` (absolute `@page`, fixed by the product) — page name `/Operator/NewSchedule/NewSchedule`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- `?routeId=` (from a route page) chooses that route when it is offered.
- Choices: `listRoutes` and `listBuses` with `status=active&page=1&pageSize=200`, read on every draw.
- No JavaScript: "Show fares for this bus" posts the whole form to `?handler=ShowFares`; the page reads the
  bus's seat classes and draws one box per class in the bus's order, keeping fares typed for a class of the
  same name (capitals ignored). "Save schedule" does the same first, so the boxes always match the chosen bus.
- Redirect on success: `/Operator/Schedule/Schedule` with the new `scheduleId` (PRG); flash above.

## Form fields

| Field (Input.*) | Type | Required | Validation message |
|---|---|---|---|
| RouteId | choice | yes | "Route is required." |
| BusId | choice | yes | "Bus is required." |
| DepartureTime | text, `H:mm` or `HH:mm`, 00:00–23:59 (sent as `HH:mm`) | yes | "Enter a time from 00:00 to 23:59, like 22:30." |
| Days | ticks, Monday–Sunday | at least one | "Days is required." |
| Fares[i].Fare (+ hidden SeatClass) | text, whole rupees 50–10,000 | each | `msg:validation.fare` |

## Screen states

1. **New form (GET)** — title, note, the form; fare hint until a bus's boxes are drawn.
2. **Nothing to schedule** — no active route or no active bus: the message and the two links; no form.
3. **Fare boxes (Show fares)** — boxes for the chosen bus; nothing sent; a bus that cannot be read: message at Bus.
4. **Field invalid (POST → 200)** — every message at its field; everything kept; nothing sent.
5. **Refused by Fleet** — field errors (`VALIDATION_FAILED`) at their fields, other fields Fleet's title on top;
   `BUS_INACTIVE` / `ROUTE_INACTIVE` at their field; `UNKNOWN_BUS` / `UNKNOWN_ROUTE` on top with that choice
   cleared; `CLASS_FARES_MISMATCH` on top and boxes redrawn from the bus; `BUS_TRIP_OVERLAP`,
   `OPERATOR_PROFILE_NOT_READY`, `FORBIDDEN`, upstream, others on top. Everything else typed is kept.
6. **Success (POST → 302)** — the schedule page with the flash.
7. **Choices not loaded** — the message (a refused list setting is our fault: internal error); no form.

## Acceptance criteria

- [x] AC-1: open the form (active routes and buses, the note) — `Get_Operator_OffersActiveRoutesAndBusesAndExplainsSale`;
  route already chosen — `Get_RouteIdFromRoutePage_ChoosesThatRoute`
- [x] AC-2: fare boxes follow the bus — `PostShowFares_BusChosenThenAnother_FareBoxesFollowTheBus`;
  seat classes unreadable — `PostShowFares_SeatClassesUnreadable_AsksToChooseBusAgain`
- [x] AC-3: create — `Post_ValidSchedule_CreatesAndShowsScheduleWithConfirmation`
- [x] AC-4: nothing to schedule — `Get_NoActiveRouteOrBus_ShowsWhatIsNeededWithoutForm`
- [x] AC-5: page checks — `Post_NothingChosen_ShowsEachProblemAndSendsNothing`, `Post_FareOutOfRange_ShowsFareMessageKeepsEverything`
- [x] AC-9: bus busy — `Post_BusTripOverlap_ShowsConflictAndKeepsEverything`
- [x] AC-10: fares do not match — `Post_ClassFaresMismatch_ReloadsSeatClassesKeepingMatchingFares`
- [x] Bus/route inactive, not found, profile not ready — `Post_FleetRefuses_ShowsMessageWhereItBelongsKeepingTheForm`,
  `Post_UnknownBus_ClearsBusChoice`
- [x] Field invalid from Fleet — `Post_FleetRefusesTime_ShowsTimeMessage`
- [x] Service unavailable — `Post_FleetUnreachable_ShowsUpstreamKeepsEverything`, `Get_ChoicesRefused_ShowsMessageWithoutForm`
- [x] Choice list `VALIDATION_FAILED` → internal error — `Get_ChoiceListRefusedAsInvalid_ShowsInternalError`
- [x] Operator only — `Get_Customer_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `createSchedule` | `routego-fleet:CAP-TRP-001@0.1.0` | `POST /schedules` ScheduleRequest → 201 Schedule | Save | `VALIDATION_FAILED`, `BUS_INACTIVE`, `ROUTE_INACTIVE`, `UNKNOWN_BUS`, `UNKNOWN_ROUTE`, `CLASS_FARES_MISMATCH`, `BUS_TRIP_OVERLAP`, `OPERATOR_PROFILE_NOT_READY`, `FORBIDDEN` |
| `listRoutes` | `routego-fleet:CAP-RTE-003@0.1.0` | `GET /routes?status=active&page=1&pageSize=200` | Route choice | `OPERATOR_PROFILE_NOT_READY`, `FORBIDDEN`; `VALIDATION_FAILED` → internal error |
| `listBuses` | `routego-fleet:CAP-BUS-002@0.1.0` | `GET /buses?status=active&page=1&pageSize=200` | Bus choice | as above |
| `getBus` | `routego-fleet:CAP-BUS-003@0.1.0` | `GET /buses/{busId}` | Seat classes → fare boxes | any refusal → "We could not load this bus's seat classes. …" |

All calls carry the operator's access token. Wire shapes from Fleet's contract (`contracts/trips/schedule.yaml`,
`routes/route.yaml`, `buses/bus.yaml`); not built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
