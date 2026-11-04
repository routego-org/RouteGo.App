# CAP-UI-RPT-002 — Admin summary

- Capability: CAP-UI-RPT-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/reports/features/admin-summary.md` (feature spec 0.1.0, AC-1 to AC-7) and `capabilities/reports/README.md` (shared rules)
- Area: Admin
- Slice: `Features/Admin/Summary/` — `Summary.Handler.cs` (wire records, the one call), `Summary.cshtml.cs` (page model), `Summary.cshtml`; tests in `Summary.Tests.cs`
- Shared: reuses `Common/UI/ReportModels.cs` and `ReportDisplay.cs` (money, the 31-day range check, the revenue note) built for the operator dashboard, so the two pages count and word the figures alike. The markup is a copy of the dashboard's sales section (a partial may not be shared across slices).

**Text provenance:**

| Words | Source |
|---|---|
| "Summary", From, To, "Show figures" | UI states |
| Confirmed bookings, Confirmed seats, Revenue, Refunds, Net, Date | `msg:label.sales.*` |
| "Revenue counts a booking on the day it was confirmed, even if it was cancelled later. Refunds count on the day the money was paid back." | AC-2, reports shared rule 3 |
| "See settlements" | AC-5 |
| `msg:validation.date_range`, `msg:validation.format` ("Enter a valid date.", as on the dashboard) | UI states, Dates invalid |

## Contract
- URL: `/admin/summary` (folder route). A bare address is sent on (302) to `/admin/summary?from=…&to=…`: from = 6 days before today (IST), to = today. A key present but empty is not defaulted: it is told it is not a date.
- Auth: admins only (`/Admin/` prefix); Reporting refuses an operator too.
- Checks before sending: From and To must be dates, To not before From, at most 31 days (the portal's own limit); a failed check sends nothing and keeps the typed dates. Reporting's own `VALIDATION_FAILED` shows the range message.
- `GET /reports/admin/summary?from=&to=` → totals and one row per day, shown as Reporting gives them; whole-rupee money, negative amounts with "−". No company names or breakdown. Read only.

## Form fields
From, To (date boxes).

## Screen states
1. **Summary** — totals, the daily list, the note, "See settlements"; zeros for a quiet range.
2. **Dates invalid**; **Forbidden / Reporting unavailable / Server error** (no figures, dates kept).

## Acceptance criteria

- [x] AC-1: opens on the last 7 days — `Get_BareAddress_IsSentOnToTheLastSevenDaysAndAsksReportingForThem`
- [x] AC-2: totals and the daily list across companies — `Get_OneDayAcrossCompanies_ShowsTheFiveTotalsTheDailyRowAndTheNote`
- [x] AC-3: a day with nothing shows zeros — `Get_DayWithNothing_ShowsARowOfZeros` (Reporting sends the zero rows; the page shows what it is given)
- [x] AC-4: range checked before sending — `Get_ReversedOrTooLongRange_ShowsTheRangeMessageKeepsTheDatesAndSendsNothing`, `Get_DayThatIsEmptyOrNotADate_ShowsTheFormatMessageOnThatBoxAndSendsNothing`, `Get_ReportingRefusesTheDates_ShowsTheRangeMessage`
- [x] AC-5: link to settlements — `Get_Summary_OffersTheLinkToSettlements`
- [x] AC-6: only an admin — `Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingReporting`, `Get_Visitor_IsSentToSignInWithTheSummaryAsReturnAddress`
- [x] AC-7: Reporting refuses or is down — `Get_ReportingRefuses_ShowsForbiddenAndNoFigures`, `Get_ReportingDown_ShowsUpstreamKeepsTheDatesAndTheMenu`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getAdminSummary` | `reporting:CAP-RPT-004@0.1.1` | `GET /reports/admin/summary?from=&to=` → 200 AdminSummary | The figures | `VALIDATION_FAILED`, `FORBIDDEN` |

Wire shape from Reporting's contract (`D:\Project-learn-aws\Insight\Reporting\contracts\reporting\admin-summary.yaml`); no Reporting API in `02-Code-Projects` yet. Tested against `FakeBackend` only; not live-tested.
