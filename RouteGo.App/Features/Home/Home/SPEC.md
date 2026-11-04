# CAP-UI-HOME-001 — Home page and role menu

- Capability: CAP-UI-HOME-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/home/features/home-and-navigation.md`
- Area: Home
- Slice: `Features/Home/Home/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`.

## Contract
- URL: `/` — page name `/Home/Home/Home`. Declared absolutely (`@page "/"`): the product fixes
  the root address (brand link, "Go to Home" links, the sign-in default), so it is not the
  convention's `/home/home`.
- Auth: everyone.
- GET, and `POST /` for the bus search form (spec 0.3.0, AC-13): every home page shows the shared search form
  (`Features/Shared/_SearchForm.cshtml`, rules in `Common/UI/SearchForm*.cs`, spec in
  `Features/Search/BusSearch/SPEC.md`). "Search" posts to `/` — a failed check draws Home again at `/`
  with the field messages (and, for a city typed but not picked, Search's matching cities to choose);
  passed checks redirect to `/bus-search?…`. Suggestions as you type come from `/bus-search?handler=Cities`.
  Tests: `Home.Search.Tests.cs`.
  verb-justified: `OnPostAsync` writes nothing — it is a POST so a failed check keeps the address `/` (bus-search
  AC-7); the shareable address is the `/bus-search` GET it redirects to. The role menu itself lives in `Features/Shared/_Layout.cshtml` (it is on every page);
  its words and order are this capability's and are tested here.

## Screen states

1. **Visitor home** — title "Welcome to RouteGo"; "Book intercity bus tickets in minutes.";
   actions "Sign in" → `/auth/login` (primary), "Create an account" → `/auth/register`,
   "Apply as a bus operator" → `/operator/apply`. No Identity call.
2. **Signed-in home** — title "Home"; "Welcome, {full_name}." and "You are signed in as {role}."
   with {role} = "Customer" / "Operator" / "Admin" (`msg:role.customer` / `msg:role.operator` /
   `msg:role.admin`). The full name comes from `getMyProfile` on every visit.
3. **Signed-in home, name unavailable** — any answer other than a profile (outage, problem,
   server error): "Welcome, {email}." with the session's email; no error message.
4. **Session ended on home** — `UNAUTHORIZED` from the profile call: sign-in page with
   `msg:session.ended` (done by the session code, tested in `SessionRenewalTests`).
5. **Role menu — visitor** — "Sign in", "Register", "Apply as operator".
6. **Role menu — customer / operator** — "Signed in as {email}"; "Home", "My profile", "Sign out".
7. **Role menu — admin** — "Signed in as {email}"; "Home", "My profile", "Users", "Create admin",
   "Operator applications", "Email log", "Signing keys", "Sign out".

## Acceptance criteria

- [x] AC-1: visitor home, visitor menu, no Identity call — `Get_Visitor_ShowsWaysInWithoutCallingIdentity`
- [x] AC-2: customer — `Get_Customer_ShowsWelcomeAndCustomerMenu`
- [x] AC-3: operator — `Get_Operator_ShowsWelcomeAndOperatorMenu`
- [x] AC-4: admin — `Get_Admin_ShowsWelcomeAndAdminMenu`
- [x] AC-5: name cannot be loaded — `Get_ProfileUnavailable_WelcomesByEmailWithoutError`
- [x] AC-13: the search form on every home page, menus unchanged — `Get_Visitor_ShowsSearchFormWithTodayWithoutCallingSearch`, `Get_SignedIn_ShowsSearchFormWithWelcome` (menus: AC-1..AC-4 above)
- [x] The profile call carries the person's token and the correlation id — `Get_ProfileCall_SendsAccessTokenAndCorrelationId`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getMyProfile` | `routego-identity:CAP-PRF-001@0.1.0` | `GET /me` (Bearer) → 200 `Profile` | The full name in the welcome | `UNAUTHORIZED` → session ended; anything else → email fallback |

Identity has not built this endpoint yet; built and tested against `contracts/profile/`.
