# CAP-UI-BKG-002 — Payment return

- Capability: CAP-UI-BKG-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/payment-return.md` (feature spec 0.1.0)
- Area: Bookings
- Slice: `Features/Bookings/PaymentReturn/` — `PaymentReturn.Handler.cs` (phases, handler), `PaymentReturn.cshtml.cs` (page model), tests in `PaymentReturn.Tests.cs`
- Shared with the other booking pages: `Common/UI/BookingDisplay.cs` (status/failure/refund words, hold time, alerts),
  `Common/UI/BookingModels.cs` (Booking's booking shape, `BookingApi.GetBookingAsync`),
  `Features/Shared/_HoldCountdown.cshtml` + `wwwroot/js/hold-countdown.js`, `Features/Shared/_BookingOutcome.cshtml`.

**Text provenance:**

| Words | Source |
|---|---|
| "Confirming your payment", "This usually takes a few seconds. Please do not close this page." | UI states, Confirming |
| "Your seats are held for {time_left}." | bookings shared rule 7 |
| "Your booking is confirmed.", "Total ₹{total}" | UI states, Confirmed |
| "We are still confirming your payment. This can take a few minutes. We will email you when it is done." | UI states, Still confirming |
| "Your booking was not completed.", "The payment service said: {detail}" | UI states, Not completed |
| Expired / cancelled words, refund line | `msg:state.*`, `msg:booking_note.expired`, `msg:cancellation_kind.*`, `msg:refund_reason.*`, `msg:refund_status.*` |
| "View ticket", "View booking", "Try again", "My bookings", "Search for buses" | `msg:link.*` |
| "We couldn't find that booking…" / "You don't have access to this." / "This part of RouteGo is not available…" | `msg:problem.not_found`, `msg:problem.forbidden`, `msg:problem.upstream` |
| Page title for failed / expired / cancelled / not-found: "Your booking" | not in the spec (it gives titles only for Confirming and Confirmed) — **new wording, add to payment-return.md** |

## Contract
- URL: `/my-bookings/{bookingId}/payment-return` (absolute `@page`, fixed: Payment's return address), optional `?since={unix seconds}`.
- Auth: **customers only** (`PageAccessLevel.CustomerOnly` by the `/Bookings/` prefix); visitor → sign in and back with this address,
  operator/admin → no-permission, Booking not called. Booking's own owner check answers `FORBIDDEN`.
- `getBooking` with the person's token. A booking identifier that is not a GUID shows "couldn't find that booking" without calling Booking.
- Checking again: the page asks the browser to refresh itself every 2 s (`<meta http-equiv="refresh">`, no script needed). The 60 s are
  counted from when the visit started, which the refresh address carries as `since` (Unix seconds); opening the address without it, or with a
  value in the future, starts a new minute. The countdown (script) counts down by itself and reloads once at zero.
- The payment page address is never read by this page and no "Pay again" is offered.

## Form fields
None — a read-only page.

## Screen states
1. **Confirming** — booking waiting, under 60 s: heading, wait text, countdown when a future hold end is given; refreshes.
2. **Confirmed** — route, departure, seats, total, View ticket / View booking; no more checking.
3. **Still confirming** — waiting after 60 s: email promise, View booking / Try again / My bookings; no more checking.
4. **Not completed (failed)**, **Expired**, **Cancelled** — status words and outcome lines; no more checking.
5. **Not found**, **Forbidden**, **Service unavailable / server error** — the message; an outage keeps the refresh while under 60 s.

## Acceptance criteria

- [x] AC-1: confirming, then confirmed — `Get_Waiting_ShowsConfirmingCountdownAndRefreshesEveryTwoSeconds`, `Get_Confirmed_ShowsTripSeatsTotalAndLinksAndDoesNotCheckAgain`
- [x] AC-2: hold countdown while waiting — `Get_Waiting_ShowsConfirmingCountdownAndRefreshesEveryTwoSeconds`, `Get_HoldEndedWhileWaiting_ShowsNoCountdownAndKeepsWaiting`
- [x] AC-3: still waiting after 60 seconds — `Get_StillWaitingAfterAMinute_ShowsEmailPromiseAndStopsChecking`
- [x] AC-4: declined — `Get_Declined_ShowsReasonPaymentDetailAndSearchLink`
- [x] AC-5: failure with no payment detail — `Get_FailedWithoutPaymentDetail_ShowsNoPaymentServiceLine`
- [x] AC-6: expired — `Get_ExpiredWithLateRefund_ShowsStatusNoteAndRefundLine`
- [x] AC-7: already confirmed when the page opens — `Get_Confirmed_…` (no wait text, no refresh)
- [x] AC-8: cancelled — `Get_CancelledByCustomer_ShowsStatusKindAndRefundLine`
- [x] AC-9: another customer's booking — `Get_AnotherCustomersBooking_ShowsForbiddenAndNoBooking`
- [x] AC-10: unknown booking / not an identifier — `Get_UnknownBooking_…`, `Get_AddressIsNotABookingId_ShowsNotFoundWithoutCallingBooking`
- [x] AC-11: visitor signs in first — `Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress`
- [x] AC-12: operators and admins — `Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBooking`
- [x] AC-13: Booking cannot be reached while waiting — `Get_BookingDownWhileWaiting_…`, `Get_BookingDownAfterAMinute_…`
- [x] AC-14: session ended while waiting — foundation (`IdentityTokenHandler`/`SessionEndedMiddleware`), tested in `Foundation/SessionRenewalTests.cs`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBooking` | `routego-booking:CAP-BKG-002@0.1.3` | `GET /bookings/{bookingId}` → 200 Booking | The page, repeatedly while waiting | `NOT_FOUND`, `FORBIDDEN` |

Wire shape from Booking's `contracts/bookings/booking.yaml` (no Booking API exists in `02-Code-Projects` yet): camelCase, string enums,
`holdExpiresAt`/`checkoutUrl` only while unpaid. Tested against `FakeBackend` only; not live-tested.
