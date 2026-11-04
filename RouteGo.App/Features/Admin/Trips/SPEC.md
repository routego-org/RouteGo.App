# CAP-UI-ADM-007 — Admin trip search

- Capability: CAP-UI-ADM-007
- Canonical spec: `spec-RouteGo.App` (routego-app 0.3.0) → `capabilities/administration/features/trip-search.md` (feature spec 0.2.0)
- Area: Admin
- Slice: `Features/Admin/Trips/` — `Trips.Handler.cs` (query, wire records, handler), `Trips.cshtml.cs`
  (page model); tests in `Trips.Tests.cs`. The date-range rule, arrival text and status choices are
  shared with the operator's trip list in `Common/UI/TripDisplay.cs`.

**Text provenance:**

| Words | Source |
|---|---|
| "Trips", From city, To city, From, To, Status, "Search", "All", "Showing {from}–{to} of {total} trips" | UI state "List" |
| Company name, Travel date, Route, Departs, Arrives, Bus, Status, Fares, Cancellation reason, Cancelled at | AC-1 / UI state "List" |
| "{city name}, {state}" | business rule 2 |
| "Mumbai → Pune" | AC-1 ("{first city} → {last city}") |
| Scheduled / Departed / Cancelled | `state.scheduled`, `state.departed`, `state.cancelled` |
| "3 Oct 2026", "10:30 PM IST", "4 Oct 2026, 2:00 AM IST" | design system formats (`IndiaTime`); an arrival on a later day shows its date too (as on the operator's trip list) |
| "Company: {company_name}", "Show all companies" | UI state "List" / AC-3 (with no name in the address, the identifier is shown) |
| "No trips match the current filters." | `msg:empty.filtered` ({items} "trips") |
| "Choose a To date on or after the From date, at most 31 days apart." | `msg:validation.date_range` |
| `msg:problem.forbidden`, `msg:problem.upstream`, `msg:problem.internal_error` | UI states |

## Contract
- URL: `/admin/trips` (by convention) — page name `/Admin/Trips/Trips`
- Auth: admins only (page access table: `/Admin/` prefix). An operator or customer sees the
  no-permission message (403) with no Fleet call; a visitor is sent to sign in.
- Address: `?operatorId=&operatorName=&sourceCityId=&destinationCityId=&from=&to=&status=&pageNumber=`.
  Empty dates = today (IST, portal clock) through today + 13; Fleet always gets `from` and `to`. A
  date that cannot be read, To before From, or more than 31 days (both ends counted) → the range
  message, no `searchTrips` call.
- Company: set only by the operators page's "See trips" link; kept through the filter form as hidden
  fields. "Show all companies" drops `operatorId`/`operatorName` and keeps the other filters (page 1).
- `listCities` on every draw, alongside the search; refused or unreachable → no city filters, the
  search still works.
- Fleet's `VALIDATION_FAILED` naming `from`/`to` → the range message; naming anything else (e.g. a
  malformed `operatorId` in the address) → Fleet's title at the top and the default search.
- Read only: no action on a trip; the bus links to its public page `/buses/{busId}`.

## Screen states

1. **List (GET)** — filters, company line, table, showing line, pages.
2. **Empty** — `msg:empty.filtered`.
3. **Date range invalid** — the message under the filters; no list.
4. **Cities unavailable** — no From city / To city filters; the rest works.
5. **Not allowed / unavailable / server error** — the message; filters kept, no list.

## Acceptance criteria

- [x] AC-1: default search — `Get_Default_ListsTwoWeeksOfEveryCompanysTripsReadOnly`
- [x] AC-2: route and date — `Get_RouteAndDatesChosen_SendsThemAndKeepsThemInAddress`
- [x] AC-3: one company — `Get_OneCompanyFromOperatorsPage_SearchesThatCompanyWithWayBack`
- [x] AC-4: date range — `Get_DateRangeNotAllowed_ShowsRangeMessageWithoutSearching`, `Get_FleetRefusesDates_ShowsRangeMessage`
- [x] AC-5: nothing found — `Get_NothingFound_ShowsFilteredEmptyMessage`
- [x] AC-6: only admins — `Get_Operator_ShowsNoPermissionWithoutCallingFleet`
- [x] AC-7: every trip links to its seats, "Seats" → `/admin/trips/{tripId}/seats?route={route}` (scheduled and cancelled checked) — `Get_Default_ListsTwoWeeksOfEveryCompanysTripsReadOnly`
- [x] Cities unavailable — `Get_CitiesNotGiven_SearchesWithoutCityFilters`
- [x] Other refused filter — `Get_FleetRefusesCompany_ShowsWhyAndDefaultSearch`
- [x] Not allowed / service unavailable — `Get_FleetWillNotSearch_ShowsMessageKeepsFilters`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchTrips` | `routego-fleet:CAP-TRP-011@0.1.0` | `GET /admin/trips?operatorId&sourceCityId&destinationCityId&from&to&status&page&pageSize=20` (admin token) → 200 `{items, totalCount, page, pageSize}` | The list | `VALIDATION_FAILED` (from/to → range message; other → title, default search), `FORBIDDEN` |
| `listCities` | `routego-fleet:CAP-RTE-001@0.1.0` | `GET /cities` (admin token) → 200 `{items}` | City filters | any refusal → no city filters |

Wire shapes checked against the built Fleet API (`RouteGo.Fleet.Api/Features/Trips/SearchTrips`,
register status drift: with only one of from/to Fleet covers 14 days from that end — this page always
sends both, so it never meets that case). Tested against `FakeBackend` only, not live-tested.
