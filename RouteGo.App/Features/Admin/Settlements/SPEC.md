# CAP-UI-RPT-003 — Settlements

- Capability: CAP-UI-RPT-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/reports/features/settlements.md` (feature spec 0.1.0, AC-1 to AC-10) and `capabilities/reports/README.md` (shared rules)
- Area: Admin
- Slice: `Features/Admin/Settlements/` — `Settlements.Handler.cs` (wire records, the list and download calls), `Settlements.cshtml.cs` (page model), `Settlements.cshtml`; tests in `Settlements.Tests.cs`
- Shared: `Common/UI/ReportDisplay.cs` (money, day parsing). The download link is checked with `BookingDisplay.PaymentAddress` (an absolute http or https address; anything else is dropped).

**Text provenance:**

| Words | Source |
|---|---|
| "Settlements", Day, "Show settlements", "Download report" | UI states |
| Operator ID, Gross sales, Refunds, Net, Generated at | rule 4 (`msg:label.settlement.*`) |
| "{n} settlements for {day}." / "1 settlement for {day}." | rule 4 |
| "No settlements for {day}. The nightly run may not have finished yet." | AC-3 (the portal's own wording, an open question in the spec) |
| "This settlement report is not available." | AC-7 |
| `msg:validation.format` ("Enter a valid day."), `msg:problem.forbidden`, `msg:problem.upstream`, `msg:access.no_permission` | UI states |

## Contract
- URL: `/admin/settlements` (folder route). A bare address is sent on (302) to `?day=` yesterday (IST). A `day` key present but empty is not defaulted: it is told it is not a date. Admins only (`/Admin/` prefix).
- `GET /reports/settlements?date=` → the companies in the order Reporting gives them; an empty list is "not settled yet", not an error. A day that is not a date is never sent; Reporting's `VALIDATION_FAILED` shows the same format message.
- **Download**: "Download report" is a plain link (`?handler=Download&day=…&operatorId=…`), not a form, so the page's `form-action` rule never meets the report's address (a form's redirect to another origin would be blocked). The page calls `GET /reports/settlements/{date}/download?operatorId=` for a fresh link every time and answers a 302 to it with `Cache-Control: no-store`; the link is never rendered, logged, emailed or kept. A GET is used knowing a link prefetcher could ask for a link; the link only works for 5 minutes and only an admin can ask. `NOT_FOUND` (or a refused day or operator, or a link that is not a web address) shows "This settlement report is not available." and the list is loaded again.
- Read only. Operators cannot open the page.

## Form fields
Day (date box).

## Screen states
1. **Settlements** — count and table; **No settlements**; **Day invalid**.
2. **Report opens** (no page; the browser goes to the link), **Report not available**.
3. **Forbidden / Reporting unavailable / Server error**.

## Acceptance criteria

- [x] AC-1: opens on yesterday — `Get_BareAddress_IsSentOnToYesterdayAndAsksReportingForIt`, `Get_TwoSettlements_ShowsTheCountAndEveryCompanysFigures`, `Get_OneSettlement_SaysOneSettlementNotSettlements`
- [x] AC-2: choose another day — `Get_DayNotSettledYet_SaysTheNightlyRunMayNotHaveFinished` (a day in the address is asked for as typed)
- [x] AC-3: a day not settled yet — `Get_DayNotSettledYet_SaysTheNightlyRunMayNotHaveFinished`
- [x] AC-4: a negative net — `Get_SettlementWithRefundsOverSales_ShowsANegativeNetWithAMinusSign`
- [x] AC-5: download a report — `Download_Report_SendsTheBrowserStraightToTheLinkWithoutKeepingItAndAsksReportingAgainEachTime`
- [x] AC-6: every download makes a new link — same test (two downloads, two Reporting calls)
- [x] AC-7: a report that is not there — `Download_ReportNotThere_ShowsNotAvailableAndLoadsTheSettlementsAgain`, `Download_LinkThatIsNotAWebAddress_IsDroppedAndShowsNotAvailable`
- [x] AC-8: the day is checked before sending — `Get_DayThatIsEmptyOrNotADate_ShowsTheFormatMessageAndSendsNothing`, `Get_ReportingRefusesTheDay_ShowsTheFormatMessageAndKeepsTheTypedDay`
- [x] AC-9: only an admin — `Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingReporting`, `Get_Visitor_IsSentToSignInWithTheSettlementsAsReturnAddress`
- [x] AC-10: Reporting refuses or is down — `Get_ReportingRefuses_ShowsForbidden`, `Get_ReportingDown_ShowsUpstreamAndKeepsTheTypedDay`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listSettlements` | `reporting:CAP-RPT-006@0.1.1` | `GET /reports/settlements?date=` → 200 SettlementListResponse | The list | `VALIDATION_FAILED`, `FORBIDDEN` |
| `downloadSettlement` | `reporting:CAP-RPT-007@0.1.1` | `GET /reports/settlements/{date}/download?operatorId=` → 200 SettlementDownloadLink | The report link | `VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND` |

Wire shapes from Reporting's contract (`D:\Project-learn-aws\Insight\Reporting\contracts\reporting\settlement.yaml`); no Reporting API in `02-Code-Projects` yet. Tested against `FakeBackend` only; not live-tested.
