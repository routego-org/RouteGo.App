# CAP-UI-ADM-004 — Signing keys page

- Capability: CAP-UI-ADM-004
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/administration/features/signing-keys.md`
- Area: Admin
- Slice: `Features/Admin/SigningKeys/` — GET-only view

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Column labels derive from the JSON
Web Key members the spec names: `kid` → "Key ID", `kty` → "Key type", `alg` → "Algorithm",
`use` → "Use".

## Contract
- URL: `/admin/signing-keys` — page name `/Admin/SigningKeys/SigningKeys`
- Auth: admins only (page access table: `/Admin/` prefix), by product decision although the key
  set is public. A customer or operator sees the no-permission message (403) with no Identity
  call; a visitor is sent to sign in.
- The key set is fetched **without** the admin's token: Identity publishes it anonymously, and the
  page does not depend on session renewal.
- No form, no action; nothing is cached — every visit shows what Identity publishes at that moment.

## Screen states

1. **Keys (GET)** — title "Signing keys"; text "These are the public keys other RouteGo services
   use to check that a sign-in pass really comes from RouteGo Identity. They contain no secrets.
   Keys are changed by the platform engineer, not on this page."; table Key ID, Key type,
   Algorithm, Use — one row per key in Identity's order. The key numbers (`n`, `e`) are not shown.
2. **No keys** — "Identity is not publishing any keys right now. Sign-in will not work until it
   does. Tell the platform engineer."
3. **Service unavailable** — "This part of RouteGo is not available right now. Please try again in
   a few minutes." (`msg:problem.upstream`)
4. **Server error** — "Something went wrong on our side. Try again — if it keeps happening,
   contact support with reference {traceId}." (`msg:problem.internal_error`)
5. **Not for other roles** — "You don't have permission to open this page."
   (`msg:access.no_permission`), no Identity call.

## Acceptance criteria

- [x] AC-1: keys listed — `Get_Admin_ListsPublishedKeysInIdentitysOrder`
- [x] AC-2: no actions, says who changes keys — `Get_Admin_OffersNoActionAndSaysWhoChangesKeys`
- [x] AC-3: not for other roles — `Get_Customer_ShowsNoPermissionWithoutCallingIdentity`
- [x] AC-4: Identity unavailable — `Get_IdentityUnreachable_ShowsUpstreamMessage`
- [x] No keys state — `Get_NoKeysPublished_TellsAdminToCallPlatformEngineer`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `getSigningKeys` | `routego-identity:CAP-KEY-001@0.1.0` | `GET /.well-known/jwks.json` (anonymous) → 200 `{keys: [{kty, use, alg, kid, n, e}]}` | The table | (none — shared rules) |

Wire shape taken from the built Identity API (`Features/SigningKeys/GetSigningKeys`).
