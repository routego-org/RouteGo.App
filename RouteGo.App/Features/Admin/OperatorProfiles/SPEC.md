# CAP-UI-ADM-006 — Operators page: search approved bus companies in Fleet

- Capability: CAP-UI-ADM-006
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/administration/features/fleet-operators.md` (feature spec 0.1.0)
- Area: Admin
- Slice: `Features/Admin/OperatorProfiles/` — `OperatorProfiles.Handler.cs` (query, wire records, handler),
  `OperatorProfiles.cshtml.cs` (search check, page model); tests in `OperatorProfiles.Tests.cs`

**Text provenance:**

| Words | Source |
|---|---|
| "Operators", "Company name", "Search", "See trips", "Showing {from}–{to} of {total} companies" | UI state "List" |
| Company name, Email, Rating, Approved at, Operator ID | AC-1 |
| "{rating} out of 5" (one decimal place) | business rule 4 |
| "1 Oct 2026, 9:30 PM IST" | design system formats (`IndiaTime`) |
| "A company you just approved can take a minute to appear here." | UI state "List" / business rule 5 (shown under the list, also when it is empty) |
| "No approved companies yet." | `msg:empty.collection` ({items} "approved companies") |
| "No companies match your search." | `msg:empty.search` ({items} "companies") |
| "Company name must be 150 characters or fewer." | `msg:validation.max_length` ({field} "Company name", {n} 150) |
| `msg:problem.forbidden`, `msg:problem.upstream`, `msg:problem.internal_error` | UI states |

## Contract
- URL: `/admin/operator-profiles` (by convention) — page name `/Admin/OperatorProfiles/OperatorProfiles`
- Auth: admins only (page access table: `/Admin/` prefix). An operator or customer sees the
  no-permission message (403) with no Fleet call; a visitor is sent to sign in.
- Address: `?companyName=&pageNumber=`. Fleet gets `companyName` only when one is typed, always
  `page` and `pageSize=20`.
- "See trips": `/admin/trips?operatorId={operatorId}&operatorName={company name}` (the name escaped, a
  space as `%20`).
- Page navigation: page numbers only, as on the other admin lists.

## Form fields

| Field | Type | Required | Validation message |
|---|---|---|---|
| companyName (search) | search, at most 150 characters | no — empty means every company | `msg:validation.max_length`, checked before the call; also when Fleet's `VALIDATION_FAILED` names `companyName` |

## Screen states

1. **List (GET)** — search box, table, showing line, pages, the "just approved" note.
2. **Empty — no search / search** — `msg:empty.collection` / `msg:empty.search`.
3. **Search invalid** — the length message on the field; no list, no call.
4. **Other refusal of the address** (e.g. a page number Fleet will not take) — Fleet's title at the top.
5. **Not allowed / unavailable / server error** — the message; the search text is kept.

## Acceptance criteria

- [x] AC-1: all companies — `Get_Admin_ListsEveryCompanyWithDetails`
- [x] AC-2: search by part of the name — `Get_SearchByPartOfName_SendsItAndKeepsItInAddress`
- [x] AC-3: see a company's trips — `Get_SeeTrips_LinksToTripSearchForThatCompany`
- [x] AC-4: no match — `Get_SearchMatchesNothing_ShowsSearchEmptyMessage`; no company at all — `Get_NoCompanyAtAll_ShowsCollectionEmptyMessage`
- [x] AC-5: search too long — `Get_SearchLongerThan150Characters_ShowsLengthMessageWithoutCallingFleet`, `Get_FleetRefusesCompanyName_ShowsLengthMessage`
- [x] AC-6: only admins — `Get_Operator_ShowsNoPermissionWithoutCallingFleet`
- [x] Not allowed — `Get_FleetForbids_ShowsForbiddenMessage`; service unavailable — `Get_FleetUnreachable_ShowsUpstreamMessageKeepsSearch`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchOperatorProfiles` | `routego-fleet:CAP-OPP-002@0.1.0` | `GET /admin/operatorProfiles?companyName&page&pageSize=20` (admin token) → 200 `{items, totalCount, page, pageSize}` | The list | `VALIDATION_FAILED` (`companyName` → length message; other → title), `FORBIDDEN` |

Wire shapes checked against the built Fleet API
(`RouteGo.Fleet.Api/Features/OperatorProfiles/SearchOperatorProfiles`, register status done); tested
against `FakeBackend` only, not live-tested.
