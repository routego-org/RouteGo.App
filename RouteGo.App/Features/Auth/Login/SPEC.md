# CAP-UI-AUTH-004 — Sign-in page

- Capability: CAP-UI-AUTH-004
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/authentication/features/sign-in.md`
- Area: Auth
- Slice: `Features/Auth/Login/`

**Text provenance:** every user-facing string below is projected, never authored — from the
canonical spec's `## UI states`, or from `design/message-catalog.md` when the spec references
`msg:<key>` (recorded as words + key). Field labels derive from Identity's `user` data model
(`email` → "Email"; `label.user.password` → "Password").

## Contract
- URL: `/auth/login` — page name `/Auth/Login/Login`
- Auth: visitors only (a signed-in person is sent to `/` — page access, home-and-navigation AC-10)
- Query: `returnUrl` (where to go after success; kept through failed attempts), `reason`
  (`sign-in-required` | `session-ended`, set by page access and by the session code)
- Redirect on success: `returnUrl` when it is a portal path (starts with a single `/`), otherwise `/`. No flash.
- Side effect on success: a session is started from Identity's token pair
  (`Common/UI/PortalSession.cs`), replacing any session in the browser.

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| Email | email | yes | "Email is required." (`msg:validation.required`); "Enter a valid email." (`msg:validation.format`) |
| Password | password | yes | "Password is required." (`msg:validation.required`) — never refilled |

`{field}` inside a sentence is written in lower case ("Enter a valid email."); at the start of
a sentence it keeps its label's capital ("Email is required.").

## Screen states

1. **Form (GET)** — title "Sign in"; Email, Password, "Sign in"; links "New to RouteGo? Create an
   account" → `/auth/register`, "Didn't get the verification email?" → `/auth/resend-verification`,
   "Are you a bus company? Apply as a bus operator" → `/operator/apply`.
2. **Arrived — sign-in required** (`reason=sign-in-required`) — "Please sign in to continue."
   (`msg:session.sign_in_required`) above the form.
3. **Arrived — session ended** (`reason=session-ended`) — "Your session has ended. Please sign in
   again." (`msg:session.ended`) above the form.
4. **Validation error (POST → 200)** — field messages above; Identity's `VALIDATION_FAILED` field
   errors show the same field messages. Email kept, password emptied. Identity is not called for a
   locally invalid form.
5. **Success (POST → 302)** — session started; redirect as in Contract.
6. **Refusals (POST → 200, page-level alert, email kept, password emptied):**
   - `INVALID_CREDENTIALS` → "The email or password is incorrect." (`msg:problem.invalid_credentials`)
   - `EMAIL_NOT_VERIFIED` → "Please verify your email before signing in. Check your inbox for the
     verification link." + link "Send the link again" → `/auth/resend-verification?email={email}`
   - `ACCOUNT_PENDING_APPROVAL` → "Your operator application is waiting for review. We will email
     you when it is decided."
   - `ACCOUNT_REJECTED` → "Your operator application was not approved. The email we sent you gives
     the reason. You can apply again 7 days after the rejection." + link "Re-apply" → `/operator/reapply`
   - `ACCOUNT_SUSPENDED` → "Your account is suspended. Please contact RouteGo support." (`msg:problem.account_suspended`)
   - `ACCOUNT_LOCKED` → "Your account is locked for a short time because of too many wrong
     passwords. Try again after {locked_until}." (`msg:problem.account_locked`), `lockedUntil` in IST
7. **Service unavailable** — "This part of RouteGo is not available right now. Please try again in a
   few minutes." (`msg:problem.upstream`).
8. **Server error** — "Something went wrong on our side. Try again — if it keeps happening, contact
   support with reference {trace_id}." (`msg:problem.internal_error`), `traceId` or the correlation id.

## Acceptance criteria

- [x] AC-1: form and links — `Get_Visitor_ShowsFormAndLinks`
- [x] AC-2: success goes Home with a session (HttpOnly, Secure, SameSite=Lax) — `Post_RightPassword_StartsSessionAndGoesHome`
- [x] AC-3: success returns to the wanted page — `Post_RightPasswordWithReturnUrl_GoesToReturnUrl`
- [x] AC-4: wrong email or password — `Post_InvalidCredentials_ShowsMessageKeepsEmailEmptiesPassword`
- [x] AC-5: email not verified — `Post_EmailNotVerified_ShowsMessageWithResendLink`
- [x] AC-6: operator waiting — `Post_AccountPendingApproval_ShowsWaitingMessage`
- [x] AC-7: operator rejected — `Post_AccountRejected_ShowsMessageWithReapplyLink`
- [x] AC-8: suspended — `Post_AccountSuspended_ShowsSuspendedMessage`
- [x] AC-9: locked, time in IST — `Post_AccountLocked_ShowsLockEndInIst`
- [x] AC-10: arriving after the session ended — `Get_SessionEnded_ShowsSessionEndedMessage` (+ `Get_SignInRequired_ShowsSignInRequiredMessage`)
- [x] AC-11: malformed input checked before sending — `Post_MalformedInput_ShowsFieldMessagesWithoutCallingIdentity`
- [x] Shared rules: Identity field error, unreachable, unknown problem — `Post_IdentityFieldError_ShowsFieldMessage`, `Post_IdentityUnreachable_ShowsUpstreamMessageKeepsEmail`, `Post_UnknownProblem_ShowsInternalErrorWithTraceId`
- [x] home-and-navigation AC-9 (foreign return address) and AC-10 (signed in → `/`) — `Post_ReturnUrlOnAnotherSite_GoesHome`, `Get_SignedIn_GoesHome`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `login` | `routego-identity:CAP-AUTH-004@0.1.0` | `POST /auth/sessions` → 200 `TokenPair` | Starting the session | `INVALID_CREDENTIALS`, `EMAIL_NOT_VERIFIED`, `ACCOUNT_PENDING_APPROVAL`, `ACCOUNT_REJECTED`, `ACCOUNT_SUSPENDED`, `ACCOUNT_LOCKED` (+`lockedUntil`); 400 field errors |

Identity has not built this endpoint yet (its register row is `planned`); the slice is built and
tested against the contract in `contracts/authentication/session.yaml`.
