# CAP-UI-BKG-003 — My bookings

- Capability: CAP-UI-BKG-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/my-bookings.md` (feature spec 0.1.0)
- Area: Bookings
- Slice: `Features/Bookings/MyBookings/` — `MyBookings.Handler.cs` (wire records, paging rules, handler), `MyBookings.cshtml.cs` (page model), tests in `MyBookings.Tests.cs`
- Shared with the other booking pages: `Common/UI/BookingDisplay.cs`, `Common/UI/BookingModels.cs` (trip and refund records).

**Text provenance:**

| Words | Source |
|---|---|
| "My bookings" (title and heading) | UI states, List |
| "{totalItems} bookings" ("1 booking" for one) | UI states, List — **singular for one is new wording** |
| "{source} → {destination}", Departs, seat numbers, Total, status words, refund line | business rule 3; `msg:state.*`, `msg:refund_*` |
| "View booking", "Search for buses" | `msg:link.view_booking`, `msg:link.search_buses` |
| "No bookings yet." | `msg:empty.collection` with {items} "bookings" |
| Upstream message | `msg:problem.upstream` |

## Contract
- URL: `/my-bookings` (absolute `@page`, fixed), optional `?page={n}` (page numbers only; `page` is read from the query by hand because Razor Pages reserves it). A value that is not a whole number of 1 or more shows page 1.
- Auth: **customers only** (`/Bookings/` prefix). Visitor → sign in and back; operator/admin → no-permission, Booking not called.
- `listBookings` with the person's token, `page={n}&pageSize=20`. Booking leaves out a booking still waiting for payment (its own page shows it).

## Form fields
None — a read-only page.

## Screen states
1. **List** — "{n} bookings", one card per booking, page numbers when more than one page.
2. **Empty** — "No bookings yet." with "Search for buses".
3. **Service unavailable / server error** — the message; the role menu keeps working.

## Acceptance criteria

- [x] AC-1: newest first, details, refund lines, links — `Get_Customer_ListsBookingsInTheOrderBookingGivesWithDetailsRefundsAndLinks`
- [x] AC-2: paging, bad page numbers — `Get_ThirdOfThreePages_ShowsTheOldestAndPageNumbers`, `Get_PageThatIsNotAWholeNumberOfOneOrMore_ShowsPageOne`
- [x] AC-3: no bookings yet — `Get_NoBookingsYet_ShowsEmptyMessageAndSearchLink`
- [x] AC-4: a booking waiting for payment is not listed — Booking's rule; the page shows what Booking lists (its page: `Features/Bookings/Booking/`)
- [x] AC-5: only customers — `Get_Visitor_IsSentToSignInWithTheListAsReturnAddress`, `Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBooking`
- [x] AC-22: Booking cannot be reached — `Get_BookingDown_ShowsUpstreamMessageAndKeepsTheMenu`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listBookings` | `routego-booking:CAP-BKG-003@0.1.3` | `GET /bookings?page=&pageSize=` → 200 BookingPage | The list | `FORBIDDEN`, `VALIDATION_FAILED` (shared handling) |

Wire shape from Booking's `contracts/bookings/booking.yaml` (no Booking API in `02-Code-Projects` yet). Tested against `FakeBackend` only; not live-tested.
