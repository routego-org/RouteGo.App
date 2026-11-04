# CAP-UI-AUTH-006 — Sign-out page

- Capability: CAP-UI-AUTH-006
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/authentication/features/session-and-sign-out.md`
- Area: Auth
- Slice: `Features/Auth/Logout/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`;
"You have signed out." is `msg:success.signed_out`.

## Contract
- URL: `/auth/logout` — page name `/Auth/Logout/Logout`
- Auth: any signed-in person; a visitor is sent to sign in with `msg:session.sign_in_required`.
- Sign-out is a protected form submit, from this page or from the role menu's "Sign out" (a small
  form in `_Layout.cshtml`), never a plain link.
- Redirect on success: `/` with flash "You have signed out." (`msg:success.signed_out`).

## Screen states

1. **Sign-out confirm (GET)** — title "Sign out"; "Do you want to sign out?"; action "Sign out";
   link "Cancel" → `/`. Nothing changes until the action is chosen.
2. **Signing out (POST)** — `logout` is called with the session's refresh token and a valid access
   token: an expired access token is renewed first (the session code fills the body with the
   refresh token current after that renewal). If the renewal is refused, the call is skipped.
3. **Sign-out success** — whatever Identity answers (204, `UNAUTHORIZED`, `VALIDATION_FAILED`, an
   outage, a server error): the session is removed, redirect `/` as a visitor with the flash.
4. **Sign-out while signed out** — page access sends the visitor to `/auth/login` with the return
   address and `msg:session.sign_in_required`.

## Acceptance criteria

- [x] AC-6: sign out from the role menu — `PostFromRoleMenu_SignedIn_RevokesAndArrivesHomeSignedOut`
- [x] AC-7: sign out works even when Identity does not — `Post_IdentityRefusesOrIsDown_StillSignsOut`, `Post_RenewalRefused_SkipsRevokeAndSignsOut`
- [x] AC-8: typing the address does not sign out — `Get_SignedIn_AsksBeforeSigningOut`
- [x] AC-9: other browsers stay signed in — `Post_OneBrowser_OtherBrowserStaysSignedIn`
- [x] Sign-out while signed out (home-and-navigation AC-6) — `Get_Visitor_SentToSignInWithReturnAddress`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `logout` | `routego-identity:CAP-AUTH-006@0.1.0` | `POST /auth/sessions/revoke` (Bearer) → 204 | Revoking this browser's refresh token chain | none — every answer signs out |
| `refreshSession` | `routego-identity:CAP-AUTH-005@0.1.0` | `POST /auth/sessions/refresh` | Renewing an expired access token first (session code) | `REFRESH_TOKEN_INVALID` / `REFRESH_TOKEN_REUSED` → skip the call |

Identity has not built these endpoints yet; built and tested against `contracts/authentication/session.yaml`.
