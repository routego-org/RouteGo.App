# CAP-UI-RTE-001 — My routes (route list)

- Capability: CAP-UI-RTE-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/routes/features/my-routes.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/Routes/` — `Routes.cshtml.cs` (query, wire records, handler, page model), tests in `Routes.Tests.cs`

**Text provenance:**

| Words | Source |
|---|---|
| "My routes", "Show", "All", "Add a route" | UI state "List" |
| Route, Distance (km), Journey time | `label.route.name`, `label.route.distance_km`, `label.route.journey_minutes` |
| Stops, Status | derived from Fleet's `stops`, `status` |
| Active / Inactive | `state.active`, `state.inactive` |
| "Mumbai → Pune via Lonavala" | routes area shared rule 3 (`FleetDisplay.RouteName`) |
| "3 h 30 min" | design system *Voice & tone* (`FleetDisplay.Duration`) |
| "Showing {from}–{to} of {total} routes" | UI state "List" (`FleetDisplay.ShowingText`) |
| "You have no routes yet. Add a route to say where your buses go." | UI state "List empty — no routes" |
| "No routes match the current filters." | `msg:empty.filtered` ({items} "routes") |
| Fleet's 400 `title` | UI state "Bad filter" (shared rule 3) |
| "You don't have access to this." / "Your operator account is being set up. Please try again in a minute." / "This part of RouteGo is not available right now. Please try again in a few minutes." / internal error | `msg:problem.forbidden`, `msg:problem.operator_profile_not_ready`, `msg:problem.upstream`, `msg:problem.internal_error` |

## Contract
- URL: `/operator/routes` (by convention) — page name `/Operator/Routes/Routes`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- Address: `?status=&pageNumber=`. No status = "All" (Fleet is sent no status). Fleet always gets `page`
  and `pageSize=20`.
- No local check of the filter: it is a choice list, so a bad value only comes from a hand-edited address,
  and Fleet's `VALIDATION_FAILED` answer drives the "Bad filter" state (list reloaded with "All", page 1,
  title on top).
- Page navigation: page numbers only, as on the admin lists.

## Screen states

1. **List (GET)** — title, Status filter + "Show", "Add a route" (`/operator/routes/new`), table Route
   (link to `/operator/routes/{routeId}`) / Stops / Distance (km) / Journey time / Status, the showing
   line, page numbers when more than one page.
2. **Empty — no routes** / **Empty — filtered** — the two messages above.
3. **Bad filter** — Fleet's title on top; the list as "All".
4. **Profile not ready / not allowed** — the message instead of the content (no filter, no add, no list).
5. **Service unavailable / server error** — the message; the filter is kept; no list.

## Acceptance criteria

- [x] AC-1: see my routes — `Get_Operator_ListsRoutesWithDetailsAndLinks`
- [x] AC-2: filter kept in the address (+ paging) — `Get_ActiveFilterSecondPage_SendsFilterAndKeepsItInAddress`
- [x] AC-2: no routes at all — `Get_NoRoutesAtAll_ShowsFirstRouteInvitation`
- [x] Empty filtered — `Get_FilterMatchesNothing_ShowsFilteredEmptyMessage`
- [x] Bad filter — `Get_FleetRefusesFilter_ShowsAllRoutesWithProblemTitle`
- [x] AC-6: not his / not ready — `Get_FleetWillNotList_ShowsMessageInsteadOfContent`
- [x] Service unavailable — `Get_FleetUnreachable_ShowsUpstreamMessageKeepsFilter`
- [x] home-and-navigation AC-11 (customer on an operator page) — `Get_Customer_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listRoutes` | `routego-fleet:CAP-RTE-003@0.1.0` | `GET /routes?status&page&pageSize=20` (operator token) → 200 `{items, totalCount, page, pageSize}` | The list | `VALIDATION_FAILED` (title), `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |

Wire shapes from Fleet's contract (`contracts/routes/route.yaml`, `RouteListResponse` / `Route`); not built in
RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
