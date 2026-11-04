# CAP-UI-RPT-001 — Operator dashboard

- Capability: CAP-UI-RPT-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/reports/features/operator-dashboard.md` (feature spec 0.1.0, AC-1 to AC-12) and `capabilities/reports/README.md` (shared rules)
- Area: Operator
- Slice: `Features/Operator/Dashboard/` — `Dashboard.Handler.cs` (wire records, the two calls, trip words and addresses), `Dashboard.cshtml.cs` (page model), `Dashboard.cshtml`, `_Sales.cshtml`, `_Trips.cshtml`, `wwwroot/js/dashboard.js`; tests in `Dashboard.Tests.cs`, `Dashboard.Trips.Tests.cs`, `Dashboard.Access.Tests.cs`
- Shared (the admin summary reuses them): `Common/UI/ReportModels.cs` (`SalesTotals`, `SalesDay`, range check result), `Common/UI/ReportDisplay.cs` (whole-rupee money with a minus sign, the 31-day range check, the revenue note).

**Text provenance:**

| Words | Source |
|---|---|
| "Dashboard", "Sales", "Trips on {day}" | UI states |
| From, To, Day, Show figures, Show trips | UI states, business rules 3 and 4 |
| Confirmed bookings, Confirmed seats, Revenue, Refunds, Net, Date | `msg:label.sales.*` |
| "Revenue counts a booking on the day it was confirmed, even if it was cancelled later. Refunds count on the day the money was paid back." | reports shared rule 3 |
| "{n} trips.", "1 trip.", "{seats_sold} of {seats_total} seats sold ({percent}%)", "No trips leave on {day}." | business rule 6, AC-8 |
| "Seats", "Passengers" | AC-7 |
| `msg:validation.date_range`, `msg:validation.format` | UI states, Dates invalid |
| **New wording:** the format message reads "Enter a valid date." on every date box (the catalog's `{field}` is "date"; the box's own label is already above it); the trips heading is "Trips" while the Day box holds no date | — |

## Contract
- URL: `/operator/dashboard` (folder route). A bare address is sent on (302) to `/operator/dashboard?from=…&to=…&day=…`: from = 6 days before today (IST), to = today, day = today, so a link or reload shows the same page. A key present but empty is **not** defaulted: it is told it is not a date.
- Auth: **operators only** (`/Operator/` prefix). Reporting works out the company from the access token; the portal sends no company.
- **Two sections, two questions.** `?handler=Sales` draws the sales section alone (calls `getOperatorSummary` only) and `?handler=Trips` the trips section alone (`listOperatorTrips` only). With `dashboard.js` the section forms fetch those and redraw one section, then rewrite the address bar; without it the same forms (GET) reload the whole page, which calls both. The whole page asks for both at once.
- Checks before sending: From and To must be dates (`yyyy-MM-dd`), To not before From, at most 31 days (the portal's own limit). Day must be a date. A failed check sends nothing and keeps the typed values. Reporting's own `VALIDATION_FAILED` shows the same range message (sales) or format message (trips).
- Money: whole rupees, "₹10,800", negative net "−₹1,000". Percentage: seats sold ÷ seats in total rounded to the nearest whole number, 0% with no seats. Trips and days appear in the order Reporting gives them.
- Each section keeps its own failure: sales unavailable shows the upstream message in the sales section only, and the same for trips. `FORBIDDEN` shows the access message and no figures.
- Read only; as loaded; the open questions (longest range, "as of", cancelled trips) are left as written.

## Form fields
Sales form: From, To (date boxes; Day travels as a hidden field). Trips form: Day (date box; From and To travel as hidden fields).

## Screen states
1. **Dashboard** — sales (totals, one row per day, the note) and trips of the day.
2. **Figures only zeros / No trips** — zeros and a row for every day; "No trips leave on {day}.".
3. **Dates invalid** — range or format message beside the boxes; typed values kept.
4. **Sales unavailable / Trips unavailable / Forbidden / Server error** — per section.

## Acceptance criteria

- [x] AC-1: opens on the last 7 days and today — `Get_BareAddress_IsSentOnToTheLastSevenDaysAndTodayAndAsksReportingForBoth`
- [x] AC-2: totals, daily list and note — `Get_TwoDaysOfSales_ShowsTheFiveTotalsTheDailyRowsAndTheNote`
- [x] AC-3: a day with nothing — `Get_DayWithNothing_ShowsARowOfZeros` (Reporting sends the zero rows; the page shows what it is given)
- [x] AC-4: negative net — `Get_RefundsOnADayWithNoBookings_ShowsANegativeNetWithAMinusSign`, `Rupees_WholeRupeesWithAMinusSign_ForAnAmount`
- [x] AC-5: range checked before sending — `Get_ReversedOrTooLongRange_ShowsTheRangeMessageKeepsTheDatesAndDoesNotAskForTheSummary`, `Get_FromThatIsNotADate_…`, `Get_FromLeftEmpty_…`, `Get_ReportingRefusesTheDates_ShowsTheRangeMessage`
- [x] AC-6: seats sold per trip — `Get_TripsOfADay_ShowsRouteDepartureAndSeatsSoldWithPercent`, `Percent_…`; the section alone — `Get_TripsSectionOnly_…`, `Get_SalesSectionOnly_…` (the in-page redraw by the script is script-only, not covered by .NET tests)
- [x] AC-7: links from a trip — `Get_EveryTrip_LinksToItsSeatsAndPassengersWithTheRouteName`
- [x] AC-8: a day with no trips — `Get_DayWithNoTrips_SaysNoTripsLeaveThatDay`, `Get_OneTrip_SaysOneTripNotTrips`
- [x] AC-9: sections load on their own — `Get_TripsCannotBeAnswered_ShowsTheFiguresAndTheUpstreamMessageInTheTripsSection`
- [x] AC-10: only an operator — `Get_CustomerOrAdmin_ShowsNoPermissionWithoutCallingReporting`, `Get_Visitor_IsSentToSignInWithTheDashboardAsReturnAddress`
- [x] AC-11: Reporting refuses — `Get_ReportingRefuses_ShowsForbiddenAndNoFigures`
- [x] AC-12: Reporting down — `Get_ReportingDown_ShowsUpstreamInBothSectionsKeepsTheDatesAndTheMenu`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getOperatorSummary` | `reporting:CAP-RPT-002@0.1.1` | `GET /reports/operator/summary?from=&to=` → 200 OperatorSummary | Sales section | `VALIDATION_FAILED`, `FORBIDDEN` |
| `listOperatorTrips` | `reporting:CAP-RPT-003@0.1.1` | `GET /reports/operator/trips?date=` → 200 OperatorTripListResponse | Trips section | `VALIDATION_FAILED`, `FORBIDDEN` |

Wire shape from Reporting's contract (`D:\Project-learn-aws\Insight\Reporting\contracts\reporting\`); no Reporting API in `02-Code-Projects` yet. Tested against `FakeBackend` only; not live-tested.
