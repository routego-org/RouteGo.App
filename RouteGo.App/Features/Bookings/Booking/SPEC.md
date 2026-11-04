# CAP-UI-BKG-004 — One booking

- Capability: CAP-UI-BKG-004
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/my-bookings.md` (feature spec 0.1.0, AC-6 to AC-21)
- Area: Bookings
- Slice: `Features/Bookings/Booking/` — `Booking.Handler.cs` (what to offer, handler), `Booking.cshtml.cs` (page model), tests in `Booking.Tests.cs` and `Booking.Actions.Tests.cs`
- Shared with the other booking pages: `Common/UI/BookingDisplay.cs`, `Common/UI/BookingModels.cs`,
  `Features/Shared/_HoldCountdown.cshtml` + `wwwroot/js/hold-countdown.js`, `Features/Shared/_BookingOutcome.cshtml`.

**Text provenance:**

| Words | Source |
|---|---|
| "Booking {booking_id}" | UI states, Booking |
| Route, Travel date, Departs, Arrives, Bus, Bus type, Bus company; Seat number … Passenger gender; Total, Contact name, Contact email, Booked at, Confirmed at | business rule 4; `msg:label.*` (derived labels; "Bus", "Bus type" and "Bus company" are derived from busName, busType and operatorName) |
| "Seat changes: {n} of 3 used" | `msg:label.booking.seat_change_count` |
| "Your seats are held for {time_left}." | bookings shared rule 7 |
| "The time to pay has ended. If you already paid, we will confirm it or refund you." | business rule 6 |
| "Pay now", "Give up this booking", the question, "Give up booking", "Keep booking" | business rule 5, UI states |
| "You gave up this booking. Your seats were released." | UI states, Success — given up |
| "This booking is already {status}. Nothing was changed." | UI states, No longer unpaid |
| "You have used all 3 seat changes.", "Seats can't be changed in the last 30 minutes before the trip leaves." | business rule 7 |
| "Change seats", "Cancel booking" | business rule 7 |
| Status, failure, refund, cancellation words | `msg:state.*`, `msg:booking_failure.*`, `msg:booking_note.expired`, `msg:cancellation_kind.*`, `msg:refund_*` |

## Contract
- URL: `/my-bookings/{bookingId}` (absolute `@page`, fixed). The booking is not constrained to a GUID in the route: an address that is no
  identifier shows "couldn't find that booking" without calling Booking.
- Auth: **customers only** (`/Bookings/` prefix); Booking's owner check answers `FORBIDDEN`.
- **Pay now** (`?handler=PayNow`, POST): loads the booking again; only when Booking gives a payment page address that is an absolute http(s)
  address and the hold is still running does the browser go there (one redirect). The address is never written to the page, a log line or the session.
  Deployed, the payment page's origin must be allowed as a form target by the security policy (`PaymentPageOrigin`, see Program.cs).
- **Give up** is asked first by an address (`?giveUp=true`) so it works without a script; `?handler=GiveUp` (POST) calls `releaseBooking`, then
  redirects back with a one-time message; a refusal re-renders with the booking loaded again.
- The hold countdown is a small script (`hold-countdown.js`); without it the time stays as loaded. At zero the script loads the page once more.
- The portal's own 30-minute figure only decides whether "Change seats" is offered (`BookingDisplay.CutoffMinutes`); Booking still decides and refuses.

## Form fields
None typed. Buttons: Pay now, Give up booking.

## Screen states
1. **Confirmed** — details, "Seat changes: n of 3 used", ticket link, Change seats / Cancel booking or the explanations; after departure only the notice.
2. **Waiting for payment** — countdown, Pay now (when an address is given), Give up this booking; hold ended → "The time to pay has ended…".
3. **Give-up question**, **Given up**, **No longer unpaid**.
4. **Cancelled / Failed / Expired** — outcome lines, "Search for buses" only.
5. **Not found**, **Forbidden**, **Service unavailable / server error**.

## Acceptance criteria

- [x] AC-6: a confirmed booking — `Get_Confirmed_ShowsTripSeatsMoneyAndActions`
- [x] AC-7: waiting for payment — `Get_WaitingForPayment_ShowsCountdownPayNowAndGiveUpButNotTheAddress` (the zero-reload is script-only, not covered by the .NET tests)
- [x] AC-8: Pay now — `PayNow_WhileTheHoldRuns_SendsTheBrowserToTheAddressBookingGives`
- [x] AC-9: give up — `GiveUp_Link_AsksFirstWithBothChoicesAndSendsNothing`, `GiveUp_Confirmed_ReleasesTheBookingAndShowsItAbandoned`
- [x] AC-10: giving up twice — Booking answers the same booking (200); the page treats it as success (same path as AC-9)
- [x] AC-11: paid meanwhile — `GiveUp_WhenPaidMeanwhile_SaysSoAndLoadsTheBookingAgain`
- [x] AC-12: the time to pay has ended — `Get_HoldEndedButStillUnpaid_ShowsTimeToPayEndedWithoutCountdownOrPayNow`, `PayNow_WhenTheHoldEndedMeanwhile_ShowsTheBookingAgainWithoutRedirecting`
- [x] AC-13: seat changes used up — `Get_AllSeatChangesUsed_ExplainsAndDoesNotOfferChangeSeats`
- [x] AC-14: the last 30 minutes — `Get_LastThirtyMinutes_ExplainsAndStillOffersCancel`
- [x] AC-15: after departure — `Get_AfterDeparture_ShowsNoticeAndNeitherChangeNorCancel`
- [x] AC-16: a cancelled booking — `Get_CancelledByCustomer_ShowsKindTimePercentRefundAndNoActions`, `Get_CancelledWithNoRefundDue_SaysSo`
- [x] AC-17: cancelled by an admin — `Get_CancelledByAdmin_ShowsTheAdminsReason`
- [x] AC-18: failed and expired — `Get_FailedAndExpired_ShowReasonOrNoteAndOnlyTheSearchLink`
- [x] AC-19: another customer's booking — `Get_AnotherCustomersBooking_ShowsForbiddenAndNoBooking`
- [x] AC-20: unknown booking / not an identifier — `Get_UnknownBookingOrNotAnIdentifier_ShowsNotFoundAndOnlyCallsBookingForTheFirst`
- [x] AC-21: operators and visitors — `Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress`, `Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBooking`
- [x] AC-22: Booking cannot be reached — `Get_BookingDown_ShowsUpstreamMessage`, `GiveUp_WhenBookingIsDown_ShowsUpstreamMessageAndSendsTheReleaseOnce`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBooking` | `routego-booking:CAP-BKG-002@0.1.3` | `GET /bookings/{bookingId}` → 200 Booking | The page, and before "Pay now" | `NOT_FOUND`, `FORBIDDEN` |
| `releaseBooking` | `routego-booking:CAP-BKG-004@0.1.3` | `POST /bookings/{bookingId}/release` → 200 Booking | Give up | `NOT_FOUND`, `FORBIDDEN`, `BOOKING_NOT_UNPAID` |

Wire shape from Booking's `contracts/bookings/booking.yaml` (no Booking API in `02-Code-Projects` yet). Tested against `FakeBackend` only; not live-tested.
