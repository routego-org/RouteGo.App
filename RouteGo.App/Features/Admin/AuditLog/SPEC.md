# CAP-UI-ADM-011 — Audit log

- Capability: CAP-UI-ADM-011
- Canonical spec: `spec-RouteGo.App` (routego-app 0.6.0) → `capabilities/administration/features/audit-log.md` (feature spec 0.1.0, AC-1 to AC-13) and `capabilities/administration/README.md` (shared rules)
- Area: Admin
- Slice: `Features/Admin/AuditLog/` — `AuditLog.Handler.cs` (wire records, the two lookups, the words of a record), `AuditLog.cshtml.cs` (page model), `AuditLog.cshtml`; tests in `AuditLog.Tests.cs`; stubs in `RouteGo.App.Tests/Testing/AuditStubs.cs`

**Text provenance:**

| Words | Source |
|---|---|
| "Audit log", "Look up what was done to one item, or everything done on one day. Records cannot be changed or removed.", "Show records" | UI states, Lookup form; AC-10 |
| "An item" (Kind, Item ID), "A day" (Day) | UI states, rule 2 |
| When, Who ("{id}, {role}"), Action, Item ("{type} {id}"), Details ("key: value") | rule 4 (`msg:label.audit_record.*`, `msg:role.*`) |
| "{n} records." / "1 record.", "No records found." | UI states |
| `msg:validation.required` ("Item ID"), `msg:validation.format` ("day") | AC-7, AC-8 |
| **New wording:** the format message names the box "kind" when Kind is refused (a kind the page does not offer, or Audit's `entityType` refusal); the spec names no message for that case | — |

## Contract
- URL: `/admin/audit-log` (folder route). Admins only (`/Admin/` prefix); Audit checks again.
- **Two forms, never both**: "An item" (`kind`, `id`) and "A day" (`day`) are two GET forms, each sending only its own boxes, so the address is exactly `?kind=Booking&id=B-501` or `?day=2026-10-02` (plus `page` from page 2) and Audit's "both" and "neither" refusals cannot happen. This is the no-script shape of the spec's "Look up by" choice. The Day box opens on today (IST) when the address names no day; a `day` key present but empty is told it is not a date. When an address names both, the item wins.
- Kinds offered: Booking, Trip, Payment, Operator, Account, SigningKey, sent exactly as written; a kind not on the list is refused before sending.
- `GET /audit?entityType=&entityId=&page=&pageSize=20` or `?date=&page=&pageSize=20`. Records are shown oldest first as Audit returns them; page numbers only; the total is Audit's `totalCount`. Details are the publisher's keys and values, one line each, exactly as written (strings raw, other values as their JSON text).
- A Booking item's ID links to `/admin/bookings/{id}`, a Trip item's to `/admin/trips/{id}/seats`; other kinds show the ID alone. Role shows `msg:role.*` for Customer, Operator and Admin and as given otherwise ("System").
- Read only: no button, form or link on the page changes a record. `page` is read from the query by hand because Razor Pages reserves the name.

## Form fields
Kind (choice), Item ID (text); Day (date box).

## Screen states
1. **Lookup form** — no `kind`, `id` or `day` in the address.
2. **Records**, **No records**, **Item ID missing**, **Day invalid**, **Item invalid**.
3. **Forbidden / Audit unavailable / Server error**.

## Acceptance criteria

- [x] AC-1: a booking's history — `Get_BookingsHistory_ShowsTheCountAndEachRecordOldestFirstWithWhoWhatAndItem`
- [x] AC-2: look up an item — `Get_ItemLookup_ShowsDetailsExactlyAsWrittenAndAReadOnlyNote`, `Get_RecordWithoutDetails_ShowsNoDetails`
- [x] AC-3: look up a day — `Get_DayLookup_AsksForThatDayAndShowsItsRecords`
- [x] AC-4: day lookup opens on today — `Get_NoLookup_ShowsOnlyTheFormsAndTheDayBoxOpensOnToday`
- [x] AC-5: paging — `Get_SecondPage_AsksForThatPageAndKeepsTheLookupInEveryPageLink`
- [x] AC-6: nothing found — `Get_NothingFound_SaysNoRecordsFoundAndNoError`
- [x] AC-7: Item ID required — `Get_ItemWithNoItemId_ShowsRequiredOnTheBoxAndSendsNothing`, `Get_KindThatIsNotOffered_ShowsTheFormatMessageOnKindAndSendsNothing`
- [x] AC-8: the day must be a date — `Get_DayThatIsEmptyOrNotADate_ShowsTheFormatMessageAndSendsNothing`
- [x] AC-9: links from a record — `Get_RecordsOfABookingAndATrip_LinkTheIdToTheirPagesAndOtherKindsHaveNoLink`
- [x] AC-10: as written, read only — `Get_ItemLookup_ShowsDetailsExactlyAsWrittenAndAReadOnlyNote`
- [x] AC-11: only admins — `Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingAudit`, `Get_Visitor_IsSentToSignInWithTheLookupAsReturnAddress`
- [x] AC-12: Audit refuses — `Get_AuditForbids_ShowsForbidden`, `Get_AuditRefusesTheItem_ShowsTheMessageUnderTheBoxes`, `Get_AuditRefusesTheDay_ShowsTheFormatMessageUnderTheDay`
- [x] AC-13: Audit down — `Get_AuditDown_ShowsUpstreamKeepsTheTypedValuesAndTheMenu`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchAuditRecords` | `audit:CAP-AUD-002@0.1.2` | `GET /audit?…` → 200 AuditRecordListResponse | Both lookups | `VALIDATION_FAILED`, `FORBIDDEN` |

Wire shape from Audit's contract (`D:\Project-learn-aws\Insight\Audit\contracts`); no Audit API in `02-Code-Projects` yet. Tested against `FakeBackend` only; not live-tested.
