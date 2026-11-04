# CAP-UI-BUS-002 — Change a bus

- Capability: CAP-UI-BUS-002 (this slice: the "change" half; `Features/Operator/NewBus/` is the "add" half)
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/buses/features/bus-form.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/EditBus/` — `EditBus.cshtml.cs` (wire records, handler, page model); tests in `EditBus.Tests.cs`
- The form, its builder steps and its page checks are the shared bus form (see `Features/Operator/NewBus/SPEC.md`):
  `Features/Shared/_BusForm.cshtml` and `Common/UI/BusForm*.cs`. Their behaviour is tested once, through NewBus;
  this slice tests what differs: the form filled from Fleet, the full replace, and the change-only refusals.

**Text provenance:** the shared form's words are listed in NewBus's SPEC.md. This page adds:

| Words | Source |
|---|---|
| "Change {bus_name}" (title; the saved name, kept while the name box changes) | UI state "Edit form" |
| "Bus" (browser title when no bus is shown) | `label.bus.name` shown as "Bus" |
| "Bus updated." (flash, on the bus page) | `msg:success.updated` ({entity} "Bus") |
| "This bus has {n} upcoming trips. The next one leaves on {next_departure}. Stop its schedules or move them to another bus first." | `msg:problem.bus_in_use` |
| "You can still change the name, registration number, AC and amenities. To change the bus type, seat classes or seats, the bus must have no upcoming trips." | UI state "Bus in use" |
| "We couldn't find that bus. It may have been removed.", "Back to my buses" | `msg:problem.not_found` ({entity} "bus"), UI state "Bus not found" |

## Contract
- URL: `/operator/buses/{busId}/edit` (absolute `@page`, fixed by the product; linked from the bus page) — page
  name `/Operator/EditBus/EditBus`
- Auth: signed-in Operator only. Customer or admin: no-permission message (403), no Fleet call.
- GET reads the bus (`getBus`) and fills the form: details, seat classes, decks (the upper deck ticked when
  Fleet has one) and every seat. Builder steps post to `?handler=Build` on the same address and call nothing.
  The saved name travels in `SavedName`, so the title does not change while the name box does.
- "Save bus" sends every field (`replaceBus` is a full replacement), changed and unchanged.
- Redirect on success: `/Operator/Bus/Bus` with the same `busId` (PRG); flash "Bus updated."
- "Cancel" → the bus page `/operator/buses/{busId}`, without asking.
- Fleet calls carry the session's access token; Fleet's UNAUTHORIZED ends the session (shared rule 8).

verb-justified: `OnPostBuild` sends nothing to Fleet — a builder step only changes the form (see NewBus).

## Form fields

The shared bus form's fields and page checks — see `Features/Operator/NewBus/SPEC.md`.

## Screen states

1. **Edit loading / Edit form (GET)** — `getBus`; the form only when it answers, filled with the bus.
2. **Builder steps, questions, notices, page checks** — as on the add page; nothing sent.
3. **Success (POST → 302)** — `/operator/buses/{busId}` showing "Bus updated."
4. **Bus in use** — `BUS_IN_USE` on save: the two sentences at the top; everything entered kept.
5. **Registration taken** — the message at Registration number; everything kept.
6. **Field invalid from Fleet** — as on the add page.
7. **Profile not ready / Fleet down / server error on save** — the message at the top; everything kept.
8. **Bus not found / not allowed** — on GET: the message (not found: with "Back to my buses"), no form. On save
   (`NOT_FOUND`, `FORBIDDEN`): the same, and the form is no longer shown.
9. **Profile not ready / Fleet down on GET** — the message, no form.

## Acceptance criteria

- [x] AC-9: change a bus (form filled, every detail sent, "Bus updated.") — `Get_Operator_FillsFormWithSavedBus`,
  `PostSave_NameChanged_ReplacesEveryDetailAndShowsUpdatedBus`
- [x] AC-10: seats locked while the bus has upcoming trips — `PostSave_BusHasUpcomingTrips_ShowsTripsAndWhatCanChangeAndKeepsForm`
- [x] AC-8 on change: registration taken — `PostSave_RegistrationTaken_ShowsMessageAtRegistration`
- [x] Builder steps stay on this bus — `PostBuild_BuilderStep_StaysOnThisBusWithItsTitle`
- [x] Bus not found — `Get_BusNotFound_ShowsNotFoundWithLinkBackAndNoForm`, `PostSave_BusNotFound_ShowsNotFoundAndNoForm`
- [x] Not allowed / profile not ready / Fleet down on open — `Get_FleetRefuses_ShowsMessageAndNoForm`
- [x] Profile not ready on save keeps the form — `PostSave_ProfileNotReady_ShowsMessageAndKeepsForm`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `Get_FleetAnswersUnauthorized_EndsSession`
- [x] AC-12: operators only — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBus` | `routego-fleet:CAP-BUS-003@0.1.0` | `GET /buses/{busId}` (operator token) → 200 Bus | Fill the form | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `replaceBus` | `routego-fleet:CAP-BUS-004@0.1.0` | `PUT /buses/{busId}` `BusRequest` → 200 Bus | Save | `VALIDATION_FAILED`, `BUS_REGISTRATION_ALREADY_EXISTS` (409), `BUS_IN_USE` (409), `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |

Wire shapes from Fleet's contract (`contracts/buses/bus.yaml` `Bus` / `SeatLayout`, `BusRequest`). Neither
operation is built in RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
