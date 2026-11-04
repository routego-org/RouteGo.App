# CAP-UI-PRF-001 — My profile page

- Capability: CAP-UI-PRF-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/profile/features/my-profile.md`
- Area: Profile
- Slice: `Features/Profile/Profile/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Labels derive from Identity's `user`
data model: `email` → "Email", `role` → "Role", `full_name` → "Full name", `label.user.phone` →
"Mobile number".

## Contract
- URL: `/profile` — declared absolutely (`@page "/profile"`): the product fixes the address (the
  "My profile" menu link and the spec's surface), so it is not the convention's `/profile/profile`.
  Page name `/Profile/Profile/Profile`.
- Auth: any signed-in person (page access table: signed in).
- Redirect on success: back to `/Profile/Profile/Profile` (PRG); Flash: "Profile updated."
  (`msg:success.updated`, {entity} "Profile"). The GET after the redirect loads the profile from
  Identity again, so the fields show what Identity now holds (the mobile number in `+91` form).

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| FullName | text | yes | "Full name is required." (`validation.required`); "Full name must be at least 2 characters." (`validation.min_length`); "Full name must be 100 characters or fewer." (`validation.max_length`). Length counted as Identity counts it (an emoji is one character). |
| Phone | tel | no — "(optional)" | "Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9." (`validation.phone`), Identity's exact pattern `^(\+91)?[6-9][0-9]{9}\z`. Empty is sent as `"phone": null`, which removes it. |

Email and Role are shown as text, never as inputs.

## Screen states

1. **Profile (GET)** — title "My profile"; Email and Role (`msg:role.customer` / `role.operator` /
   `role.admin`) as text, hint "Email and role cannot be changed."; Full name and Mobile number
   "(optional)" filled with the current values; "Save changes".
2. **Saving** — the submit button is disabled while the form is sent (site-wide `site.js`).
3. **Success (POST → 302 → GET)** — `msg:success.updated` "Profile updated."; fields show the
   values Identity returns.
4. **Validation error (POST → 200)** — field messages above; Identity's `VALIDATION_FAILED` field
   errors show the same catalog wording (a full-name error whose value is within 2–100 characters
   shows Identity's own message); an error for a field the page does not show appears at the top
   with Identity's message. Typed values kept. Email and Role come from the session (they cannot
   change), so no second Identity call is made.
5. **Load unavailable (GET)** — `msg:problem.upstream`; no form, no email/role.
6. **Save unavailable (POST)** — `msg:problem.upstream`; typed values kept.
7. **Session ended** — `UNAUTHORIZED` after renewal → `/auth/login?returnUrl=%2Fprofile&reason=session-ended`
   (`msg:session.ended`), handled by the shared session pipeline.
8. **Server error** — `msg:problem.internal_error` with the trace id; typed values kept (on GET: no form).

## Acceptance criteria

- [x] AC-1: view my profile — `Get_SignedIn_ShowsProfileWithFixedEmailAndRole`
- [x] AC-2: save changes — `Post_ChangedValues_SavesAndShowsReturnedValues`
- [x] AC-3: remove the mobile number — `Post_EmptiedMobileNumber_SendsNoPhoneAndShowsEmptyField`
- [x] AC-4: field rules checked before sending — `Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity`
- [x] AC-5: profile cannot be loaded — `Get_IdentityUnreachable_ShowsUpstreamMessageAndNoForm`
- [x] AC-6: session ended while saving — `Post_IdentityAnswersUnauthorized_EndsSession`
- [x] Validation error from Identity — `Post_IdentityFieldError_ShowsFieldMessageKeepsValues`
- [x] Save unavailable — `Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues`
- [x] Server error — `Post_UnknownProblem_ShowsInternalError`
- [x] Full-name length counted like Identity — `Post_FullNameOf100Emoji_IsCountedLikeIdentityAndSent`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getMyProfile` | `routego-identity:CAP-PRF-001@0.1.0` | `GET /me` (access token) → 200 `Profile` | Loading the page | `UNAUTHORIZED` (session ended) |
| `replaceMyProfile` | `routego-identity:CAP-PRF-002@0.1.0` | `PUT /me` (access token), body `{fullName, phone}` with `phone` always present → 200 `Profile` | Saving | `VALIDATION_FAILED` field errors; `UNAUTHORIZED` (session ended) |

Wire shapes taken from the built Identity API (`Features/Profile/GetMyProfile`,
`Features/Profile/ReplaceMyProfile`). Identity refuses a body with no `phone` field at all, so the
page always sends it (as `null` to remove the number).
