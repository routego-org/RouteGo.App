# CAP-UI-ADM-001 — Users page: search, suspend and reactivate

- Capability: CAP-UI-ADM-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/administration/features/user-search-and-suspension.md`
- Area: Admin
- Slice: `Features/Admin/Users/` — `Users.cshtml.cs` (query, input, validators, page model) and
  `Users.Handler.cs` (wire records, Identity calls); tests in `Users.Tests.cs` (list, access) and
  `Users.Actions.Tests.cs` (suspend, reactivate).

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Labels derive from Identity's `user`
data model: `email` → "Email", `full_name` → "Full name", `label.user.phone` → "Mobile number",
`role` → "Role", `status` → "Status", `created_at` → "Created at",
`label.account_suspension.reason` → "Reason". Role words are `role.*`; status words are `state.*`
(kept in this slice, not `Common/`, because `Common/` is at the structure checker's budget).

## Contract
- URL: `/admin/users` — page name `/Admin/Users/Users`
- Auth: admins only (page access table: `/Admin/` prefix). A customer or operator sees the
  no-permission message (403) with no Identity call; a visitor is sent to sign in.
- Address: `?email=&role=&status=&pageNumber=` holds the list; `&suspend={userId}` or
  `&reactivate={userId}` opens the confirmation. `pageNumber`, not `page`: Razor Pages reserves the
  `page` route value. Identity is still sent `page` and `pageSize=20`.
- Confirmation: a separate confirm view (a box above the list) reached by a link, so it works
  without JavaScript. It is shown only when the account is on the loaded page and still allows
  the change. "Cancel" is a link back to the same list (no change is sent).
- Redirect after a change (success or "changed meanwhile" refusal): back to the list with the same
  filters and page (PRG); the message is a flash.
- Page navigation: page numbers only — the catalog has no words for "Previous"/"Next" (decision
  2026-10-08).

## Form fields

| Field | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| email (filter) | search | no | "Enter a valid email." (`validation.format`) over 254 characters, or when Identity refuses it |
| role (filter) | select | no — "Any role" | Customer / Operator / Admin (`role.*`); "Enter a valid role." if Identity refuses it |
| status (filter) | select | no — "Any status" | `state.pending_verification`, `state.pending_approval`, `state.active`, `state.rejected`, `state.suspended`; "Enter a valid status." if Identity refuses it |
| Input.Reason | textarea | yes | "Reason must be between 10 and 500 characters." (`validation.reason_length`), counted like Identity (an emoji is one character) |

## Screen states

1. **List (GET)** — title "Users"; filters; "Search"; "Create admin" → `/admin/users/new`;
   columns Email, Full name, Mobile number, Role, Status, Created at (IST); "Showing {from}–{to} of
   {total} accounts"; page numbers when more than one page.
2. **Empty** — no filter: "No accounts yet." (`empty.collection`); filtered: "No accounts match
   your search." (`empty.search`).
3. **Actions** — "Suspend" only for an active customer or operator; "Reactivate" only for a
   suspended account.
4. **Confirm suspend** — "Suspend {email}? They will be signed out within 15 minutes and cannot
   sign in until an admin reactivates them." + Reason + "Suspend account" / "Cancel".
5. **Confirm reactivate** — "Reactivate {email}? They will be able to sign in again." + Reason +
   "Reactivate account" / "Cancel".
6. **Working** — the confirm button is disabled while sent (site-wide `site.js`).
7. **Success** — "{email} is suspended. They will be signed out within 15 minutes." / "{email} is
   active again. They can sign in now." ({email} from Identity's answer); list reloaded.
8. **Validation error** — reason message; the confirmation stays open with the typed reason.
9. **Changed meanwhile** (flash, list reloaded) — `USER_NOT_ACTIVE`: "This account is no longer
   active. It is now {state}. The list has been updated."; `USER_NOT_SUSPENDED`: "This account is
   not suspended. It is now {state}. The list has been updated."; `ADMIN_CANNOT_BE_SUSPENDED`:
   "Admin accounts cannot be suspended."; `NOT_FOUND`: "We couldn't find that account. It may have
   been removed." (`problem.not_found`).
10. **Not allowed / unavailable / server error** — `FORBIDDEN`, `msg:problem.upstream`,
    `msg:problem.internal_error`: on a change the confirmation and reason are kept; on the list the
    filters are kept and no list is shown.

## Acceptance criteria

- [x] AC-1: first view lists all accounts — `Get_Admin_ListsAllAccountsNewestFirstWithFilters`
- [x] AC-2: search with filters — `Get_WithFilters_SendsThemAndKeepsThemInTheForm`
- [x] AC-3: nothing matches (+ no filters) — `Get_NothingMatches_ShowsEmptyMessage`
- [x] AC-4: actions only where allowed — `Get_MixedAccounts_OffersActionsOnlyWhereAllowed`
- [x] AC-5: suspend with a reason — `Post_SuspendWithReason_SuspendsAndReloadsList`
- [x] AC-6: reason too short — `Post_ReasonTooShort_ShowsMessageWithoutCallingIdentity`
- [x] AC-7: cancel does nothing — `Get_Cancel_ReturnsToListWithoutChangingAnything`
- [x] AC-8: reactivate with a reason — `Post_ReactivateWithReason_ReactivatesAndReloadsList`
- [x] AC-9: someone else acted first (+ not suspended, admin, not found) — `Post_AccountChangedMeanwhile_TellsWhyAndReloadsList`
- [x] AC-10: paging — `Get_SecondPage_SendsSameFiltersAndShowsRange`
- [x] Service unavailable (list) — `Get_IdentityUnreachable_ShowsUpstreamMessageKeepsFilters`
- [x] Service unavailable (change) — `Post_IdentityUnreachable_KeepsConfirmationAndReason`
- [x] Filter refused by Identity — `Get_IdentityRefusesFilter_ShowsFormatMessageOnField`
- [x] CAP-UI-HOME-002 AC-7 wrong role — `Get_Customer_ShowsNoPermissionWithoutCallingIdentity`
- [x] CAP-UI-HOME-002 AC-8 visitor — `Get_Visitor_IsSentToSignInWithReturnAddress`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchUsers` | `routego-identity:CAP-AUTH-007@0.1.0` | `GET /auth/admin/users?email&role&status&page&pageSize=20` (admin token) → 200 `{items, totalCount, page, pageSize}` | The list | 400 filter errors; `UNAUTHORIZED` (session ended), `FORBIDDEN` |
| `suspendUser` | `routego-identity:CAP-AUTH-009@0.1.0` | `POST /auth/admin/users/{userId}/suspend` `{reason}` → 200 account | Suspend | `USER_NOT_ACTIVE` (`currentStatus`), `ADMIN_CANNOT_BE_SUSPENDED`, `NOT_FOUND`; 400 reason |
| `reactivateUser` | `routego-identity:CAP-AUTH-010@0.1.0` | `POST /auth/admin/users/{userId}/reactivate` `{reason}` → 200 account | Reactivate | `USER_NOT_SUSPENDED` (`currentStatus`), `NOT_FOUND`; 400 reason |

Wire shapes taken from the built Identity API (`Features/Authentication/SearchUsers`,
`SuspendUser`, `ReactivateUser`).
