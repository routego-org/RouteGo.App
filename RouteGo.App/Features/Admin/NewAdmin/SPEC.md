# CAP-UI-ADM-002 — Create admin page

- Capability: CAP-UI-ADM-002
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/administration/features/admin-account-creation.md`
- Area: Admin
- Slice: `Features/Admin/NewAdmin/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Labels derive from Identity's `user`
data model: `email` → "Email", `password` → "Password", `full_name` → "Full name",
`label.user.phone` → "Mobile number" + "(optional)". `success.created` is kept in this slice (not
`Common/`, which is at the structure checker's budget).

## Contract
- URL: `/admin/users/new` — page name `/Admin/NewAdmin/NewAdmin`. **Absolute route**: the address
  is fixed by the spec (the users page and the admin menu link to it), while the slice folder is
  named for its action.
- Auth: admins only (page access table: `/Admin/` prefix). A customer or operator sees the
  no-permission message (403) with no Identity call; a visitor is sent to sign in.
- Redirect on success: `/Admin/Users/Users` with `email` = the email Identity returned →
  `/admin/users?email={email}`; Flash: "Admin account created." (`msg:success.created`, {entity}
  "Admin account"). The framework writes the `@` of the email unescaped in the query
  (`?email=support2@routego.in`, which RFC 3986 allows); it is the same address as the spec's
  `?email=support2%40routego.in`, and the test compares the decoded address.
- The password is never sent back to the browser: every re-render clears it.

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| Email | email | yes | "Email is required." (`validation.required`); "Enter a valid email." (`validation.format`) |
| Password | password | yes | "Password is required." (`validation.required`); "Password must be at least 10 characters and have at least one letter and one number." (`validation.password_policy`) |
| FullName | text | yes | "Full name is required."; "Full name must be at least 2 characters." (`validation.min_length`); "Full name must be 100 characters or fewer." (`validation.max_length`) |
| Phone | tel | no | "Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9." (`validation.phone`) |

The rules are Identity's authentication shared rules 1–3 (the same as registration); the page
checks them first and Identity checks them again.

## Screen states

1. **Form (GET)** — title "Create admin"; the four fields; hint under Password: "RouteGo does not
   email this password. Give it to the new admin yourself. They cannot change it later.";
   "Create admin" and a link "Back to users" → `/admin/users`.
2. **Submitting** — the button is disabled while sent (site-wide `site.js`).
3. **Success (POST → 302)** — to `/admin/users?email={email}` with "Admin account created.".
4. **Validation error (POST → 200)** — the page's own check or Identity's `VALIDATION_FAILED`: the
   field message above on the failing field. Typed values kept except the password.
5. **Email already used** — `EMAIL_ALREADY_REGISTERED`: the Email field shows "An account already
   uses this email. Each email can have only one account and one role.". Typed values kept except
   the password.
6. **Not allowed / unavailable / server error** — `FORBIDDEN`: "You don't have access to this."
   (`msg:problem.forbidden`); unreachable: `msg:problem.upstream`; anything else:
   `msg:problem.internal_error`. Typed values kept except the password.

## Acceptance criteria

- [x] AC-1: the form — `Get_Admin_ShowsFormHintAndBackLink`
- [x] AC-2: admin created — `Post_ValidAdmin_CreatesAndShowsAccountInUsersList`
- [x] AC-3: email already used — `Post_EmailAlreadyRegistered_ShowsMessageOnEmailAndKeepsValuesExceptPassword`
- [x] AC-4: field rules checked before sending — `Post_FieldBreaksRule_ShowsMessageWithoutCallingIdentity`
- [x] Validation error from Identity — `Post_IdentityRefusesField_ShowsCatalogMessageOnField`
- [x] Service unavailable — `Post_IdentityUnreachable_ShowsUpstreamMessageAndKeepsValues`
- [x] Not for other roles — `Get_Customer_ShowsNoPermissionWithoutCallingIdentity`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `createAdmin` | `routego-identity:CAP-AUTH-008@0.1.0` | `POST /auth/admin/users` `{email, password, fullName, phone}` (admin token) → 201 account | Create | `EMAIL_ALREADY_REGISTERED`; 400 field errors; `FORBIDDEN` |

Wire shape taken from the built Identity API (`Features/Authentication/CreateAdmin`).
