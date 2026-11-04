# CAP-UI-OPR-001 — Operator application page

- Capability: CAP-UI-OPR-001
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/operator-onboarding/features/operator-application.md`
- Area: Operator
- Slice: `Features/Operator/Apply/`

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Labels derive from Identity's data
model: `email` → "Email", `label.user.password` → "Password", `full_name` → "Full name",
`label.user.phone` → "Mobile number", `company_name` → "Company name", `contact_email` →
"Contact email", `label.operator_application.gstin` → "GSTIN".

## Contract
- URL: `/operator/apply` — page name `/Operator/Apply/Apply`
- Auth: visitors only (a signed-in person is sent Home).
- Redirect on success: back to `/Operator/Apply/Apply` (PRG); the success state replaces the
  form. The email travels in TempData (encrypted cookie), never in the address. The visitor is not
  signed in.

## Form fields

| Field (Input.*) | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| Email | email | yes | "Email is required." (`validation.required`); "Enter a valid email." (`validation.format`) — also for more than 254 characters, Identity's limit. Hint: "You will use this email to sign in." |
| Password | password | yes | "Password is required."; "Password must be at least 10 characters and have at least one letter and one number." (`validation.password_policy`), checked as Identity does (ASCII letter + ASCII digit). Hint: "At least 10 characters, with at least one letter and one number." Never refilled. |
| FullName | text | yes | "Full name is required."; "Full name must be at least 2 characters." (`validation.min_length`); "Full name must be 100 characters or fewer." (`validation.max_length`). Counted like Identity (an emoji is one character). Hint: "Your own name." |
| Phone | tel | no — "(optional)" | "Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9." (`validation.phone`), pattern `^(\+91)?[6-9][0-9]{9}\z`. Empty is sent as `null`. |
| CompanyName | text | yes | "Company name is required."; "Company name must be at least 2 characters."; "Company name must be 150 characters or fewer." Counted like Identity. |
| ContactEmail | email | yes | "Contact email is required."; "Enter a valid contact email." (`validation.format`) — also for more than 254 characters. Hint: "Where RouteGo can reach your company. It can be the same as your sign-in email." |
| Gstin | text | yes | "GSTIN is required."; "Enter a valid 15-character GSTIN, for example 27AAPFU0939F1ZV." (`validation.gstin`), Identity's pattern `^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]\z`. Spaces removed and letters capitalised before checking and sending; a re-render shows the tidied value. Hint: "15 characters, for example 27AAPFU0939F1ZV." |

## Screen states

1. **Form (GET)** — title "Apply to sell bus tickets on RouteGo"; intro "Tell us about your bus
   company. After you verify your email, our team reviews your application."; the seven fields;
   "Send application"; "Your application was rejected before? Re-apply" → `/operator/reapply`.
2. **Submitting** — the submit button is disabled while the form is sent (site-wide `site.js`).
3. **Success (POST → 302 → GET)** — "Thank you for applying. We sent a verification link to
   {email}. Open the email and click the link. After you verify your email, our team will review
   your application and email you the result." + "Didn't get the email? Send the email again" →
   `/auth/resend-verification?email={email}`. {email} is the email in Identity's answer.
4. **Validation error (POST → 200)** — field messages above. Identity's `VALIDATION_FAILED` field
   errors show the same catalog wording; a full-name or company-name error whose value is within
   its limits shows Identity's own message; an error for a field the page does not show appears at
   the top with Identity's message. Values kept, password emptied.
5. **Email already registered** — `EMAIL_ALREADY_REGISTERED` → "This email is already registered.
   If it is your operator account and your application was rejected, re-apply instead. Otherwise,
   sign in." + "Re-apply" → `/operator/reapply` and "Sign in" → `/auth/login`.
6. **GSTIN already registered** — `GSTIN_ALREADY_REGISTERED` → on the GSTIN field: "This GSTIN is
   already registered with RouteGo. If you think this is wrong, contact RouteGo support."
   (`msg:problem.gstin_already_registered`).
7. **Service unavailable** — `msg:problem.upstream`. **Server error** — `msg:problem.internal_error`.
   Values kept, password emptied.

## Acceptance criteria

- [x] AC-1: application page offers the form — `Get_Visitor_ShowsFormAndReapplyLink`
- [x] AC-2: successful application — `Post_NewApplication_ShowsVerifyEmailWithoutSigningIn`
- [x] AC-3: GSTIN tidied before sending — `Post_GstinTypedInLowerCaseWithSpaces_SendsTidiedGstin`
- [x] AC-4: field rules checked before sending — `Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity`
- [x] AC-5: email already registered — `Post_EmailAlreadyRegistered_ShowsMessageWithReapplyAndSignInLinks`
- [x] AC-6: GSTIN already registered — `Post_GstinAlreadyRegistered_ShowsMessageOnGstinField`
- [x] AC-7: Identity unavailable — `Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues`
- [x] Validation error from Identity — `Post_IdentityFieldError_ShowsFieldMessageKeepsValues`
- [x] Server error — `Post_UnknownProblem_ShowsInternalError`
- [x] Email over Identity's 254-character limit — `Post_EmailLongerThan254Characters_ShowsFormatMessageWithoutCallingIdentity`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `applyAsOperator` | `routego-identity:CAP-OPR-001@0.1.0` | `POST /operators/applications` (anonymous) → 201 `OperatorApplication` | Creating the operator account and its application | `EMAIL_ALREADY_REGISTERED`, `GSTIN_ALREADY_REGISTERED` (409); 400 field errors |

Wire shapes taken from the built Identity API (`Features/OperatorOnboarding/ApplyAsOperator`).
