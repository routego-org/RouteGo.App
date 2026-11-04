# CAP-UI-OPR-002 — Operator re-application page

- Capability: CAP-UI-OPR-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/operator-onboarding/features/operator-reapplication.md`
- Area: Operator
- Slice: `Features/Operator/Reapply/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Labels derive from Identity's data
model: `email` → "Email", `label.user.password` → "Password", `company_name` → "Company name",
`contact_email` → "Contact email", `label.operator_application.gstin` → "GSTIN". Dates follow the
design system: "2 Oct 2026, 4:00 PM IST", and a date without a time "2 Oct 2026".

## Contract
- URL: `/operator/reapply` — page name `/Operator/Reapply/Reapply`
- Auth: visitors only (a signed-in person is sent Home).
- Redirect on success: back to `/Operator/Reapply/Reapply` (PRG); the success state replaces the
  form. The earlier rejections travel as JSON in TempData (encrypted cookie). The company is not
  signed in.

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| Email | email | yes | "Email is required." (`validation.required`); "Enter a valid email." (`validation.format`) — also for more than 254 characters, Identity's limit. |
| Password | password | yes | "Password is required." (`validation.required`) only — the password proves who is applying, so no policy check. Never refilled. |
| CompanyName | text | yes | "Company name is required."; "Company name must be at least 2 characters." (`validation.min_length`); "Company name must be 150 characters or fewer." (`validation.max_length`). Counted like Identity (an emoji is one character). |
| ContactEmail | email | yes | "Contact email is required."; "Enter a valid contact email." (`validation.format`) — also for more than 254 characters. |
| Gstin | text | yes | "GSTIN is required."; "Enter a valid 15-character GSTIN, for example 27AAPFU0939F1ZV." (`validation.gstin`), Identity's pattern. Spaces removed and letters capitalised before checking and sending; a re-render shows the tidied value. Hint: "15 characters, for example 27AAPFU0939F1ZV." |

The company rules are copied from the Apply slice on purpose, not shared: `Common/` is at the
structure checker's budget (decision 2026-10-08). A rule change must touch both slices; both
slices' tests pin the rules.

## Screen states

1. **Form (GET)** — title "Re-apply as a bus operator"; intro "Was your application not approved?
   Enter your sign-in email and password, and your corrected company details. You can re-apply 7
   days after the rejection."; the five fields; "Send new application"; "First time? Apply as a
   bus operator" → `/operator/apply`.
2. **Submitting** — the submit button is disabled while the form is sent (site-wide `site.js`).
3. **Success (POST → 302 → GET)** — "Your new application is sent. Our team will review it and
   email you the result."; each earlier rejection (newest first, from `previousRejections`) as
   "Earlier rejection: {reason}" and "Rejected on {date}" (IST date); "Go to Home" → `/`.
4. **Validation error (POST → 200)** — field messages above; Identity's `VALIDATION_FAILED` field
   errors show the same wording (a company-name error within 2–150 characters shows Identity's
   own message; an unshown field shows Identity's message at the top). Password emptied.
5. **Wrong email or password** — `INVALID_CREDENTIALS` → `msg:problem.invalid_credentials`.
6. **Account locked** — `ACCOUNT_LOCKED` → `msg:problem.account_locked` with `lockedUntil` in IST.
7. **Too soon** — `REAPPLICATION_TOO_SOON` → "You can re-apply from {reapplyAllowedAt in IST}."
8. **Not a rejected operator** — `OPERATOR_NOT_REJECTED` by `currentStatus`:
   `pendingApproval` → "Your application is already waiting for review. We will email you when it
   is decided."; `pendingVerification` → "Please verify your email first. Check your inbox for the
   verification link." + "Send the link again" → `/auth/resend-verification?email={typed email}`;
   `active` → "This account can already sign in. There is nothing to re-apply for." + "Sign in" →
   `/auth/login`; `suspended` → `msg:problem.account_suspended`.
9. **GSTIN already registered** — on the GSTIN field: `msg:problem.gstin_already_registered`.
10. **Service unavailable** — `msg:problem.upstream`. **Server error** — `msg:problem.internal_error`.
    A timed refusal missing its time, or an unknown `currentStatus`, falls back to the internal
    error. Values kept, password emptied, in every refusal.

## Acceptance criteria

- [x] AC-1: re-application page offers the form — `Get_Visitor_ShowsFormAndApplyLink`
- [x] AC-2: successful re-application — `Post_RejectedLongEnoughAgo_ShowsSentWithEarlierRejections`
- [x] AC-3: too soon — `Post_TooSoon_ShowsAllowedDateInIstKeepsValues`
- [x] AC-4: wrong email or password — `Post_WrongEmailOrPassword_ShowsInvalidCredentialsEmptiesPassword`
- [x] AC-5: account locked — `Post_AccountLocked_ShowsUnlockTimeInIst`
- [x] AC-6 / AC-7 (+ not verified, suspended): `Post_OperatorNotRejected_ShowsMessageForCurrentStatus`
- [x] AC-8: GSTIN held by another operator — `Post_GstinAlreadyRegistered_ShowsMessageOnGstinField`
- [x] AC-9: field rules checked before sending — `Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity`
- [x] Validation error from Identity — `Post_IdentityFieldError_ShowsFieldMessageKeepsValues`
- [x] Service unavailable — `Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues`
- [x] Server error — `Post_UnknownProblem_ShowsInternalError`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `reapplyAsOperator` | `routego-identity:CAP-OPR-002@0.1.0` | `POST /operators/reapplications` (anonymous) → 201 `OperatorApplication` | Sending the new application to review | `INVALID_CREDENTIALS` (401), `ACCOUNT_LOCKED` (403, `lockedUntil`), `OPERATOR_NOT_REJECTED` (409, `currentStatus`), `GSTIN_ALREADY_REGISTERED` (409), `REAPPLICATION_TOO_SOON` (422, `reapplyAllowedAt`); 400 field errors |

Wire shapes taken from the built Identity API (`Features/OperatorOnboarding/ReapplyAsOperator`).
