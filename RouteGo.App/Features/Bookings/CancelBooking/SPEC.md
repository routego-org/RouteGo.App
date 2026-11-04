# CAP-UI-BKG-006 — Cancel booking

- Capability: CAP-UI-BKG-006
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/cancel-booking.md` (feature spec 0.1.0)
- Area: Bookings
- Slice: `Features/Bookings/CancelBooking/` — `CancelBooking.Handler.cs` (wire records, preview + cancel, refusal words), `CancelBooking.cshtml.cs` (page model), tests in `CancelBooking.Tests.cs`
- Shared with the other booking pages: `Common/UI/BookingDisplay.cs`, `Common/UI/BookingModels.cs`.

**Text provenance:**

| Words | Source |
|---|---|
| "Cancel booking: {route}, {departure}" | UI states, Preview |
| "You paid ₹{total}.", "If you cancel now you get {percent}% back: ₹{refund} of ₹{total}.", "…you get no refund.", "This was worked out at {calculated_at}…", "Cancelling frees all {n} seats…" | UI states, Preview |
| "Cancel booking", "Keep booking" | UI states, Preview |
| "Your booking is cancelled.", "Refund: {percent}% — ₹{amount}", refund status word, "We will email you when your refund is completed.", "No refund is due." | UI states, Success — cancelled |
| "The refund is {percent}%, not {preview_percent}%, because the trip is closer now." | business rule 5 |
| "This booking is {current_status}, so this can't be done. Nothing was changed." | `msg:problem.booking_not_confirmed` |
| "This trip has already left. Nothing was changed." | `msg:problem.trip_departed` |
| "View booking", "My bookings" | `msg:link.view_booking`, `msg:link.my_bookings` |
| Page title "Booking cancelled" after a cancellation; "Cancel booking" when no booking is shown | not in the spec — **new wording, add to cancel-booking.md** |

## Contract
- URL: `/my-bookings/{bookingId}/cancel` (absolute `@page`, fixed). An address that is no booking identifier shows "couldn't find that booking" without calling Booking.
- Auth: **customers only** (`/Bookings/` prefix).
- `getBooking` and `previewBookingCancellation` are called together on load. The page is the confirmation: "Cancel booking" posts `?handler=Cancel`
  (once; a write is never retried), "Keep booking" is a link and sends nothing.
- The refund share the preview showed is posted back as `PreviewPercent`, so the success page can say when the refund came out lower.
- Success redirects to `…/cancel?cancelled=true`; the summary rides one redirect in TempData (a refreshed page falls back to the normal load,
  which then reports the booking as not confirmed). The portal never works out a refund and never knows the refund tiers.
- When Booking cannot be reached after "Cancel booking": the upstream message with "View booking" (no reload, never resent by the page).

## Form fields
Hidden `PreviewPercent`. No typed fields.

## Screen states
1. **Preview** — what is paid, the refund on offer, when it was worked out, seats freed; Cancel booking / Keep booking.
2. **Cancelled** — refund line and status (or "No refund is due."), "lower than shown" note, View booking / My bookings.
3. **Not confirmed / Trip left** — the message in place of the preview, "View booking", no cancel action.
4. **Not found / Forbidden / Service unavailable / server error.**

## Acceptance criteria

- [x] AC-1: the refund is shown before cancelling — `Get_Confirmed_ShowsTheRefundBeforeCancelling`
- [x] AC-2: no refund due — `Get_NoRefundDue_SaysSoAndStillOffersCancel`
- [x] AC-3: cancel with a refund — `Cancel_WithARefund_ShowsTheCancellationRefundAndEmailPromiseAndNoCancelAction`
- [x] AC-4: cancel when no refund is due — `Cancel_WhenNoRefundIsDue_SaysNoRefundIsDue`
- [x] AC-5: the tier moved — `Cancel_WhenTheTierMovedMeanwhile_SaysTheRefundIsLowerThanShown`
- [x] AC-6: cancelling twice — `Cancel_PressedTwiceFromAnOpenPage_ShowsTheSameSuccessBothTimes`
- [x] AC-7, AC-8: not confirmed — `Get_AlreadyCancelled_…`, `Get_BookingNotConfirmed_NamesItsStatusAndOffersNoCancelAction`
- [x] AC-9: the trip has left — `Get_TripHasLeft_…`, `Cancel_WhenTheTripLeftMeanwhile_ShowsTripDepartedAndLoadsThePageAgain`
- [x] AC-10: visitor, wrong role — `Get_Visitor_…`, `Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBooking`
- [x] AC-11: another customer's booking, unknown booking — `Get_AnotherCustomersBooking_…`, `Get_UnknownBookingOrNotAnIdentifier_…`
- [x] AC-12: Booking down — `Get_BookingDown_ShowsUpstreamMessage`, `Cancel_WhenBookingIsDown_ShowsUpstreamMessageWithViewBookingAndSendsTheCancelOnce`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBooking` | `routego-booking:CAP-BKG-002@0.1.3` | `GET /bookings/{bookingId}` → 200 Booking | Route, departure, seat count | `NOT_FOUND`, `FORBIDDEN` |
| `previewBookingCancellation` | `routego-booking:CAP-CXL-001@0.1.3` | `GET /bookings/{bookingId}/cancellationPreview` → 200 | The refund on offer | `BOOKING_NOT_CONFIRMED`, `TRIP_DEPARTED` |
| `cancelBooking` | `routego-booking:CAP-CXL-002@0.1.3` | `POST /bookings/{bookingId}/cancel` → 200 Cancellation | Cancel | `BOOKING_NOT_CONFIRMED`, `TRIP_DEPARTED` |

Wire shapes from Booking's `contracts/cancellations/cancellation.yaml` (no Booking API in `02-Code-Projects` yet). Tested against `FakeBackend` only; not live-tested.
