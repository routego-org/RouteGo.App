# CAP-UI-BUS-003 — Bus page: details, seat layout, activate and deactivate

- Capability: CAP-UI-BUS-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/buses/features/my-buses.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/Bus/` — `Bus.cshtml.cs` (wire records, handler, page model), `Bus.Layout.cs`
  (the seat layout drawn deck by deck); tests in `Bus.Tests.cs`

**Text provenance:** every string comes from the canonical spec's `## UI states` / acceptance criteria, the
message catalog where the spec says `msg:<key>`, or a catalog label:

| Words | Source |
|---|---|
| "{bus name}" (page title) | UI state "Bus page" |
| "Bus" (title when no bus is shown) | `label.bus.name` shown as "Bus" (the catalog's short form) |
| Bus name, AC, Seats | `label.bus.name`, `label.bus.is_ac`, `label.bus.total_seats` |
| Registration number, Bus type, Amenities, Seat classes, Status, Created at, Updated at | derived from Fleet's `registration_number`, `bus_type`, `amenities`, `seat_classes`, `status`, `created_at`, `updated_at` |
| Seater / Sleeper / Semi-sleeper | `bus_type.*` |
| Wi-Fi, Charging point, Blanket, Water bottle, Reading light, CCTV, GPS tracking, Restroom | `amenity.*` |
| "None" (no amenities) | UI state "Bus page" |
| Yes / No | `flag.yes_no` |
| Active / Inactive | `state.active`, `state.inactive` |
| Lower deck / Upper deck | `deck.lower`, `deck.upper` |
| "Edit bus", "Deactivate bus", "Activate bus", "See what customers see", "Back to my buses", "Cancel" | UI states |
| "Deactivate {bus_name}? You will not be able to put it on a schedule until you activate it again. Customers can still see its page." | UI state "Confirm deactivate" |
| "Bus deactivated." | `msg:success.deactivated` ({entity} "Bus") |
| "Bus activated." | `msg:success.activated` ({entity} "Bus") |
| "This bus has {n} upcoming trips. The next one leaves on {next_departure}. Stop its schedules or move them to another bus first." | `msg:problem.bus_in_use` ({n} `futureTripCount`, {next_departure} `nextDepartureAt` in IST) |
| "See this bus's schedules" | UI state "Bus in use" |
| "We couldn't find that bus. It may have been removed." | `msg:problem.not_found` ({entity} "bus") |
| "You don't have access to this." | `msg:problem.forbidden` |
| "Your operator account is being set up. Please try again in a minute." | `msg:problem.operator_profile_not_ready` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |

## Contract
- URL: `/operator/buses/{busId}` (absolute `@page`, fixed by the product) — page name `/Operator/Bus/Bus`
- Auth: signed-in Operator only (every `/Operator/` page except Apply/Reapply). Customer or admin:
  no-permission message (403), no Fleet call. Visitor: sent to sign in.
- Confirm: `?deactivate=true` (the "Deactivate bus" link) shows the question above the bus; only for an
  active bus. "Cancel" links back to the bus page; nothing is sent until "Deactivate bus" is pressed.
  "Activate bus" posts at once (`?handler=Activate`) — activating stops nothing, so it does not ask.
- Redirect on success: `/Operator/Bus/Bus` with the same `busId` (PRG); flash "Bus deactivated." /
  "Bus activated."; the bus is read again from Fleet, so it shows the new status and the other action.
- Links: "Edit bus" → `/operator/buses/{busId}/edit`, "See what customers see" → `/buses/{busId}`,
  "See this bus's schedules" → `/operator/schedules?busId={busId}`, "Back to my buses" → `/operator/buses`.
- Fleet calls carry the session's access token (`IdentityRequests.WithUserToken`); Fleet's UNAUTHORIZED
  ends the session (shared rule 8).

## Form fields

None — the deactivate and activate forms only carry the anti-forgery token; the bus comes from the address.

## Screen states

1. **Bus page (GET)** — title = bus name; Bus name, Registration number, Bus type, AC, Amenities (or
   "None"), Seat classes, Seats, Status, Created at, Updated at; the seat layout deck by deck (each row and
   column in place, seat number and seat class in each seat, empty places drawn empty, the aisle as a
   narrow empty column after `aisleAfterColumn`). Actions "Edit bus", "Deactivate bus" (active) or
   "Activate bus" (inactive), "See what customers see"; link "Back to my buses".
2. **Confirm deactivate** — the question and "Deactivate bus" / "Cancel"; the other actions are hidden.
3. **Working** — the button is disabled while sent (site-wide `site.js`).
4. **Success — deactivated / activated (POST → 302)** — back to the bus page with the flash.
5. **Bus in use** — `BUS_IN_USE` on deactivate: the message with the schedules link at the top; the bus
   read again and still Active; the question closed (asking again would be refused again).
6. **Not found** — `NOT_FOUND` on read, deactivate or activate: not-found message with the link "Back to
   my buses"; no bus shown.
7. **Not allowed / profile not ready** — `FORBIDDEN` / `OPERATOR_PROFILE_NOT_READY`: the message instead of
   the page content.
8. **Service unavailable / server error** — on GET: the message, no bus. On deactivate or activate: the
   message, the bus read again, and the deactivate question kept open.

9. **Photos** — the photo section the spec puts on this page (business rule 6) is shown read-only: "Photos",
   "{n} of 5 photos" (or "No photos yet. Customers trust a bus more when they can see it."), each photo in
   order with the first marked "Cover photo" (`label.bus.cover_photo`; words from `bus-photos.md` UI states),
   and the link "Manage photos" → `/operator/buses/{busId}/photos`. Adding, ordering and deleting live on that
   page (`Features/Operator/BusPhotos/`, CAP-UI-BUS-004) — its own page by agreement with the product owner
   (2026-10-10); "Manage photos" is agreed wording, not yet in the spec. A photo is shown only from an
   http(s) address.

## Acceptance criteria

- [x] AC-5: open one bus — `Get_ActiveBus_ShowsDetailsSeatLayoutAndActions`,
  `Get_TwoDeckBus_ShowsEachSeatInItsPlaceWithTheAisle`
- [x] AC-6: deactivate (asks first, then sends) — `Get_Deactivate_AsksFirstAndSendsNothing`,
  `Post_Deactivate_DeactivatesAndOffersActivate`
- [x] AC-7: bus with upcoming trips — `Post_DeactivateBusWithUpcomingTrips_ShowsTripsAndKeepsBusActive`
- [x] AC-8: activate without asking — `Post_Activate_ActivatesWithoutAskingAndOffersDeactivate`
- [x] Business rule 5 (only the action that fits the status) — `Get_InactiveBusAskedToDeactivate_OffersActivateInsteadOfQuestion`
- [x] AC-9: not found / another company's bus — `Get_BusNotFound_ShowsNotFoundWithLinkBack`,
  `Post_ActivateBusNotFound_ShowsNotFoundWithoutBus`, `Get_FleetRefuses_ShowsMessageInsteadOfBus` (FORBIDDEN)
- [x] AC-4: account still being set up — `Get_FleetRefuses_ShowsMessageInsteadOfBus` (OPERATOR_PROFILE_NOT_READY)
- [x] Service unavailable — `Get_FleetRefuses_ShowsMessageInsteadOfBus` (UPSTREAM_UNAVAILABLE),
  `Get_FleetUnreachable_ShowsUpstreamMessage`, `Post_DeactivateFleetUnreachable_KeepsBusAndQuestion`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `Get_FleetAnswersUnauthorized_EndsSession`
- [x] AC-10: operator only — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`
- [x] Business rule 6 (photo section) — `Get_BusWithPhotos_ShowsThemCoverFirstWithLinkToManagePhotos`,
  `Get_BusWithoutPhotos_SaysNoPhotosYet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBus` | `routego-fleet:CAP-BUS-003@0.1.0` | `GET /buses/{busId}` (operator token) → 200 Bus | The bus page | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `deactivateBus` | `routego-fleet:CAP-BUS-005@0.1.0` | `POST /buses/{busId}/deactivate` (no body) → 200 Bus | Deactivate | `BUS_IN_USE` (409), `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `activateBus` | `routego-fleet:CAP-BUS-006@0.1.0` | `POST /buses/{busId}/activate` (no body) → 200 Bus | Activate | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |

Wire shapes from Fleet's contract (`contracts/buses/bus.yaml` `Bus` / `SeatLayout` / `SeatLayoutDeck` /
`SeatLayoutSeat`; of `photos` only `photoId` and `url` are read). None of the three operations is built in RouteGo.Fleet.Api
yet — tested against `FakeBackend` only.
