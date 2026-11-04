# CAP-UI-RTE-003 — Route page: stops, deactivate

- Capability: CAP-UI-RTE-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/routes/features/my-routes.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/Route/` — `Route.cshtml.cs` (wire records, handler, page model), tests in `Route.Tests.cs`

**Text provenance:** every string comes from the canonical spec's `## UI states` / acceptance criteria, the
message catalog where the spec says `msg:<key>`, or a catalog label:

| Words | Source |
|---|---|
| "{route_name}" (page title), "Mumbai → Pune via Lonavala" | routes area shared rule 3 (`FleetDisplay.RouteName`) |
| "{sequence}. {city name}, {state} — {time from start}" | UI state "Route page" |
| "1 h 50 min", "3 h 30 min", "0 min" | design system *Voice & tone* (`FleetDisplay.Duration`); a whole number of hours is shown "3 h" (the design system gives no example for it) |
| Distance (km), Journey time | `label.route.distance_km`, `label.route.journey_minutes` |
| Status, Created at, Deactivated at | derived from Fleet's `status`, `created_at`, `deactivated_at` |
| "Route" (title when no route is shown) | `label.route.name` |
| Active / Inactive | `state.active`, `state.inactive` |
| "Add a schedule on this route", "Deactivate route", "Cancel", "Back to my routes" | UI states |
| "Deactivate the route {route_name}? You will not be able to add new schedules on it. Schedules and trips already on it keep running. You cannot turn a route back on." | UI state "Confirm deactivate" |
| "Route deactivated." | `msg:success.deactivated` ({entity} "Route") |
| "We couldn't find that route. It may have been removed." | `msg:problem.not_found` ({entity} "route") |
| "You don't have access to this." | `msg:problem.forbidden` |
| "Your operator account is being set up. Please try again in a minute." | `msg:problem.operator_profile_not_ready` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |

## Contract
- URL: `/operator/routes/{routeId}` (absolute `@page`, fixed by the product) — page name `/Operator/Route/Route`
- Auth: signed-in Operator only (every `/Operator/` page except Apply/Reapply). Customer or admin:
  no-permission message (403), no Fleet call. Visitor: sent to sign in.
- Confirm: `?deactivate=true` (the "Deactivate route" link) shows the question above the route; only for
  an active route. "Cancel" links back to the route page; nothing is sent until "Deactivate route" is pressed.
- Redirect on success: `/Operator/Route/Route` with the same `routeId` (PRG); flash "Route deactivated.";
  the route is read again from Fleet, so it shows Inactive and Deactivated at.
- Fleet calls carry the session's access token (`IdentityRequests.WithUserToken`); Fleet's UNAUTHORIZED
  ends the session (shared rule 8).

## Form fields

None — the deactivate form only carries the anti-forgery token; the route comes from the address.

## Screen states

1. **Route page (GET)** — title = route name; stops in order; Distance (km), Journey time, Status,
   Created at, and Deactivated at when inactive. Active route: "Add a schedule on this route"
   (`/operator/schedules/new?routeId={routeId}`) and "Deactivate route". Link "Back to my routes".
2. **Confirm deactivate** — the question and "Deactivate route" / "Cancel".
3. **Working** — the button is disabled while sent (site-wide `site.js`).
4. **Success (POST → 302)** — back to the route page with the flash; no actions any more.
5. **Not found** — `NOT_FOUND` on read or deactivate: not-found message with the link "Back to my routes";
   no route shown.
6. **Not allowed / profile not ready** — `FORBIDDEN` / `OPERATOR_PROFILE_NOT_READY`: the message instead of
   the page content.
7. **Service unavailable / server error** — on GET: the message, no route. On deactivate: the message, the
   route read again and the question kept open.

## Acceptance criteria

- [x] AC-3: open one route — `Get_ActiveRoute_ShowsStopsInOrderWithTimesAndActions`
- [x] AC-4: deactivate — `Post_Deactivate_DeactivatesAndShowsInactiveRoute`
- [x] AC-4 (no actions on an inactive route) — `Get_InactiveRouteAskedToDeactivate_OffersNoQuestionOrActions`
- [x] AC-5: the warning, nothing sent before confirming — `Get_Deactivate_AsksFirstAndSendsNothing`
- [x] AC-6: not found — `Get_RouteNotFound_ShowsNotFoundWithLinkBack`, `Post_RouteNotFound_ShowsNotFoundWithoutRoute`
- [x] AC-6: not his / not ready (+ Fleet down) — `Get_FleetRefuses_ShowsMessageInsteadOfRoute`
- [x] Service unavailable — `Get_FleetUnreachable_ShowsUpstreamMessage`, `Post_FleetUnreachable_KeepsRouteAndQuestion`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `Get_FleetAnswersUnauthorized_EndsSession`
- [x] Operator only (home-and-navigation AC-11 rule) — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getRoute` | `routego-fleet:CAP-RTE-004@0.1.0` | `GET /routes/{routeId}` (operator token) → 200 Route | The route page | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `deactivateRoute` | `routego-fleet:CAP-RTE-005@0.1.0` | `POST /routes/{routeId}/deactivate` (no body) → 200 Route | Deactivate | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |

Wire shapes from Fleet's contract (`contracts/routes/route.yaml`, `Route` / `RouteStop` / `city.yaml` `City`);
both operations are not built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
