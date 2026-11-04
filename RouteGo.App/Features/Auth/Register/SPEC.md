# CAP-UI-AUTH-001 — Customer register page

- Capability: CAP-UI-AUTH-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/authentication/features/customer-registration.md`
- Area: Auth
- Slice: `Features/Auth/Register/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Labels derive from Identity's `user`
data model: `email` → "Email", `full_name` → "Full name", `label.user.password` → "Password",
`label.user.phone` → "Mobile number".

## Contract
- URL: `/auth/register` — page name `/Auth/Register/Register`
- Auth: visitors only.
- Redirect on success: back to `/Auth/Register/Register` (PRG); the success state replaces the
  form. The created email travels in TempData (encrypted cookie), never in the address. The
  visitor is not signed in.

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| Email | email | yes | "Email is required." (`validation.required`); "Enter a valid email." (`validation.format`) |
| Password | password | yes | "Password is required." (`validation.required`); "Password must be at least 10 characters and have at least one letter and one number." (`validation.password_policy`). Hint: "At least 10 characters, with at least one letter and one number." Never refilled. |
| FullName | text | yes | "Full name is required."; "Full name must be at least 2 characters." (`validation.min_length`); "Full name must be 100 characters or fewer." (`validation.max_length`) |
| Phone | tel | no — "(optional)" | "Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9." (`validation.phone`). Empty is sent as `null`. |

## Screen states

1. **Form (GET)** — title "Create your account"; the four fields; "Create account"; links "Already
   have an account? Sign in" → `/auth/login`, "Are you a bus company? Apply as a bus operator" →
   `/operator/apply`.
2. **Success (POST → 302 → GET)** — "Your account is created. We sent a verification link to
   {email}. Open the email and click the link to activate your account. The link works for 24
   hours." + "Didn't get the email? Send the email again" → `/auth/resend-verification?email={email}`.
   {email} is the email in Identity's UserSummary.
3. **Validation error (POST → 200)** — field messages above. Identity's `VALIDATION_FAILED`
   field errors show the same catalog wording; a full-name error whose value is within 2–100
   characters shows Identity's own message (no catalog rule fits it); an error for a field the
   page does not show appears at the top with Identity's message. Values kept, password emptied.
4. **Email already registered** — `EMAIL_ALREADY_REGISTERED` → "This email is already registered.
   Sign in instead." (`msg:problem.email_already_registered`) + "Sign in" → `/auth/login`.
5. **Service unavailable** — `msg:problem.upstream`. **Server error** — `msg:problem.internal_error`.

## Acceptance criteria

- [x] AC-1: form and links — `Get_Visitor_ShowsFormAndLinks`
- [x] AC-2: successful registration — `Post_NewEmail_ShowsCheckYourEmailWithoutSigningIn`
- [x] AC-3: mobile number optional — `Post_NoMobileNumber_SendsNoPhone`
- [x] AC-4: field rules checked before sending — `Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity`
- [x] AC-5: email already registered — `Post_EmailAlreadyRegistered_ShowsMessageKeepsValuesEmptiesPassword`
- [x] AC-6: field error from Identity — `Post_IdentityFieldError_ShowsFieldMessageKeepsValues`
- [x] AC-7: Identity unavailable — `Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues`
- [x] Server error — `Post_UnknownProblem_ShowsInternalError`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `registerCustomer` | `routego-identity:CAP-AUTH-001@0.1.0` | `POST /auth/registrations` → 201 `UserSummary` | Creating the customer account | `EMAIL_ALREADY_REGISTERED`; 400 field errors |

Identity has not built this endpoint yet; built and tested against `contracts/authentication/user.yaml`.
