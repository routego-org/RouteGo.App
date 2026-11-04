# CAP-UI-AUTH-003 — Resend verification page

- Capability: CAP-UI-AUTH-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/authentication/features/email-verification.md`
- Area: Auth
- Slice: `Features/Auth/ResendVerification/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`; waiting times and dates follow the
design system's *Voice & tone*. Label: `email` → "Email".

## Contract
- URL: `/auth/resend-verification?email={email}` — page name `/Auth/ResendVerification/ResendVerification`
- Auth: visitors only. `email` in the query prefills the field.
- Redirect on success: back to the page (PRG); the confirmation replaces the form (carried in TempData).

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| Email | email | yes | "Email is required." (`validation.required`); "Enter a valid email." (`validation.format`) |

## Screen states

1. **Resend form** — title "Send the verification email again"; "Enter the email you registered
   with. We will send you a new verification link."; Email (prefilled); "Send verification email";
   "Back to sign in" → `/auth/login`.
2. **Resend success** — "We sent a new verification link to {email}. It works until
   {link_expires_at}. Links in older emails no longer work." (`linkExpiresAt` in IST) + "Back to sign in".
3. **Validation error** — field message above (also for Identity's `VALIDATION_FAILED`).
4. **Email not registered** — "No account uses this email. Check the spelling, or create an
   account." + "Create an account" → `/auth/register`; email kept.
5. **Email already verified** (`accountStatus` not `pendingApproval`) — "This email is already
   verified. You can sign in." + "Sign in" → `/auth/login`.
6. **Already verified — operator waiting** — "This email is already verified. Your operator
   application is waiting for review."
7. **Asked too often** — "You asked for a new link a short time ago. You can ask again in {wait}."
   with `retryAfterSeconds` in words ("30 seconds", "19 hours 40 minutes"); the action is disabled
   for that long (`data-wait-seconds`, wwwroot/js/site.js). Without a usable `retryAfterSeconds`
   the shared internal-error message is shown.
8. **Service unavailable / server error** — `msg:problem.upstream` / `msg:problem.internal_error`; email kept.

## Acceptance criteria

- [x] AC-9: resend form, prefilled — `Get_WithEmail_PrefillsForm`
- [x] AC-10: new link sent — `Post_Registered_ShowsNewLinkSentWithExpiryInIst`
- [x] AC-11: no account for the email — `Post_EmailNotRegistered_ShowsMessageWithRegisterLinkKeepsEmail`
- [x] AC-12: email already verified (+ operator waiting) — `Post_EmailAlreadyVerified_ShowsMessageByAccountStatus`
- [x] AC-13: asked too often — `Post_AskedTooOften_ShowsWaitAndHoldsTheAction`, `Post_AskedTooOftenForHours_ShowsWaitInWords`
- [x] AC-14: malformed email — `Post_MalformedEmail_ShowsFormatMessageWithoutCallingIdentity`
- [x] Service unavailable — `Post_IdentityUnreachable_ShowsUpstreamKeepsEmail`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `resendVerificationEmail` | `routego-identity:CAP-AUTH-003@0.1.0` | `POST /auth/verificationEmails` → 200 `VerificationEmailSent` | Sending a new link | `EMAIL_NOT_REGISTERED`, `EMAIL_ALREADY_VERIFIED` (+`accountStatus`), `RATE_LIMITED` (+`retryAfterSeconds`); 400 field errors |

Identity has not built this endpoint yet; built and tested against `contracts/authentication/email-verification.yaml`.
