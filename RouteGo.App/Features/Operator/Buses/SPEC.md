# CAP-UI-BUS-001 — My buses (bus list)

- Capability: CAP-UI-BUS-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/buses/features/my-buses.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/Buses/` — `Buses.cshtml.cs` (query, wire records, handler, page model), tests in `Buses.Tests.cs`

**Text provenance:** every string comes from the canonical spec's `## UI states` / acceptance criteria, the
message catalog where the spec says `msg:<key>`, or a catalog label:

| Words | Source |
|---|---|
| "My buses" (title), "Add a bus", "Show", "All", "No photo yet" | UI state "List" |
| Cover photo, Bus name, AC, Seats | `label.bus.cover_photo`, `label.bus.name`, `label.bus.is_ac`, `label.bus.total_seats` |
| Registration number, Bus type, Status | derived from Fleet's `registration_number`, `bus_type`, `status` |
| Seater / Sleeper / Semi-sleeper | `bus_type.*` |
| Yes / No | `flag.yes_no` |
| Active / Inactive | `state.active`, `state.inactive` |
| "Showing {from}–{to} of {total} buses" | UI state "List" (`FleetDisplay.ShowingText`) |
| "You have no buses yet. Add your first bus to start selling seats." | UI state "List empty — no buses" |
| "No buses match the current filters." | `msg:empty.filtered` ({items} "buses") |
| "Your operator account is being set up. Please try again in a minute." | `msg:problem.operator_profile_not_ready` |
| "You don't have access to this." | `msg:problem.forbidden` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |
| (bad filter) the problem's `title` | UI state "Bad filter" |

## Contract
- URL: `/operator/buses` (by `SliceRouteConvention`) — page name `/Operator/Buses/Buses`
- Auth: signed-in Operator only (every `/Operator/` page except Apply/Reapply). Customer or admin:
  no-permission message (403), no Fleet call. Visitor: sent to sign in.
- Address keys: `status` (`active` | `inactive`; absent = "All") and `pageNumber` (1-based). The filter is a
  GET form, so the address keeps it and a reload shows the same list.
- Links: each bus name → `/operator/buses/{busId}`; "Add a bus" → `/operator/buses/new`.
- The cover photo is drawn only from an absolute `http`/`https` address; anything else shows "No photo yet".
- Fleet calls carry the session's access token (`IdentityRequests.WithUserToken`); Fleet's UNAUTHORIZED
  ends the session (shared rule 8).

## Form fields

None — the status filter is a GET form (`status`), read into `BusesQuery` with `pageNumber`.

## Screen states

1. **List (GET)** — title, "Add a bus", the status filter with "Show"; one row per bus in Fleet's order
   (cover photo or "No photo yet", Bus name linking to the bus page, Registration number, Bus type, AC,
   Seats, Status); "Showing …" and page numbers when there is more than one page (20 per page).
2. **List empty — no buses** — the invitation and "Add a bus".
3. **List empty — filtered** — `msg:empty.filtered`.
4. **Bad filter** — `VALIDATION_FAILED`: the list read again as "All", with the problem's title at the top.
5. **Profile not ready / not allowed** — the message instead of the content: no filter, no "Add a bus", no list.
6. **Service unavailable / server error** — the message; the filter kept; no list.

## Acceptance criteria

- [x] AC-1: see my buses — `Get_Operator_ListsBusesWithDetailsAndLinks`
- [x] AC-2: filter by status (kept in the address) — `Get_InactiveFilterSecondPage_SendsFilterAndKeepsItInAddress`
- [x] AC-3: no buses yet — `Get_NoBusesAtAll_ShowsFirstBusInvitation`
- [x] List empty — filtered — `Get_FilterMatchesNothing_ShowsFilteredEmptyMessage`
- [x] Bad filter — `Get_FleetRefusesFilter_ShowsAllBusesWithProblemTitle`
- [x] AC-4: account still being set up (+ not allowed) — `Get_FleetWillNotList_ShowsMessageInsteadOfContent`
- [x] Service unavailable — `Get_FleetUnreachable_ShowsUpstreamMessageKeepsFilter`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `Get_FleetAnswersUnauthorized_EndsSession`
- [x] AC-10: operator only — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`
- [x] A cover photo address that is not a web address is never put into the page — `Get_CoverPhotoNotAWebAddress_ShowsNoPhoto`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listBuses` | `routego-fleet:CAP-BUS-002@0.1.0` | `GET /buses?status=&page=&pageSize=20` (operator token) → 200 BusListResponse | The list | `VALIDATION_FAILED` (bad filter), `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |

Wire shapes from Fleet's contract (`contracts/buses/bus.yaml` `BusListResponse` / `BusSummary`). Not built in
RouteGo.Fleet.Api yet — tested against `FakeBackend` only.

**Photo addresses (host wiring, not this slice):** the CSP's `img-src` allows the photo delivery origin from
the optional setting `PhotoDeliveryOrigin` (SSM `photo-delivery-origin`). Until that parameter is set in a
deployment, cover photos are blocked in a real browser there.
