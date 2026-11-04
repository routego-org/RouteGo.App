# CAP-UI-TRP-001 — My schedules (schedule list)

- Capability: CAP-UI-TRP-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/trips/features/my-schedules.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/Schedules/` — `Schedules.Handler.cs` (query, wire records, handler), `Schedules.cshtml.cs`
  (page model), tests in `Schedules.Tests.cs`

**Text provenance:**

| Words | Source |
|---|---|
| "My schedules", "Show", "All", "Add a schedule" | UI state "List" |
| Route, Bus, Departure time, Status | `label.route.name`, `label.bus.name` (shown "Bus"), derived from `departure_time`, `status` |
| Days, Fares, Trips on sale until | `label.schedule.days_of_week`, `label.schedule_class_fare.fare`, `label.schedule.trips_generated_through` |
| "Mumbai → Pune via Lonavala", "10:30 PM IST", "Monday, Wednesday, Friday", "Lower berth: ₹1,200, …", "22 Oct 2026" | routes area shared rule 3, trips area shared rules 3–5, design system formats (`TripDisplay`, `IndiaTime`) |
| "Unknown route" / "Unknown bus" | trips area shared rule 2; API surface (names only) |
| Active / Inactive | `state.active`, `state.inactive` |
| "Showing {from}–{to} of {total} schedules" | UI state "List" |
| "You have no schedules yet. Add a schedule to put a bus on a route and start selling trips." | UI state "List empty — no schedules" |
| "No schedules match the current filters." | `msg:empty.filtered` ({items} "schedules") |
| Fleet's 400 `title` | UI state "Bad filter" |
| `msg:problem.forbidden`, `msg:problem.operator_profile_not_ready`, `msg:problem.upstream`, `msg:problem.internal_error` | UI states |

## Contract
- URL: `/operator/schedules` (by convention) — page name `/Operator/Schedules/Schedules`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- Address: `?status=&routeId=&busId=&pageNumber=`; `?busId=` is the link from a bus page. Fleet gets only the
  chosen filters, plus `page` and `pageSize=20`.
- `listSchedules` first; then `listRoutes` and `listBuses` together (`page=1&pageSize=200`, every status) for
  names and the Route / Bus filters. A list Fleet will not give: its names show "Unknown …" and its filter is
  not offered.
- Fares are shown in Fleet's order: the list carries no bus seat-class order.
- No local check of the filters (choice lists); Fleet's `VALIDATION_FAILED` drives "Bad filter".

## Screen states

1. **List (GET)** — title, filters + "Show", "Add a schedule", table (Route links to `/operator/schedules/{scheduleId}`),
   the showing line, page numbers when more than one page.
2. **Empty — no schedules** / **Empty — filtered** — the two messages above.
3. **Bad filter** — Fleet's title on top; the list with no filters.
4. **Profile not ready / not allowed** — the message instead of the content.
5. **Service unavailable / server error** — the message; the filters are kept; no list.

## Acceptance criteria

- [x] AC-1: see my schedules — `Get_Operator_ListsSchedulesWithNamesAndLinks`
- [x] AC-2: filter by status / route (kept in the address, paging) — `Get_RouteAndStatusChosen_SendsFiltersAndKeepsThemInAddress`
- [x] AC-2: the link from a bus page — `Get_BusIdFromBusPage_SendsBusIdAndShowsThatBusChosen`
- [x] AC-3: no schedules yet — `Get_NoSchedulesAtAll_ShowsFirstScheduleInvitation`
- [x] Empty filtered — `Get_FilterMatchesNothing_ShowsFilteredEmptyMessage`
- [x] Bad filter — `Get_FleetRefusesFilter_ShowsAllSchedulesWithProblemTitle`
- [x] Names unknown, filters not offered — `Get_RoutesAndBusesNotGiven_ShowsUnknownNamesWithoutThoseFilters`
- [x] AC-12: not his / not ready — `Get_FleetWillNotList_ShowsMessageInsteadOfContent`
- [x] Service unavailable — `Get_FleetUnreachable_ShowsUpstreamMessageKeepsFilter`
- [x] Operator only — `Get_Customer_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listSchedules` | `routego-fleet:CAP-TRP-002@0.1.0` | `GET /schedules?status&routeId&busId&page&pageSize=20` → 200 `{items, totalCount, page, pageSize}` | The list | `VALIDATION_FAILED` (title), `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `listRoutes` | `routego-fleet:CAP-RTE-003@0.1.0` | `GET /routes?page=1&pageSize=200` | Route names, Route filter | any refusal → "Unknown route", no filter |
| `listBuses` | `routego-fleet:CAP-BUS-002@0.1.0` | `GET /buses?page=1&pageSize=200` | Bus names, Bus filter | any refusal → "Unknown bus", no filter |

All calls carry the operator's access token. Wire shapes from Fleet's contract (`contracts/trips/schedule.yaml`,
`routes/route.yaml`, `buses/bus.yaml`); not built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
