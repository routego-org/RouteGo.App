# CAP-UI-BUS-005 — Public bus page

- Capability: CAP-UI-BUS-005
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/buses/features/public-bus-page.md` (feature spec 0.1.0)
- Area: Buses
- Slice: `Features/Buses/PublicBus/` — `PublicBus.cshtml.cs` (wire records, handler, page model), tests in `PublicBus.Tests.cs`

**Text provenance:**

| Words | Source |
|---|---|
| "{bus name}" (page title) | UI state "Bus" |
| "Bus" (browser title when no bus is shown) | `label.bus.name` shown as "Bus" |
| Bus type, Amenities, Seat classes | derived from Fleet's attribute names |
| AC | `label.bus.is_ac` |
| Seater / Sleeper / Semi-sleeper; Wi-Fi … Restroom; Yes / No | `bus_type.*`, `amenity.*`, `flag.yes_no` |
| "None" (no amenities) | business rule 5 |
| "{total_seats} seats", "Bus company: {company_name}", "Rating {rating} out of 5" (one decimal) | UI state "Bus", business rule 4 |
| "No photos of this bus yet." | UI state "No photos" |
| "We couldn't find that bus. It may have been removed.", "Go to Home" (to `/`) | `msg:problem.not_found` ({entity} "bus"), `msg:access.go_home` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |

## Contract
- URL: `/buses/{busId}` (absolute `@page`, fixed by the product) — page name `/Buses/PublicBus/PublicBus`
- Auth: **everyone** — visitors and every signed-in role (`PageAccessLevel.Everyone` in the page access table;
  not visitor-only, so nobody is sent away).
- Fleet call **without** the person's access token (the request is not marked `WithUserToken`), so it never
  renews or ends a session either.
- Photos are shown in Fleet's order; a photo whose address is not an `http`/`https` address is left out.
- Not found is shown at the same address (200), with "Go to Home".

## Form fields

None — a read-only page.

## Screen states

1. **Bus (GET)** — title = bus name; photos (or "No photos of this bus yet."); Bus type, AC, Amenities (or
   "None"), Seat classes; "{n} seats", "Bus company: …", "Rating … out of 5". No status, no registration number,
   no seat layout.
2. **Bus not found** — `NOT_FOUND`: the message with "Go to Home".
3. **Service unavailable / server error** — the message; the role menu keeps working.

## Acceptance criteria

- [x] AC-1: a visitor opens a bus page (no sign-in, no token, photos in order, every detail) — `Get_Visitor_ShowsBusWithoutSignInOrToken`
- [x] AC-2: signed-in people see the same page, no token — `Get_SignedIn_ShowsSamePageWithoutToken` (Customer, Operator, Admin)
- [x] AC-3: a bus without photos (+ rule 5 "None") — `Get_BusWithoutPhotosOrAmenities_SaysNoPhotosAndNone`
- [x] AC-4: an inactive bus still shows, no status — `Get_BusWithoutPhotosOrAmenities_SaysNoPhotosAndNone` (no status word on the page)
- [x] AC-5: unknown bus — `Get_UnknownBus_ShowsNotFoundWithGoHomeLink`
- [x] AC-6: Fleet is down — `Get_FleetUnreachable_ShowsUpstreamMessage`
- [x] A photo address that is not a web address is never put into the page — `Get_PhotoAddressNotAWebAddress_LeavesThatPhotoOut`
- [x] Home-and-navigation (everyone opens `/buses/{busId}`) — page access table row tested in `Foundation/SessionRulesTests.cs`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getPublicBus` | `routego-fleet:CAP-BUS-011@0.1.0` | `GET /public/buses/{busId}` (no token) → 200 PublicBus | The page | `NOT_FOUND` |

Wire shape from Fleet's contract (`contracts/buses/bus.yaml` `PublicBus`, `bus-photo.yaml` `PublicBusPhoto`).
Not built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.

**Photo addresses (host wiring, not this slice):** the CSP's `img-src` allows the photo delivery origin from
the optional setting `PhotoDeliveryOrigin` (SSM `photo-delivery-origin`). Until that parameter is set in a
deployment, photos are blocked in a real browser there.
