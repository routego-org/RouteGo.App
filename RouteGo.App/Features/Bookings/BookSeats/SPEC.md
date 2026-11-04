# CAP-UI-BKG-001 — Book seats

- Capability: CAP-UI-BKG-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/bookings/features/book-seats.md` (feature spec 0.1.0)
- Area: Bookings
- Slice: `Features/Bookings/BookSeats/` — `BookSeats.Handler.cs` (wire records, load, pay), `BookSeats.Input.cs` (typed values, field checks), `BookSeats.Refusals.cs` (what each Booking refusal means), `BookSeats.cshtml.cs` (page model), tests in `BookSeats.Tests.cs`, `BookSeats.Pay.Tests.cs`, `BookSeats.Checks.Tests.cs`
- Shared: `Common/UI/BookingDisplay.cs` (payment address check, links), `Common/UI/BookingProblems.cs`, `Common/UI/SeatDisplay.cs` + `Features/Shared/_SeatGrid.cshtml` (seat grid; the tick box now carries `data-amount`), `wwwroot/js/book-seats.js`.

**Text provenance:** all words are the spec's: "Book seats: {route}, {departure}" (or "…trip leaving {departure}"), "{a} of {t} seats available.", "Your seats are held for you when you choose Pay.", "Total ₹{total}", "Pay ₹{total}" / "Pay", "Choose at least one seat.", "You can book up to 6 seats.", the price/seat-taken/unpaid-booking/not-yet-bookable/unknown-seats/could-not-finish messages, `msg:problem.*`, `msg:validation.*`, `msg:gender.*`. **New wording (add to book-seats.md):** the button "Update seats" (the no-script way to open passenger boxes) and the labels "Seat {n} · {class} · ₹{fare}" above each passenger's boxes.

## Contract
- URL: `/trips/{tripId}/book` (absolute `@page`, fixed), optional `?route=` label. Customers only (`/Bookings/` prefix; a visitor signs in and comes back with `route`).
  An address naming no trip shows "Seat details … not available" without any call.
- Open: `getSeatMap` (no token) and Identity's profile (token) are asked together; a profile that fails leaves Contact name empty, silently.
- **Fares as seen:** every passenger box posts the fare the page showed (`Passengers[seat].Fare`); the expected total is their sum. After PRICE_CHANGED the answer's
  `seatFares` replace the shown fares, so the next Pay sends the new total.
- **Pay** (POST): checks (1–6 seats, name 2–60, age whole 0–120, gender, contact 2–60) → `createBooking` once (never retried) → redirect to the payment page address
  Booking gave (only an absolute http/https address; one redirect, never shown, logged or kept), or to `/my-bookings/{id}` when none. Deployed, the payment
  page's origin must be in `PaymentPageOrigin` (SSM `payment-page-origin`), because the browser checks the redirect against `form-action`.
- After a refusal the seats are loaded again; seats no longer free are unchosen; typed values and the other seats stay.
- **No script:** `?handler=Choose` ("Update seats") draws the passenger boxes and total for the ticked seats. With the script, ticking opens the boxes and updates the
  total and Pay at once (script-only, not covered by the .NET tests).
- Booking's own UPSTREAM_* ("a service of mine was down") says "We could not complete your booking…"; the portal being unable to reach Booking says `msg:problem.upstream`
  (told apart by the `unreachable` marker the portal puts on its own outage answers).

## Form fields
Seats (`Picks`), per seat `Passengers[{seat}].Name/.Age/.Gender` (+ hidden `.Fare`), `ContactName`.

## Acceptance criteria

- [x] AC-1: open the booking page — `Get_Customer_ShowsSeatsContactNameAndTheHeldNoteAndHoldsNothing`
- [x] AC-2: choose seats and see the total — `Choose_TwoSeats_OpensTheirPassengerBoxesAndShowsTheTotal` (live update is script-only)
- [x] AC-3: pay and go to the payment page — `Pay_FilledForm_SendsTheBookingWithTheExpectedTotalAndGoesStraightToThePaymentPage`
- [x] AC-4: no payment address — `Pay_AnswerWithoutAPaymentAddress_GoesToTheBookingPage`, `Pay_AnswerWithAPaymentAddressThatIsNotAWebAddress_…`
- [x] AC-5: price changed — `Pay_PriceChanged_…`
- [x] AC-6, AC-7: a seat / several seats taken — `Pay_ASeatWasTaken_…`, `Pay_SeveralSeatsTaken_UsesWere`
- [x] AC-8: unpaid booking exists — `Pay_AnUnpaidBookingExists_OffersItAndKeepsTypedValues`
- [x] AC-9: booking closed — `Pay_BookingClosed_…`
- [x] AC-10: trip cancelled — `Pay_TripCancelledMeanwhile_…`
- [x] AC-11: trip not known to Booking — `Pay_BookingDoesNotKnowTheTripYet_…`
- [x] AC-12: seats the trip does not have — `Pay_SeatsTheTripDoesNotHave_…`
- [x] AC-13: at most 6 seats — `Choose_ASeventhSeat_…`, `Pay_MoreThanSixSeats_…`
- [x] AC-14: fields checked before sending — `BookSeats.Checks.Tests.cs`
- [x] AC-15: Booking refuses a field — `Pay_BookingRefusesAField_…`
- [x] AC-16: Booking could not finish — `Pay_BookingCouldNotFinish_…`
- [x] AC-17: Booking cannot be reached — `Pay_BookingCannotBeReached_…`
- [x] AC-18: profile cannot be loaded — `Get_ProfileCannotBeLoaded_…`
- [x] AC-19: cancelled or departed trip — `Get_CancelledOrDepartedTrip_ShowsNoticeAndNoForm`
- [x] AC-20: a trip Seat Inventory does not know — `Get_UnknownTrip_…`, `Get_AddressNamesNoTrip_…`
- [x] AC-21: visitor signs in first — `Get_Visitor_…`
- [x] AC-22: operators and admins — `Get_OperatorOrAdmin_ShowsNoPermissionAndCallsNothing`
- [x] AC-23: only one submit — `wwwroot/js/site.js` (every form), script-only
- [x] AC-24: the payment address is never shown — the redirect is the only use; `Pay_FilledForm_…` and the Booking page tests assert it is not in a page

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getSeatMap` | `routego-seat-inventory:CAP-SEAT-001@0.1.0` | `GET /trips/{tripId}/seats` (no token) | The seats and fares | `NOT_FOUND`, `VALIDATION_FAILED` |
| `getMyProfile` | `routego-identity:CAP-PRF-001@0.1.0` | `GET /me` | Contact name | any problem → empty name |
| `createBooking` | `routego-booking:CAP-BKG-001@0.1.3` | `POST /bookings` → 201 | Pay | all codes of the spec's API surface |

Wire shape from Booking's `contracts/bookings/booking.yaml`. Tested against `FakeBackend` only; not live-tested.
