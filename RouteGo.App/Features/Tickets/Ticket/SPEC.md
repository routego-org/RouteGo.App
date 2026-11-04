# CAP-UI-TKT-001 — Ticket download

- Capability: CAP-UI-TKT-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/tickets/features/ticket-download.md` (feature spec 0.1.1, AC-1 to AC-14)
- Area: Tickets
- Slice: `Features/Tickets/Ticket/` — `Ticket.Handler.cs` (what to do, handler), `Ticket.cshtml.cs` (page model), tests in `Ticket.Tests.cs`
- Shared: `Common/Ports/ITicketStorage.cs` (the read-only port), `Infrastructure/S3TicketStorage.cs` (the S3 reader), `Common/UI/BookingModels.cs` (`BookingApi.GetBookingAsync`).

**Text provenance:**

| Words | Source |
|---|---|
| "Your ticket is being prepared. This usually takes a few seconds." | UI states, Being prepared |
| "Your ticket is taking longer than usual. Please try again in a few minutes. Your booking is confirmed." | UI states, Taking longer |
| "This booking was cancelled, so its ticket can no longer be opened." | UI states, Cancelled |
| "This booking is not confirmed yet. Your ticket will be ready once payment is confirmed." | UI states, Not confirmed yet |
| "This booking was not confirmed, so it has no ticket." | UI states, No ticket |
| Page title "Your ticket" | UI states (intro) |
| `msg:link.try_again`, `msg:access.go_home`, `msg:problem.*` | message catalog |

No new wording.

## Contract
- URL: `/tickets/{bookingId}` (absolute `@page`, fixed — the ticket email links to it). The booking is not constrained to a GUID in the route:
  an address that is no identifier shows "couldn't find that booking" without calling Booking.
- Auth: **customer or admin** (`PageAccessLevel.CustomerOrAdmin`). An operator gets "no permission" before anything is called. Booking's owner check
  answers `FORBIDDEN` for someone else's booking; the portal never decides ownership.
- Order: `getBooking` with the person's token → the booking's status decides first (only `confirmed` goes on) → ticket storage is checked → a new link is made.
- **Ticket storage is not a service call.** Ticket Generator has no API (its ADR 0001/0002): the portal reads the private S3 bucket itself, object
  `tickets/{bookingId}.pdf` (bucket `ticket-bucket` / `TicketBucket`, `routego-tickets-{account}` deployed), through the platform's own identity. It only
  checks the object exists (`HeadObject`) and signs a 5-minute GET link with the file name `RouteGo-ticket-{bookingId}.pdf` and type `application/pdf`
  (shown inline, so the browser opens the PDF and offers that name when saving).
- The link is only ever put in a `302` (with `Cache-Control: no-store`); it is never rendered, logged or stored. A new one is made on every visit.
- Waiting: a meta refresh every 5 s carrying `?since=<unix seconds>` so the 2 minutes are counted across refreshes (no script). Opening the page without
  `since` (by hand or "Try again") starts a new 2 minutes; a `since` in the future is ignored.
- **Deployment needs** (not decidable in code): the portal's role needs `s3:GetObject` on `tickets/*` **and** `s3:ListBucket` on the bucket. Without
  `ListBucket`, S3 answers a missing object with 403 instead of 404, which the page would show as "not available" instead of "being prepared".
- The key layout (`tickets/{bookingId}.pdf`, booking id as a lower-case GUID) comes from Ticket Generator's product doc, not its spec (the spec leaves it
  to the codebase): confirm it when Ticket Generator is built.

## Form fields
None.

## Screen states
1. **Ticket opens** — no page; redirect to the link.
2. **Being prepared** — heading, spinner, the waiting sentence; refreshes every 5 s.
3. **Taking longer** — warning with "Try again" and "Go to Home"; no refresh.
4. **Cancelled / Not confirmed yet / No ticket** — message with "Go to Home"; no refresh.
5. **Not found**, **Forbidden**, **Booking or storage unavailable / server error** — message with "Go to Home".

## Acceptance criteria

- [x] AC-1: the customer's ticket opens — `Get_ConfirmedWithStoredTicket_SendsTheBrowserStraightToTheLink` (the PDF's file name is set when the link is signed, so it is not visible to the .NET tests)
- [x] AC-2: every visit makes a fresh link — `Get_EveryVisit_MakesANewLinkThatWorksForFiveMinutes`
- [x] AC-3: re-issued ticket — nothing in the portal: the same key is overwritten by Ticket Generator
- [x] AC-4: a visitor signs in first — `Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress`
- [x] AC-5: an admin opens any ticket — `Get_AdminOpeningACustomersTicket_OpensIt`
- [x] AC-6: an operator may not — `Get_Operator_ShowsNoPermissionWithoutCallingBookingOrStorage`
- [x] AC-7: another customer's booking — `Get_AnotherCustomersBooking_ShowsForbiddenAndMakesNoLink`
- [x] AC-8: unknown booking — `Get_UnknownBookingOrNotAnIdentifier_ShowsNotFoundAndOnlyCallsBookingForTheFirst`
- [x] AC-9: being prepared, then ready — `Get_ConfirmedButNotStoredYet_ShowsPreparingAndChecksAgainEveryFiveSeconds`, `Get_PreparingThenStored_NextCheckSendsTheBrowserToTheLink`
- [x] AC-10: still missing after 2 minutes — `Get_StillNotStoredAfterTwoMinutes_StopsCheckingAndOffersTryAgain`, `Get_StartTimeInTheFuture_StartsANewTwoMinutes`
- [x] AC-11: cancelled — `Get_CancelledBookingWithStoredTicket_ShowsCancelledAndMakesNoLink`
- [x] AC-12: unpaid — `Get_NotConfirmedYet_SaysSoAndDoesNotCheckAgain`
- [x] AC-13: failed or expired — `Get_FailedOrExpired_SaysItHasNoTicket`
- [x] AC-14: Booking or storage down — `Get_BookingDown_ShowsUpstreamMessage`, `Get_TicketStorageDown_ShowsUpstreamMessageAndMakesNoLink`

## Open questions
- Spec open question (offer `msg:link.my_bookings` next to "Go to Home"?) — followed as written: only the home link.
- `S3TicketStorage` itself (HeadObject, signing) is not covered by the .NET tests; pages are tested against `FakeTicketStorage`. Try it once against LocalStack
  (`routego-tickets-local`) when Ticket Generator or a seeded object exists.
