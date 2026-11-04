# CAP-UI-RTE-002 — Add a route

- Capability: CAP-UI-RTE-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/routes/features/route-creation.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/NewRoute/` — `NewRoute.Input.cs` (input + page checks), `NewRoute.Handler.cs`
  (wire records + handler), `NewRoute.cshtml.cs` (page model); tests in `NewRoute.Tests.cs` (open, create,
  Fleet's answers, access) and `NewRoute.Checks.Tests.cs` (page checks, adding and removing stops)

**Text provenance:** every string comes from the canonical spec's `## UI states` / acceptance criteria, the
message catalog where the spec says `msg:<key>`, or a catalog label:

| Words | Source |
|---|---|
| "Add a route" (title) | UI state "Form" |
| "You cannot change a route after you create it. Check the stops and times before you save." | UI state "Form" |
| "First stop", "Last stop", "City", "Hours", "Minutes", "Remove", "Add a stop", "Create route", "Cancel" | UI state "Form" |
| "Time from start" | `label.route_stop.offset_minutes` |
| "0 min" (the first stop's time) | AC-1 (`FleetDisplay.Duration(0)`) |
| "{city name}, {state}" (each city choice) | UI state "Form" |
| Distance (km) | `label.route.distance_km` |
| "A route can have at most 12 stops." | UI state "Most stops" |
| "City is required." | `msg:validation.required` ({field} "City") |
| "{city name} is already a stop on this route." | UI state "City twice" |
| "This stop must come later than the stop before it." | UI state "Time not later" |
| "A journey can take at most 48 hours." | UI state "Time too long" |
| "Enter hours (0 to 48) and minutes (0 to 59)." | UI state "Time invalid" |
| "Distance (km) must be between 1 and 3,000." | `msg:validation.range` ({field} "Distance (km)", {min} 1, {max} 3,000) |
| "Route created." (flash, shown on the route page) | `msg:success.created` ({entity} "Route") |
| "One of the cities is no longer available. We have loaded the city list again. Please choose the cities again." | UI state "Unknown city" |
| "Your operator account is being set up. Please try again in a minute." | `msg:problem.operator_profile_not_ready` |
| "You don't have access to this." | `msg:problem.forbidden` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |

## Contract
- URL: `/operator/routes/new` (absolute `@page`, fixed by the product; linked from the route list) — page
  name `/Operator/NewRoute/NewRoute`
- Auth: signed-in Operator only (every `/Operator/` page except Apply/Reapply). Customer or admin:
  no-permission message (403), no Fleet call. Visitor: sent to sign in.
- Redirect on success: `/Operator/Route/Route` with the new `routeId` → `/operator/routes/{routeId}` (PRG);
  flash "Route created."
- Adding and removing a stop need no JavaScript: "Add a stop" and "Remove" are submit buttons that post the
  whole form to `?handler=AddStop` / `?handler=RemoveStop&stop={index}`, and the page is drawn again with
  everything typed. "Create route" posts to the page's own address (explicit `action`, so a page drawn
  after an add or remove still creates). A hidden first "Create route" button makes the Enter key create,
  not remove a stop.
- The city list is read from Fleet each time the page is drawn (open, add/remove a stop, every failed
  create): a server-drawn page keeps nothing between requests. The spec's "loaded once when the page
  opens" is met for the browser — the operator never waits on the list while editing.
- Fleet calls carry the session's access token (`IdentityRequests.WithUserToken`); Fleet's UNAUTHORIZED
  ends the session (shared rule 8).

verb-justified: `OnPostAddStopAsync` / `OnPostRemoveStopAsync` send nothing to Fleet — they only change the
number of stops on the form, which must be posted so nothing typed is lost.

## Form fields

| Field (Input.*) | Type | Page check (message) |
|---|---|---|
| `Stops[i].CityId` | city id (choice) | required (`msg:validation.required`); not chosen at an earlier stop ("City twice", at the later stop) |
| `Stops[i].Hours`, `Stops[i].Minutes` (every stop but the first) | text, kept as typed | whole numbers 0–48 and 0–59 ("Time invalid"); total minutes more than the stop before ("Time not later"); last stop at most 2,880 minutes ("Time too long"). One message per stop, under its hours and minutes |
| `DistanceKm` | text, kept as typed | whole number 1–3,000, empty included (`msg:validation.range`) |

2 to 12 stops: the form always has the first and the last stop; "Add a stop" adds one before the last
stop and is replaced by "A route can have at most 12 stops." at 12; only stops on the way have "Remove".
Hours and minutes become total minutes (hours × 60 + minutes); the first stop is always sent as 0.

## Screen states

1. **Form (GET)** — `listCities` called; title, warning text, "First stop" (City; Time from start "0 min"),
   "Last stop" (City, Hours, Minutes), "Add a stop", Distance (km), "Create route", "Cancel"
   (`/operator/routes`). Cities sorted by name (then state).
2. **Stop added / removed (POST → 200)** — the form drawn again with the stop added before the last stop,
   or the chosen stop on the way removed; every value typed kept; nothing sent to Fleet.
3. **Page check fails (POST → 200)** — each message at its stop or field; nothing sent; everything typed kept.
4. **Saving** — buttons disabled while the form is sent (site-wide `site.js`).
5. **Success (POST → 302)** — `/operator/routes/{routeId}` showing "Route created."
6. **Field invalid from Fleet** — `VALIDATION_FAILED`: `distanceKm` → the range message at Distance (km);
   `stops[i].cityId` → "City is required." at that stop; any other field (a stop's `offsetMinutes`, which
   the page shows only as hours and minutes; `stops`) → the problem's title at the top. Everything kept.
7. **Unknown city** — `UNKNOWN_CITY`: the message at the top, the city list read again, every city choice
   emptied, hours, minutes and distance kept.
8. **Profile not ready / not allowed / Fleet down / server error on create** — the message at the top,
   the form kept.
9. **City list cannot be loaded** — on open (or when it fails on a later post): the message, no form.

## Acceptance criteria

- [x] AC-1: open the form — `Get_Operator_ShowsFirstAndLastStopWithSortedCitiesAndWarning`,
  `Get_EnterKeyInForm_SubmitsCreateNotAStopAction`
- [x] AC-2: create with a stop on the way — `Post_RouteWithStopOnTheWay_SendsMinutesAndShowsCreatedRoute`
- [x] AC-3: page checks before sending — `Post_PageCheckFails_ShowsMessageAtItsPlaceAndSendsNothing` (13 cases),
  `Post_SeveralProblems_ShowsEachAtItsPlaceAndKeepsWhatWasTyped`
- [x] AC-4: stops limit, only stops on the way removable — `Post_AddStopToElevenStops_ReachesTwelveAndOffersNoMore`,
  `Post_AddStop_AddsEmptyStopBeforeLastAndKeepsWhatWasTyped`, `Post_RemoveStop_RemovesOnlyThatStopAndKeepsTheRest`
- [x] AC-5: city not on the list any more — `Post_FleetAnswersUnknownCity_ReloadsCitiesAndKeepsEverythingButCities`
- [x] AC-6: city list cannot be loaded — `Get_FleetUnreachable_ShowsUpstreamAndNoForm`
- [x] Field invalid from Fleet — `Post_FleetRejectsField_ShowsPageWordsAndKeepsValues`
- [x] Profile not ready / not allowed / service unavailable / server error — `Post_FleetRefuses_ShowsMessageAtTopAndKeepsForm`,
  `Post_FleetUnreachableOnCreate_ShowsUpstreamAndKeepsForm`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `Get_FleetAnswersUnauthorized_EndsSession`
- [x] Operator only — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listCities` | `routego-fleet:CAP-RTE-001@0.1.0` | `GET /cities` (operator token) → 200 `{ items: City[] }` | The city choices | `UNAUTHORIZED` (session ends) |
| `createRoute` | `routego-fleet:CAP-RTE-002@0.1.0` | `POST /routes` `{ stops: [{ cityId, offsetMinutes }], distanceKm }` → 201 Route | Create | `VALIDATION_FAILED`, `UNKNOWN_CITY` (422), `OPERATOR_PROFILE_NOT_READY`, `FORBIDDEN`, `UNAUTHORIZED` |

Wire shapes from Fleet's contract (`contracts/routes/city.yaml` `City`/`CityList`, `contracts/routes/route.yaml`
`RouteRequest`/`RouteStopRequest`; only `routeId` of the 201 `Route` is read). `listCities` is built in
RouteGo.Fleet.Api; `createRoute` is not built yet — tested against `FakeBackend` only.
