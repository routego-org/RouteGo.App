# CAP-UI-TRP-002 — Change a schedule (warn before cancelling trips)

- Capability: CAP-UI-TRP-002 (shared with `Features/Operator/NewSchedule/`)
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/trips/features/schedule-form.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/EditSchedule/` — `EditSchedule.Handler.cs` (wire records, handler), `EditSchedule.cshtml.cs`
  (page model), tests in `EditSchedule.Tests.cs` (opening) and `EditSchedule.Save.Tests.cs` (saving)
- Shared form: `Common/UI/ScheduleForm.cs`, `ScheduleFormFleet.cs`, `ScheduleFormRefusals.cs`,
  `Features/Shared/_ScheduleForm.cshtml` (see NewSchedule's SPEC.md for the fields, checks and refusal placement,
  which are the same here).

**Text provenance** (words beyond the shared form's, which NewSchedule's SPEC.md lists):

| Words | Source |
|---|---|
| "Change {schedule_name}" ("Change Mumbai → Pune via Lonavala, 10:30 PM IST") | UI state "Edit form"; trips area shared rule 2 |
| "Trips leaving in less than 6 hours keep their old fares." | UI state "Edit form" (business rule 6) |
| "(inactive)" after the schedule's own route or bus | business rule 2 |
| `msg:confirm.trips_will_be_cancelled`, "Save and change trips", "Go back" | UI state "Confirm trip changes" |
| `msg:problem.schedule_inactive` + "Back to the schedule" | UI state "Edit refused — stopped" |
| `msg:problem.bus_inactive` + "Open the bus" (page check on the schedule's inactive bus) | business rule 2, UI state "Bus inactive" |
| "Schedule saved. {c} new trips, {x} trips cancelled, {r} trips with new fares." / "Schedule saved. No trips needed to change." | UI states "Success — saved" / "Success — saved, no trip change" (shown on the schedule page) |
| `msg:problem.not_found` ({entity} "schedule") + "Back to my schedules" | UI state "Schedule not found" |
| "Show fares for this bus" | agreed with the product owner 2026-10-10 — not yet in the spec (as NewSchedule) |

## Contract
- URL: `/operator/schedules/{scheduleId}/edit` (absolute `@page`, fixed by the product) — page name `/Operator/EditSchedule/EditSchedule`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- Every draw reads `getSchedule`, then `listRoutes` + `listBuses` (`page=1&pageSize=200`, every status) and keeps
  the active ones plus the schedule's own route and bus (marked "(inactive)" when no longer active).
- GET fills the form from the schedule; `getBus` for the schedule's bus draws its fare boxes in the bus's order
  with the schedule's fares.
- "Show fares for this bus" → `?handler=ShowFares`; "Go back" → `?handler=Back` (both redraw, nothing sent).
- Save: the form is compared with the schedule as read again. A different route, bus, departure time or days
  and no `Confirmed` → the question; "Save and change trips" posts the whole form with `Confirmed=true`. Fares
  only, or nothing changed → saved without asking. `replaceSchedule` always gets every field.
- A schedule whose bus is now inactive cannot be saved with that bus: the page says so at Bus before sending.
- Redirect on success: `/Operator/Schedule/Schedule` (PRG) with the flash above.

## Screen states

1. **Edit form (GET)** — title, filled form, the fare-cutoff note, "Save schedule" / "Cancel" (to the schedule page).
2. **Edit refused — stopped** — inactive schedule (on read, or `SCHEDULE_INACTIVE` on save): message + link; no form.
3. **Confirm trip changes** — the question in place of the buttons; the form stays filled.
4. **Field invalid / refused by Fleet** — as NewSchedule; everything kept.
5. **Success** — the schedule page with the counts.
6. **Not found / not allowed / profile not ready** — the message (not found with the link); no form.

## Acceptance criteria

- [x] AC-6: change only the fares — `PostSave_FaresOnlyChanged_SavesWithoutAskingAndShowsCounts` (+ form filled:
  `Get_ActiveSchedule_FillsFormWithSavedDetails`)
- [x] AC-7: change the time — `PostSave_TimeChanged_AsksFirstAndSendsNothing`, `PostSave_TimeChangedAndConfirmed_SavesAndShowsCounts`;
  "Go back" keeps everything — `PostBack_FromQuestion_ReturnsToFormKeepingEverything`
- [x] AC-8: nothing changed — `PostSave_NothingChanged_SavesWithoutAsking`
- [x] AC-9: bus busy — `PostSave_BusTripOverlap_ShowsConflictAndKeepsEverything`
- [x] AC-11: stopped schedule — `Get_InactiveSchedule_ShowsStoppedWithLinkAndNoForm`, `PostSave_ScheduleStoppedMeanwhile_ShowsStoppedWithoutForm`
- [x] AC-12: route deactivated — `Get_ScheduleRouteDeactivated_OffersItMarkedAndChosen`, `PostSave_NothingChanged_SavesWithoutAsking("inactive")`
- [x] Rule 2: bus deactivated — `PostSave_ScheduleBusDeactivated_RefusedByPageWithLinkToBus`
- [x] Not found / not his / not ready — `Get_ScheduleNotFound_ShowsNotFoundWithLinkBack`, `Get_FleetRefusesSchedule_ShowsMessageWithoutForm`
- [x] Operator only — `Get_Customer_ShowsNoPermissionWithoutCallingFleet`
- AC-10 (fares do not match) and the other refusals are the shared form's, tested through NewSchedule.

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getSchedule` | `routego-fleet:CAP-TRP-003@0.1.0` | `GET /schedules/{scheduleId}` | Fill the form, compare on save | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `replaceSchedule` | `routego-fleet:CAP-TRP-004@0.1.0` | `PUT /schedules/{scheduleId}` ScheduleRequest → 200 ScheduleChange | Save | `SCHEDULE_INACTIVE`, `NOT_FOUND`, `FORBIDDEN` + every code of `createSchedule` (shared form) |
| `listRoutes` | `routego-fleet:CAP-RTE-003@0.1.0` | `GET /routes?page=1&pageSize=200` | Route choice | `OPERATOR_PROFILE_NOT_READY`, `FORBIDDEN`; `VALIDATION_FAILED` → internal error |
| `listBuses` | `routego-fleet:CAP-BUS-002@0.1.0` | `GET /buses?page=1&pageSize=200` | Bus choice | as above |
| `getBus` | `routego-fleet:CAP-BUS-003@0.1.0` | `GET /buses/{busId}` | Seat classes → fare boxes | any refusal → "We could not load this bus's seat classes. …" |

All calls carry the operator's access token. Not built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
