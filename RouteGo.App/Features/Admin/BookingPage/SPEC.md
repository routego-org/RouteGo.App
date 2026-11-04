# CAP-UI-ADM-010 — Admin booking page with force-cancel

- Capability: CAP-UI-ADM-010
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/administration/features/admin-bookings.md` (feature spec 0.1.0, AC-8 to AC-20 and AC-21) and `capabilities/administration/README.md` (shared rules)
- Area: Admin
- Slice: `Features/Admin/BookingPage/` — `BookingPage.Handler.cs` (what the page offers, the force-cancel call and its refusals, the confirmation's words), `BookingPage.cshtml.cs` (page model), `BookingPage.cshtml`, `wwwroot/js/force-cancel.js`; tests in `BookingPage.Tests.cs`, `BookingPage.ForceCancel.Tests.cs`
- Folder name: `BookingPage`, not `Booking` or `BookingDetail`: `Booking` would be a prefix of the sibling slice `Bookings` (the slice-isolation test matches namespaces by prefix), and a namespace called `BookingDetail` would hide the `BookingDetail` record the booking pages share.

**Text provenance:**

| Words | Source |
|---|---|
| "Booking {booking_id}", Status, Booking ID, Contact name, Contact email, Travel date, Departs, Arrives, Seat number, Seat class, Fare, Passenger name/age/gender, Total, Booked at, Confirmed at, "{n} of 3 used" | UI states, Booking; rule 6 |
| "Seats held until {hold_expires_at}" and the countdown | UI states, Waiting for payment |
| failure reason, "The payment service said: {detail}", "Cancelled by RouteGo", "Reason: …", "Refund percentage: …", "{refund_reason} · ₹{amount} · {refund_status}" | rule 6 (shared with the customer's booking page: `BookingDisplay.OutcomeLines`) |
| "View ticket", "See history", "See the trip's seats", "Back to search", "Force-cancel", "Go back" | rule 7, UI states |
| "Force-cancel booking {id} for {contact}? Its seats go back on sale and the customer is refunded {refund_text}. This cannot be undone." | UI states, Confirm force-cancel |
| "Booking force-cancelled. Refund: {p}% — ₹{amount}." + "Refund requested/completed", "Booking force-cancelled. No refund is due." | UI states, Success |
| "RouteGo already cancelled this booking. Nothing was changed.", "This booking is too old to force-cancel. …" | UI states |
| `msg:validation.reason_length`, `msg:validation.range` (Refund percentage 0–100), `msg:validation.format`, `msg:problem.booking_not_confirmed`, `msg:problem.not_found`, `msg:problem.forbidden`, `msg:problem.upstream` | UI states |
| **Drift to settle:** AC-9 and rule 6 write "The payment service said: {detail}." with a final full stop; the customer pages' specs (and the shared words already built) show Booking's detail as given, with none added. The shared words are kept. | — |

## Contract
- URL: `/admin/bookings/{bookingId}` (absolute route, fixed by the product). Admins only (`/Admin/` prefix), even for the admin's own booking; Booking checks again. A booking ID that is no identifier shows the not-found message without calling Booking.
- Loads with `GET /bookings/{id}` using the admin's token (Booking lets an admin read any booking). The payment page address is never shown. Waiting bookings show "Seats held until …" and the shared countdown (`_HoldCountdown`, reloads once at zero) and offer no force-cancel.
- **Back to search**: the search the admin came from when `?back=` carries it (only `bookingId`, `email`, `tripId`, `page` are read back), otherwise `/admin/bookings`. It is kept through the confirmation and after a force-cancel (the spec's second open question, "keep the criteria in every case", is how it is built).
- **Force-cancel** is offered only on a Confirmed booking. `?forceCancel=true` opens the confirmation above the booking (no script needed); "Go back" is a plain link. Reason 10–500 characters; Refund percentage optional, a whole number 0–100 (`12.5` is a format message, `101` and `-1` the range message); both checked before sending, the confirmation staying open with what was typed. The refund words follow the percentage typed ("in full" or "{p}% of ₹{total}") when the page loads after an invalid try, and live while typing when `force-cancel.js` runs. The portal never works out the refund: the answer's percentage and amount are shown.
- `POST /bookings/{id}/forceCancellations` with `{ reason, refundPercent }` (null = full refund); never retried. 201 → success message by flash (carried across the redirect to the booking page, which shows Status "Cancelled"); 200 → "RouteGo already cancelled this booking."; `BOOKING_NOT_CONFIRMED` → the shared "This booking is {status}, so this can't be done…" words (without the customer's "View booking" link); `FORCE_CANCEL_WINDOW_CLOSED` → the window message with `departedAt` (IST) and `windowDays`; `NOT_FOUND` → not found with "Back to search"; field errors naming `reason` or `refundPercent` → the confirmation again under the box. After every attempt the booking is loaded again, except when it is gone, not allowed, or Booking cannot be reached.
- Booking unreachable during a force-cancel: `msg:problem.upstream`; the confirmation stays with the typed reason and percentage. The contact name and total travel in hidden boxes so the question can be asked again without a loaded booking.

## Form fields
Reason (text area), Refund percentage (text, optional); hidden: the contact name and total the question names.

## Screen states
1. **Booking** — everything in rule 6 and the links; **Waiting for payment**; **Failed / Expired / Cancelled** with their outcome lines.
2. **Confirm force-cancel**, **Force-cancel invalid**, **Success**, **Already force-cancelled**, **Not confirmed**, **Window closed**.
3. **Not found**, **Forbidden (role / Booking)**, **Booking unavailable**, **Server error**.

## Acceptance criteria

- [x] AC-8: open a booking in full — `Get_ConfirmedBooking_ShowsEverythingAboutItAndTheLinks`, `Get_ConfirmedBooking_OffersTicketHistorySeatsBackAndForceCancel`
- [x] AC-9: a booking that ended shows why — `Get_BookingThatFailed_ShowsWhyAndOffersNeitherTicketNorForceCancel`
- [x] AC-10: waiting for payment — `Get_BookingWaitingForPayment_ShowsHoldEndAndCountdownButNoPaymentAddressOrForceCancel`
- [x] AC-11: cancelled shows who, why and the refund — `Get_BookingCancelledByRouteGo_ShowsWhoWhyPercentageAndRefund`
- [x] AC-12: force-cancel with a full refund — `Get_ForceCancel_AsksTheQuestionNamingTheBookingAndContactWithAFullRefund`, `Post_ReasonWithNoPercentage_CancelsInFullAndShowsTheRefundAndTheCancelledBooking`
- [x] AC-13: a chosen percentage — `Post_FiftyPercent_SendsItAndShowsTheAmountBookingGives`, `Post_PercentageTypedButInvalidReason_TheQuestionNamesThePercentageOfTheTotal` (the live rewrite while typing is script-only, not covered by .NET tests)
- [x] AC-14: no refund due — `Post_ZeroPercent_SaysNoRefundIsDue`
- [x] AC-15: reason and percentage rules — `Post_ReasonOrPercentageOutOfRange_ShowsTheMessageKeepsTheQuestionOpenAndSendsNothing`, `Post_BookingRefusesTheReason_ShowsTheMessageAndKeepsWhatWasTyped`
- [x] AC-16: too old — `Post_TooOld_ShowsTheWindowMessageWithDepartureAndDaysAndLoadsTheBookingAgain`
- [x] AC-17: changed meanwhile — `Post_BookingChangedMeanwhile_ShowsWhatItIsNowAndLoadsTheBookingAgain`
- [x] AC-18: already cancelled by RouteGo — `Post_AlreadyForceCancelled_SaysRouteGoAlreadyCancelledIt`
- [x] AC-19: unknown booking — `Get_UnknownBooking_ShowsNotFoundWithTheWayBackToSearch`, `Get_AddressThatIsNoIdentifier_ShowsNotFoundWithoutCallingBooking`, `Post_BookingGoneMeanwhile_ShowsNotFoundWithTheWayBack`
- [x] AC-20: only admins — `Get_CustomerOrOperator_ShowsNoPermissionEvenForTheirOwnBookingAndDoesNotCallBooking`, `Get_Visitor_IsSentToSignInWithTheBookingAsReturnAddress`
- [x] AC-21: Booking is down — `Get_BookingDown_ShowsUpstreamAndKeepsTheMenu`, `Post_BookingDown_ShowsUpstreamAndKeepsTheQuestionWithTheTypedReason`, `Get_BookingForbids_ShowsForbidden`
- [x] Rule 7: "Back to search" — `Get_OpenedFromASearch_BackToSearchKeepsItsCriteriaAndDropsAnythingElse`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBooking` | `routego-booking:CAP-BKG-002@0.1.3` | `GET /bookings/{id}` → 200 Booking | The page | `NOT_FOUND`, `FORBIDDEN` |
| `createForceCancellation` | `routego-booking:CAP-CXL-003@0.1.3` | `POST /bookings/{id}/forceCancellations` → 201/200 Cancellation | Force-cancel | `VALIDATION_FAILED`, `NOT_FOUND`, `FORBIDDEN`, `BOOKING_NOT_CONFIRMED`, `FORCE_CANCEL_WINDOW_CLOSED` |

Wire shapes from Booking's contracts (`…\spec-RouteGo.Booking.API\contracts\bookings` and `…\cancellations`); no Booking API in `02-Code-Projects` yet. Tested against `FakeBackend` only; not live-tested.
