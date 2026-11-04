# CAP-UI-AUTH-002 — Verify-email page

- Capability: CAP-UI-AUTH-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/authentication/features/email-verification.md` (+ ADR 0003)
- Area: Auth
- Slice: `Features/Auth/Verify/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`; "Go to
Home" is `msg:access.go_home`.

## Contract
- URL: `/auth/verify?token={token}` — page name `/Auth/Verify/Verify`. Identity writes this
  address into every verification link (portal-context.md); changing it breaks emails already sent.
- Auth: everyone (signed in or not). A customer's success replaces any session in the browser.
- **Verify on click only:** the GET never calls Identity. The form posts to `/auth/verify`
  without the token in the address; the token travels in a hidden field, unchanged.
- The token is never logged: the framework's request lines (which carry the query) are off
  (`Program.cs`).

## Form fields

| Field (Input.*) | Type | Required | Validation message |
|---|---|---|---|
| Token | hidden | yes | none of its own — a missing token shows the "link incomplete" state |

## Screen states

1. **Verify ready (GET with token)** — title "Verify your email"; "Click the button below to verify
   your email address."; action "Verify my email". No Identity call.
2. **Link incomplete** (no token, or `VALIDATION_FAILED`) — "This verification link is not
   complete. Open the link from your email again, or ask for a new one." + "Send me a new link" →
   `/auth/resend-verification`. No action offered.
3. **Success — customer** (`active` + token pair) — session started; redirect `/` with flash "Your
   email is verified. Welcome to RouteGo."
4. **Success — operator** (`pendingApproval`, no tokens) — no session; PRG back to the page, which
   shows "Your email is verified. Our team will now review your operator application. We will
   email you when it is decided." + "Go to Home" → `/`.
5. **Link expired** — "This verification link has expired." + "Send me a new link".
6. **Already used** (`accountStatus` not `pendingApproval`) — "Your email is already verified. You
   can sign in." + "Sign in" → `/auth/login`.
7. **Already used — operator waiting** — "Your email is already verified. Your operator application
   is waiting for review." + "Go to Home".
8. **Link not valid** — "This verification link is not valid. It may have been replaced by a newer
   link. Use the link in the newest email, or ask for a new one." + "Send me a new link".
9. **Service unavailable / server error** — `msg:problem.upstream` / `msg:problem.internal_error`;
   the action stays offered (the link is still unused).

States 5–8 are final for the link, so "Verify my email" is no longer offered there (the spec
names no action for them).

## Acceptance criteria

- [x] AC-1: opening the link does not verify — `Get_WithToken_OffersVerifyWithoutCallingIdentity`
- [x] AC-2: customer verifies and is signed in — `Post_Customer_StartsSessionAndGoesHomeWithWelcome`
- [x] AC-3: operator verifies and waits — `Post_Operator_ShowsReviewMessageWithoutSession`
- [x] AC-4, AC-7: expired / replaced link — `Post_LinkRefused_ShowsMessageWithNewLinkOffer`
- [x] AC-5, AC-6: link already used — `Post_LinkAlreadyUsed_ShowsMessageByAccountStatus`
- [x] AC-8: address without a token — `Get_WithoutToken_ShowsIncompleteWithoutVerifyAction`
- [x] Service unavailable — `Post_IdentityUnreachable_ShowsUpstreamAndKeepsVerifyAction`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `verifyEmail` | `routego-identity:CAP-AUTH-002@0.1.0` | `POST /auth/emailVerifications` → 200 `EmailVerificationResult` | Using the link | `VERIFICATION_LINK_EXPIRED`, `VERIFICATION_LINK_ALREADY_USED` (+`accountStatus`), `VERIFICATION_LINK_INVALID`; 400 → incomplete |

verb-justified: the POST is the person's own "Verify my email" click (ADR 0003), not the GET.
Identity has not built this endpoint yet; built and tested against `contracts/authentication/email-verification.yaml`.
