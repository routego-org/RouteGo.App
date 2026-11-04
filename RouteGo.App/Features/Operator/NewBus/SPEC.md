# CAP-UI-BUS-002 — Add a bus (with the seat layout builder)

- Capability: CAP-UI-BUS-002 (this slice: the "add" half; `Features/Operator/EditBus/` is the "change" half)
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/buses/features/bus-form.md` (feature spec 0.1.0)
- Area: Operator
- Slice: `Features/Operator/NewBus/` — `NewBus.cshtml.cs` (wire record, handler, page model); tests in
  `NewBus.Tests.cs` (open, save, Fleet's answers, access) and `NewBus.Builder.Tests.cs` (builder steps, page checks)
- Shared with EditBus (user decision 2026-10-10): the form markup `Features/Shared/_BusForm.cshtml` (in Shared,
  not `Features/Operator/`, because a partial there compiles into the `Features.Operator` namespace and trips
  the architecture test's slice-isolation rule), and in
  `Common/UI/`: `BusForm.cs` (the posted form and its seat operations), `BusFormBuilder.cs` (builder steps),
  `BusFormChecks.cs` (page checks), `BusFormFleetErrors.cs` (where Fleet's field errors go), `BusFormModels.cs` (outcome, question,
  the partial's model, the request Fleet takes). Test driver: `RouteGo.App.Tests/Testing/BusFormDriver.cs`.

**Text provenance:** every string comes from the canonical spec's `## UI states` / acceptance criteria /
business rules, the message catalog where the spec says `msg:<key>`, a catalog label, or words agreed with
the product owner (marked):

| Words | Source |
|---|---|
| "Add a bus" (title), "Save bus", "Cancel", "Add a seat class", "Fill with seats", "This bus has an upper deck", "No aisle", Rows, Columns, Aisle after column | UI state "New form" |
| "Fill a deck with seats, then remove, rename or change single seats." | UI state "New form" |
| Bus name, AC, Seat number | `label.bus.name`, `label.bus.is_ac`, `label.bus_seat.label` |
| Registration number, Bus type, Amenities, Seat classes, Seat class | derived from Fleet's attribute names |
| Seater / Sleeper / Semi-sleeper; Wi-Fi … Restroom; Yes / No; Lower deck / Upper deck | `bus_type.*`, `amenity.*`, `flag.yes_no`, `deck.*` |
| "Remove" (seat, seat class), "Add a seat" (empty place) | business rules 4 and 6 |
| "Use for the whole deck" (button), "Seat class" (its choice) | **agreed with the product owner 2026-10-10** (business rule 4 gives no words) — to be added to bus-form.md |
| "{seat_class}: {n} seats" (count per class) | **agreed with the product owner 2026-10-10** (business rule 7 gives no format) — to be added to bus-form.md |
| "Seats: {n} of 60" | business rule 7 / AC-1 |
| "Replace all seats on the {deck}? Your changes to these seats will be lost.", "Replace seats" | UI state "Confirm refill" |
| "Remove the upper deck and its {n} seats?", "Remove upper deck" | UI state "Confirm remove upper deck" |
| "{n} seats outside the new size were removed." | UI state "Seats removed by resize" |
| "A bus can have at most 60 seats. You have {n}. Remove {extra} seats." | UI state "Too many seats" |
| "{seat_class} still has {n} seats. Change their class first." | UI state "Seat class still in use" |
| "{seat_class} has no seats. Give it at least one seat, or remove it." | UI state "Seat class without seats" |
| "Add at least one seat." | UI state "No seats" |
| "Seat number {label} is used twice." | UI state "Seat name taken" |
| "Seat number must be 1 to 5 letters or numbers, like 1A or U12." | UI state "Seat name invalid" |
| "Seat class {seat_class} is listed twice." | UI state "Field invalid" |
| "Registration number must have 6 to 15 letters and numbers. Spaces and hyphens are allowed." | UI state "Field invalid" |
| "{Field} is required.", "… must be at least {n} characters.", "… must be {n} characters or fewer.", "Aisle after column must be between 1 and {n}.", "Enter a valid {field}." | `msg:validation.required`, `.min_length`, `.max_length`, `.range`, `.format` |
| "This registration number is already registered with RouteGo. If this bus is yours, contact RouteGo support." | UI state "Registration taken" |
| "Bus created." (flash, on the bus page) | `msg:success.created` ({entity} "Bus") |
| "Your operator account is being set up. Please try again in a minute." | `msg:problem.operator_profile_not_ready` |
| "You don't have access to this." | `msg:problem.forbidden` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` |

## Contract
- URL: `/operator/buses/new` (absolute `@page`, fixed by the product; linked from the bus list) — page name
  `/Operator/NewBus/NewBus`
- Auth: signed-in Operator only (every `/Operator/` page except Apply/Reapply). Customer or admin:
  no-permission message (403), no Fleet call. Visitor: sent to sign in.
- No page script: every builder button posts the whole form to `?handler=Build` with its step in
  `BuilderAction` (`fill:lower`, `refill:upper`, `add-seat:lower:6:3`, `remove-seat:12`, `deck-class:upper`,
  `add-class`, `remove-class:1`, `remove-upper`, `keep-upper`, `redraw`), and the page is drawn again with
  everything typed. "Save bus" posts to the page's own address; a hidden first "Save bus" button makes the
  Enter key save. "This bus has an upper deck" shows or hides the upper deck with CSS only.
- Every post first brings the form up to date: a renamed seat class is renamed on its seats (the names as
  last drawn travel in `SeatClassesBefore`), seats outside a smaller deck are removed (with the notice), and
  an unticked upper deck that still has seats asks first. "Save bus" waits (draws the page again, sends
  nothing) while a notice or a question is showing.
- Redirect on success: `/Operator/Bus/Bus` with the new `busId` → `/operator/buses/{busId}` (PRG); flash
  "Bus created."
- Fleet calls carry the session's access token (`IdentityRequests.WithUserToken`); Fleet's UNAUTHORIZED
  ends the session (shared rule 8).

verb-justified: `OnPostBuild` sends nothing to Fleet — a builder step only changes the form, which must be
posted so nothing typed is lost (the page keeps nothing between requests).

## Form fields

| Field (Input.*) | Type | Page check (message) |
|---|---|---|
| `Name` | text | required; 2–80 characters (`validation.min_length` / `.max_length`) |
| `RegistrationNumber` | text | required; letters, digits, spaces, hyphens; 6–15 letters and digits; at most 30 characters ("Registration number must have …") |
| `BusType` | radio (seater, sleeper, semiSleeper) | required |
| `IsAc` | radio (Yes / No) | required |
| `Amenities` | checkboxes (the eight) | — |
| `SeatClasses[i]` (+ hidden `SeatClassesBefore[i]`) | text, 1–5 | required; 2–30 characters; not twice ignoring capitals; at least one seat |
| `HasUpperDeck` | checkbox | — |
| `Lower.*` / `Upper.*` (`Rows`, `Columns`, `AisleAfterColumn`, `ClassForAll`) | choices | rows and columns required (lower always, upper when ticked); aisle 1 to columns − 1 |
| `Seats[i].Label` / `.SeatClass` (+ hidden `.Deck`, `.Row`, `.Column`) | text / choice | 1–5 letters or digits; not twice ignoring capitals; class one of the bus's |
| (all seats) | — | at least 1, at most 60 |

## Screen states

1. **New form (GET)** — every field empty, one seat class box, both decks' controls, the hint, "Seats: 0 of 60".
2. **Deck filled / single seats changed / class set for a deck (POST Build → 200)** — the deck drawn with
   its seats (rule 3 names, first seat class); counts updated; nothing sent.
3. **Confirm refill / confirm remove upper deck** — the question with its confirm and "Cancel" (Cancel keeps
   everything; for the upper deck it ticks the box again).
4. **Seats removed by resize** — the notice; "Save bus" waits for the next press.
5. **Too many seats** — the message under the count (also refused on save).
6. **Page checks fail (POST → 200)** — each message at its field or seat; nothing sent; everything kept.
7. **Saving** — buttons disabled while sent (site-wide `site.js`).
8. **Success (POST → 302)** — `/operator/buses/{busId}` showing "Bus created."
9. **Registration taken** — the message at Registration number; everything kept.
10. **Field invalid from Fleet** — `VALIDATION_FAILED`: `name`, `registrationNumber`, `seatClasses[i]`,
    `layout.seats[i].label` and `layout.seats[i].seatClass` at their field in the page's words; any other
    field (a deck, the seat list) → the problem's title at the top. Everything kept.
11. **Profile not ready / not allowed / Fleet down / server error** — the message at the top; everything kept.

## Acceptance criteria

- [x] New form — `Get_Operator_ShowsEmptyFormWithEveryField`
- [x] AC-1: fill a deck — `PostBuild_FillLowerDeck_NamesSeatsRowByRowWithFirstClass`, `PostBuild_FillWithoutSize_ShowsRowsAndColumnsRequired`
- [x] AC-2: fill the upper deck, one class for the whole deck — `PostBuild_FillUpperDeckThenWholeDeckClass_NamesWithUAndSetsClass`
- [x] AC-3: change single seats — `PostBuild_ChangeSingleSeats_RemovesRenamesReclassesAndSuggestsName`
- [x] AC-4: fill again asks first — `PostBuild_FillDeckThatHasSeats_AsksFirstThenReplaces`, `PostBuild_CancelRefill_KeepsChangedSeats`
- [x] AC-5: making a deck smaller — `PostSave_DeckMadeSmaller_RemovesSeatsOutsideAndSaysHowMany`
- [x] AC-6: page checks before saving — `PostSave_PageCheckFails_ShowsMessageAndSendsNothing` (9 cases)
- [x] Business rule 5 (untick the upper deck) — `PostSave_UpperDeckUnticked_AsksThenRemovesItsSeats`
- [x] Business rule 6 (seat classes in use / renamed) — `PostBuild_RemoveSeatClassWithSeats_RefusesAndSaysHowMany`, `PostBuild_RenameSeatClass_RenamesItOnEverySeat`
- [x] AC-7: save a new bus — `PostSave_ValidBus_SendsEveryDetailAndShowsCreatedBus`, `PostSave_ValidBus_FlashesBusCreatedOnBusPage`
- [x] AC-8: registration already in RouteGo — `PostSave_RegistrationTaken_ShowsMessageAtRegistrationAndKeepsForm`
- [x] AC-11: field errors from Fleet — `PostSave_FleetRejectsSeatClass_ShowsMessageAtThatSeat`, `PostSave_FleetRejectsFieldNotOnForm_ShowsTitleAtTop`
- [x] Profile not ready / not allowed / Fleet down — `PostSave_FleetRefuses_ShowsMessageAtTopAndKeepsForm`, `PostSave_FleetUnreachable_ShowsUpstreamAndKeepsForm`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `PostSave_FleetAnswersUnauthorized_EndsSession`
- [x] AC-12: operators only — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`
- AC-9 and AC-10 (change a bus, seats locked) belong to `Features/Operator/EditBus/`.

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `createBus` | `routego-fleet:CAP-BUS-001@0.1.0` | `POST /buses` (operator token) `BusRequest` → 201 Bus | Save | `VALIDATION_FAILED`, `BUS_REGISTRATION_ALREADY_EXISTS` (409), `OPERATOR_PROFILE_NOT_READY`, `FORBIDDEN` |

Wire shape from Fleet's contract (`contracts/buses/bus.yaml` `BusRequest` / `SeatLayout`; only `busId` of the
201 `Bus` is read). `createBus` is in progress in RouteGo.Fleet.Api — tested against `FakeBackend` only.
