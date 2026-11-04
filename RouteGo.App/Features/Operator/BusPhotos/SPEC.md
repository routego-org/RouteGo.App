# CAP-UI-BUS-004 — Bus photos: add, order, delete

- Capability: CAP-UI-BUS-004
- Canonical spec: `spec-RouteGo.App` (routego-app 0.2.0) → `capabilities/buses/features/bus-photos.md` (feature spec 0.1.0);
  ADR `architecture/decisions/0004-bus-photos-sent-from-the-browser.md`
- Area: Operator
- Slice: `Features/Operator/BusPhotos/` — `BusPhotos.Handler.cs` (wire records, Fleet calls, the new
  order), `BusPhotos.Upload.cs` (the page script's step answers and their words), `BusPhotos.cshtml.cs`
  (page model), `_Photos.cshtml` (the photo section, also drawn alone for the script); tests in
  `BusPhotos.Tests.cs` (page, order, delete) and `BusPhotos.Upload.Tests.cs` (upload steps). The page script
  is `wwwroot/js/bus-photos.js`.

**Deviation from the spec (agreed with the product owner, 2026-10-10):** the spec puts the photo section on
the bus page. Here it is its **own page**, `/operator/buses/{busId}/photos`, so the slice keeps one page of
its own. The bus page (`Features/Operator/Bus/`) shows the photos read-only — "Photos", "{n} of 5 photos" or
the no-photos text, the cover marked "Cover photo" — and links here with "Manage photos". To add to
`bus-photos.md` in the spec repo.

**Text provenance:** every string comes from the canonical spec's `## UI states` / acceptance criteria, the
message catalog where the spec says `msg:<key>`, or words agreed with the product owner:

| Words | Source |
|---|---|
| "Photos of {bus name}" (page title), "Back to {bus name}", "Manage photos" (bus page link) | **agreed with product owner 2026-10-10** (not yet in spec) |
| "Photos", "{n} of 5 photos", "Cover photo" | UI state "Photos"; `label.bus.cover_photo` |
| "Move earlier", "Move later", "Make cover photo", "Delete", "Add photos" | UI state "Photos" |
| "JPEG or PNG, up to 3 MB each. The first photo is the one customers see first." | UI state "Photos" (hint) |
| "No photos yet. Customers trust a bus more when they can see it." | UI state "No photos" |
| "This bus has the most photos it can have (5). Delete one to add another." | UI state "Full" |
| "You can add {free} more photo(s). You chose {n}. Choose fewer files." — "photo" when {free} is 1 (AC-3), else "photos" | UI state "Too many files chosen" |
| "Waiting", "Sending…" | UI state "Uploading" |
| "{file_name} — added" | UI state "File added" |
| "{file_name} — not added: the file is larger than 3 MB." | UI state "File too large" |
| "{file_name} — not added: only JPEG and PNG photos can be used." | UI state "File wrong type" |
| "{file_name} — not added: the file is not the type its name says. Save it again as JPEG or PNG and try again." | UI state "File type mismatch" |
| "{file_name} — not added: the file could not be sent. Check your connection and try again." | UI state "File not sent" |
| "{file_name} — not added: sending the photo took too long. Please try again." | UI state "Upload too slow" |
| "{file_name} — not added: this bus already has 5 photos, or other photos are still being added. Wait 5 minutes or delete a photo." | UI state "Photo limit" |
| "{file_name} — not added: only JPEG and PNG photos up to 3 MB can be used." | UI state "File refused by Fleet" |
| "{file_name} — not added: this part of RouteGo is not available right now. Please try again in a few minutes." | UI state "Service unavailable" (for a file) |
| "{file_name} — not added: something went wrong on our side." | UI state "Server error" (for a file) |
| "Delete this photo? Customers will no longer see it.", "Delete photo", "Cancel" | UI state "Confirm delete" |
| "Photo deleted." | `msg:success.deleted` ({entity} "Photo") |
| "Photo order saved." | UI state "Success — order saved" |
| "The photos changed since you opened this page. We have loaded them again." | UI state "Photos changed" |
| "We couldn't find that bus. It may have been removed." + "Back to my buses" | `msg:problem.not_found` ({entity} "bus"); UI state "Bus not found" |
| "Your operator account is being set up. Please try again in a minute." | `msg:problem.operator_profile_not_ready` |
| "You don't have access to this." | `msg:problem.forbidden` |
| "This part of RouteGo is not available right now. Please try again in a few minutes." | `msg:problem.upstream` (order, delete) |
| "Something went wrong on our side. Try again — if it keeps happening, contact support with reference {trace_id}." | `msg:problem.internal_error` (order, delete) |

## Contract
- URL: `/operator/buses/{busId}/photos` (absolute `@page`, under the bus's fixed address) — page name
  `/Operator/BusPhotos/BusPhotos`
- Auth: signed-in Operator only (every `/Operator/` page except Apply/Reapply). Customer or admin:
  no-permission message (403), no Fleet call. Visitor: sent to sign in.
- **Order (no JavaScript):** each photo's buttons post `?handler=Order` with `photoId`, `move`
  (`earlier` | `later` | `cover`) and the order the page showed (`shownOrder`, every photo). The portal moves
  the photo within the **shown** order and sends the whole list, so Fleet can tell when the photos changed
  meanwhile (AC-10). Success → PRG to this page, flash "Photo order saved.".
- **Delete (no JavaScript):** "Delete" links to `?delete={photoId}`, which asks first (only for a photo the
  page shows); "Delete photo" posts `?handler=Delete` with `photoId`; "Cancel" links back. Success → PRG,
  flash "Photo deleted.".
- **Add photos (page script, ADR 0004):** the panel is drawn `hidden` and shown only by
  `wwwroot/js/bus-photos.js`; without JavaScript photos cannot be added. The script checks each file first
  (type from its first bytes — JPEG `FF D8 FF`, PNG signature; size ≤ 3,145,728 bytes; count ≤ free places),
  then for each good file in the order chosen:
  1. `POST ?handler=Slot` (`fileName`, `format`, `sizeBytes`) → the portal calls `createBusPhoto`;
  2. the browser sends the file to the answered `uploadUrl` with `uploadMethod` and exactly `contentType`,
     with no cookies;
  3. `POST ?handler=Confirm` (`photoId`, `fileName`, `attempt`) → `confirmBusPhoto`. On `PHOTO_NOT_UPLOADED`
     the first answer is `askAgain`; the script waits 2 s and asks with `attempt=2`, whose
     `PHOTO_NOT_UPLOADED` deletes the pending photo and reports "not sent".
  4. If step 2 fails: `POST ?handler=Discard` (`photoId`, `fileName`) → `deleteBusPhoto`, "not sent".
  Then `GET ?handler=Section` draws the photo section again (business rule 7).
- Step answers are JSON `{ outcome: send | askAgain | added | failed, message, stops, photoId, uploadUrl,
  uploadMethod, contentType }`. Every word is made on the server (Fleet's answers) or drawn into the page as
  `data-word-*` (the script's own checks); the script sets text only (`textContent`), never builds markup
  from the answers. `stops` (profile not ready, forbidden, bus not found) ends the run: files not yet tried
  are not sent.
- An upload address that is not an http(s) address is never handed to the browser: the pending photo is
  deleted and the file reported as a server error. The upload address is never logged.
- Security policy: `img-src` adds `PhotoDeliveryOrigin`, `connect-src` adds `PhotoUploadOrigin`
  (`Infrastructure/ConfigKeys.cs`; SSM `photo-delivery-origin` / `photo-upload-origin`; optional — left out,
  photos do not load or send; a set value that is not an http(s) origin stops the app at startup). Photo
  storage must also allow uploads from the portal's address (Fleet's deployment, ADR 0004).
- Fleet calls carry the session's access token (`IdentityRequests.WithUserToken`); Fleet's UNAUTHORIZED
  ends the session (shared rule 8). A script step that is redirected (session ended, form expired) makes the
  script follow the redirect.
- verb-justified: `OnPostDiscardAsync` and the second `OnPostConfirmAsync` send DELETE to free a place the
  browser could not fill — a cleanup, not a state the operator asked for.

## Form fields

| Field | Type | Sent by | Notes |
|---|---|---|---|
| `photoId`, `move`, `shownOrder` (repeated) | hidden + button value | Order forms | `move` from the pressed button |
| `photoId` | hidden | Delete question | |
| `fileName`, `format`, `sizeBytes` | script | Slot step | `format` jpeg or png, from the file's bytes |
| `photoId`, `fileName`, `attempt` | script | Confirm step | `attempt` 1 or 2 |
| `photoId`, `fileName` | script | Discard step | |

No local validation: the limits are Fleet's, and its refusals have their own words.

## Screen states

1. **Photos (GET)** — title "Photos of {bus name}"; "Photos"; "{n} of 5 photos"; each photo in order, the
   first marked "Cover photo"; "Move earlier" (not the first), "Move later" (not the last), "Make cover photo"
   (not the first), "Delete"; the "Add photos" panel with its hint when fewer than 5; "Back to {bus name}".
2. **No photos** — the no-photos text and "Add photos".
3. **Full** — the full text; no "Add photos".
4. **Too many files chosen / file checks / uploading / file results** — in the page script: each file listed
   "{name} — Waiting", then "{name} — Sending…", then its result line; "Add photos" disabled until all are
   done; then the photo section is drawn again.
5. **Confirm delete** — the question with "Delete photo" / "Cancel"; order buttons and "Add photos" hidden.
6. **Success — deleted / order saved (POST → 302)** — back to this page with the flash.
7. **Photos changed** — order `VALIDATION_FAILED` or delete `NOT_FOUND`: the message, photos read again.
8. **Bus not found** — `NOT_FOUND` on read or order: the message with "Back to my buses"; no photos.
9. **Not allowed / profile not ready** — `FORBIDDEN` / `OPERATOR_PROFILE_NOT_READY`: the message instead of
   the photos; for a file, the message and no further file is sent.
10. **Service unavailable / server error** — order or delete: the message and the photos read again (the
    delete question kept open); for a file: its "not added" line.

## Acceptance criteria

- [x] AC-1: photos shown in order — `Get_TwoPhotos_ShowsThemInOrderWithTheCoverFirst`
- [x] AC-2: add several photos (portal steps) — `PostSlot_GoodFile_AsksFleetWithFormatAndSizeAndAnswersWhereToSendIt`,
  `PostConfirm_FileArrived_AnswersAdded`; the browser's sending and the per-file loop are in the page script
  (not run by the .NET tests)
- [x] AC-3: free places — `Get_FewerThanFivePhotos_OffersAddPhotosThroughThePageScript` (data-free),
  `Get_Page_DrawsTheWordsThePageScriptShows`, `Get_FivePhotos_ShowsFullAndDoesNotOfferAddPhotos`; the count
  check itself is in the page script
- [x] AC-4: file checks first — words in `Get_Page_DrawsTheWordsThePageScriptShows`, size limit in
  `Get_FewerThanFivePhotos_OffersAddPhotosThroughThePageScript`; the check itself is in the page script
- [x] AC-5: one bad file does not stop the others — `PostDiscard_FileCouldNotBeSent_DeletesThePendingPhotoAndSaysNotSent`
- [x] AC-6: Fleet refuses the stored file — `PostConfirm_FleetRefusesTheStoredFile_SaysWhy`
- [x] AC-7: upload took too long — `PostConfirm_SlotExpired_SaysItTookTooLong`
- [x] Business rule 6 (not arrived: ask once more after 2 s, then free the place) —
  `PostConfirm_FileNotArrivedFirstTime_AsksTheScriptToAskAgain`, `PostConfirm_FileNotArrivedSecondTime_DeletesThePendingPhotoAndSaysNotSent`
- [x] Photo limit / refused / outage / stop states for a file — `PostSlot_FleetRefuses_SaysWhyAndWhetherToStop`,
  `PostSlot_FleetRefusesTheFileDetails_SaysOnlyJpegAndPngUpTo3Mb`, `PostSlot_UploadAddressNotAWebAddress_FreesThePlaceAndSendsNothing`
- [x] Business rule 7 (section drawn again) — `Get_Section_DrawsThePhotoSectionAloneFromAFreshRead`
- [x] AC-8: change the order — `Post_MakeCoverPhoto_SendsTheWholeNewOrderAndSaysSaved`,
  `Post_MovePhoto_SendsTheOrderWithThatPhotoMovedOnePlace`
- [x] AC-9: delete a photo — `Get_Delete_AsksFirstAndSendsNothing`, `Post_Delete_DeletesThePhotoAndSaysDeleted`
- [x] AC-10: photos changed meanwhile — `Post_OrderAfterPhotosChangedElsewhere_SaysChangedAndReadsThemAgain`,
  `Post_DeletePhotoAlreadyGone_SaysChangedAndReadsThemAgain`
- [x] Bus not found / refusals / outage — `Post_OrderForBusNotFound_ShowsNotFoundWithLinkBackAndNoPhotos`,
  `Get_FleetRefuses_ShowsMessageInsteadOfPhotos`, `Post_OrderFleetUnreachable_ShowsUpstreamAndThePhotosAsTheyAre`,
  `Post_DeleteFleetUnreachable_KeepsTheQuestionOpen`
- [x] Shared rule 8 (Fleet UNAUTHORIZED) — `Get_FleetAnswersUnauthorized_EndsSession`
- [x] Operator only — `Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet`
- [x] Security policy allows the photo addresses — `Foundation/HostTests.cs`
  `Get_AnyPage_AllowsBusPhotoAddressesFromSettings`, `ReadOrigin_*`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getBus` | `routego-fleet:CAP-BUS-003@0.1.0` | `GET /buses/{busId}` → 200 Bus (`name`, `photos[]`) | The page, the section | `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `createBusPhoto` | `routego-fleet:CAP-BUS-007@0.1.0` | `POST /buses/{busId}/photos` `{format, sizeBytes}` → 201 BusPhotoUploadSlot | Slot step | `VALIDATION_FAILED`, `PHOTO_LIMIT_REACHED`, `NOT_FOUND`, `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `confirmBusPhoto` | `routego-fleet:CAP-BUS-008@0.1.0` | `POST /buses/{busId}/photos/{photoId}/confirm` → 200 BusPhoto | Confirm step | `PHOTO_UPLOAD_EXPIRED`, `PHOTO_NOT_UPLOADED`, `PHOTO_INVALID` (`reason`), `FORBIDDEN`, `OPERATOR_PROFILE_NOT_READY` |
| `deleteBusPhoto` | `routego-fleet:CAP-BUS-009@0.1.0` | `DELETE /buses/{busId}/photos/{photoId}` → 204 | Delete, discard | `NOT_FOUND` (photos changed) |
| `replaceBusPhotoOrder` | `routego-fleet:CAP-BUS-010@0.1.0` | `PUT /buses/{busId}/photoOrder` `{photoIds}` → 200 BusPhotoList | Order | `VALIDATION_FAILED` (photos changed), `NOT_FOUND` |

Wire shapes from Fleet's contract (`contracts/buses/bus-photo.yaml`). None of these operations is built in
RouteGo.Fleet.Api yet — tested against `FakeBackend` only.
