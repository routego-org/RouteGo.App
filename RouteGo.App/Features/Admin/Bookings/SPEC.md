# CAP-UI-ADM-009 — Admin bookings search

- Capability: CAP-UI-ADM-009
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/administration/features/admin-bookings.md` (feature spec 0.1.0, AC-1 to AC-7 and AC-21 for the search) and `capabilities/administration/README.md` (shared rules)
- Area: Admin
- Slice: `Features/Admin/Bookings/` — `Bookings.Handler.cs` (wire records, the criteria checks and the search call), `Bookings.cshtml.cs` (page model), `Bookings.cshtml`; tests in `Bookings.Tests.cs`
- Shared: none new. The booking's own page is `Features/Admin/BookingPage/`; the two folders are named so neither name starts with the other (the slice-isolation test matches namespaces by prefix, so `Booking` next to `Bookings` would leak into itself).

**Text provenance:**

| Words | Source |
|---|---|
| "Bookings", "Find a booking by its ID, by the contact's email address, or by trip.", Search | UI states, Search form |
| Booking ID, Contact email, Trip ID, Status, Contact name, Trip, Seat numbers, Total, Booked at | UI states, Results (`msg:label.booking.*`) |
| "{total} bookings." / "1 booking." | UI states, Results |
| `msg:empty.search` with {items} "bookings" | AC-4 |
| "Enter a booking ID, an email address or a trip ID." | AC-5 |
| `msg:validation.format` for "booking ID", "email address", "trip ID" | AC-6 |
| `msg:access.no_permission`, `msg:problem.forbidden`, `msg:problem.upstream` | AC-7, AC-21, shared rules |

## Contract
- URL: `/admin/bookings` (folder route). Admins only (`/Admin/` prefix); Booking checks again.
- Address keys `bookingId`, `email`, `tripId`, `page` (spec rule 4); `page` is read from the query by hand because Razor Pages reserves the name. A form sends its empty boxes too, so an address with an empty box and at least one typed box is sent on (302) to the same search without the empty ones (`/admin/bookings?email=Priya%40Example.com`, as AC-1 writes it). All three empty shows the "Enter a booking ID…" message and sends nothing.
- Checks before sending: Booking ID and Trip ID must be identifiers, Contact email a valid address of at most 254 characters. A failed check sends nothing and keeps the typed values. Booking's own `VALIDATION_FAILED` naming `bookingId`, `contactEmail` or `tripId` shows the same format message under that box.
- Request: `POST /bookings/search?page={n}&pageSize=20` with body `{ bookingId, contactEmail, tripId }` (criteria not given are null; the email goes as typed, Booking matches it whole, ignoring case). A POST is not retried.
- verb-justified: the search is a POST on Booking's side only because the email must not appear in Booking's own request address; it writes nothing, and the page itself answers a GET so a reload or a shared link shows the same list.
- Results are newest first, as Booking gives them, 20 a page; page numbers only. Each booking opens `/admin/bookings/{id}?back=…`, where `back` is this search's criteria and page so "Back to search" returns to it (only the four keys are ever read back, so `back` cannot send anyone elsewhere).
- Open question as written: the email stays in the page address (the email-log precedent; admins only).
- Opened from trip search: "Bookings" on every trip of `/admin/trips` opens `/admin/bookings?tripId={tripId}` (added with this slice, in `Features/Admin/Trips/`).

## Form fields
Booking ID, Contact email, Trip ID (text boxes, all optional but at least one).

## Screen states
1. **Search form** — no criteria in the address.
2. **Results** — the count and the table; page navigation above 20.
3. **No results**, **Criteria missing**, **Criteria invalid**.
4. **Forbidden / Booking unavailable / Server error**.

## Acceptance criteria

- [x] AC-1: search by contact email — `Get_Email_SendsItAsTypedAndListsNewestFirstWithEveryColumn`
- [x] AC-2: by trip, every status — `Get_Trip_ListsEveryStatusWaitingForPaymentIncludedAndSaysHowManyBookings`, `Get_OneBooking_SaysOneBookingNotBookings`
- [x] AC-3: combined criteria and paging — `Get_SecondPage_AsksForItAndEveryPageLinkKeepsTheTripAndThePage`, `Get_EmailAndTrip_SendsBothSoOnlyBookingsMatchingBothComeBack`
- [x] AC-4: nothing found — `Get_NothingFound_SaysNoBookingsMatch`
- [x] AC-5: at least one criterion — `Get_EveryBoxEmpty_SaysToEnterACriterionAndSendsNothing`, `Get_FormWithEmptyBoxes_IsSentOnToTheSameSearchWithoutThem`
- [x] AC-6: malformed criteria — `Get_MalformedCriterion_ShowsTheFormatMessageOnThatBoxAndSendsNothing`, `Get_BookingRefusesTheBox_ShowsTheFormatMessageOnIt`
- [x] AC-7: only admins — `Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingBooking`, `Get_Visitor_IsSentToSignInWithTheSearchAsReturnAddress`
- [x] AC-21: Booking is down — `Get_BookingDown_ShowsUpstreamAndKeepsWhatWasTyped`, `Get_BookingForbids_ShowsForbidden`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchBookings` | `routego-booking:CAP-BKG-006@0.1.3` | `POST /bookings/search?page=&pageSize=` → 200 AdminBookingPage | The search | `VALIDATION_FAILED`, `FORBIDDEN` |

Wire shape from Booking's contract (`D:\Project-learn-aws\Booking\workspace\spec-RouteGo.Booking.API\contracts\bookings`); no Booking API in `02-Code-Projects` yet. Tested against `FakeBackend` only; not live-tested.
