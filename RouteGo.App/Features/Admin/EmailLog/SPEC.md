# CAP-UI-ADM-005 — Email log page: search the send log and resend

- Capability: CAP-UI-ADM-005
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/administration/features/email-log.md`
- Area: Admin
- Slice: `Features/Admin/EmailLog/` — `EmailLog.cshtml.cs` (query, input, validator, page model) and
  `EmailLog.Handler.cs` (wire records, Notification calls, the resend log line); tests in
  `EmailLog.Tests.cs` (search, access, key problem) and `EmailLog.Actions.Tests.cs` (resend).

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Column labels are the spec's (AC-1);
email types are `email_type.*`, statuses `state.sending` / `state.sent` / `state.failed`, failure
reasons `failure_reason.*`, "Resend of an earlier email" is `label.send_log_entry.resend_of` (all kept
in this slice, not `Common/`, because `Common/` is at the structure checker's budget).

## Contract
- URL: `/admin/email-log` — page name `/Admin/EmailLog/EmailLog`
- Auth: admins only (page access table: `/Admin/` prefix). A customer or operator sees the
  no-permission message (403) with no Notification call; a visitor is sent to sign in. This is the
  only role check: Notification checks the internal key, not the person (ADR 0002).
- Notification client: `HttpClients.Notification`, base URL `ConfigKeys.NotificationUrl` (SSM
  `notification-url`; local `http://localhost:5007/`). Chain: correlation id → `InternalKeyHandler`
  (`X-Internal-Key` from secret `routego/internal-key`) → `UpstreamFailureHandler` (10 s per
  attempt, search retried once, resend never retried — decision 2026-10-09). No
  `IdentityTokenHandler`, so Notification's `UNAUTHORIZED` never ends the admin's session (rule 7).
- Address: `?email=&pageNumber=` holds the list; `&resend={sendLogEntryId}` opens the confirmation.
  No `email` value = the search form only; `email=` (empty) = "Search" with nothing typed.
- Confirmation: a box above the list reached by a link (works without JavaScript), shown only for
  an entry on the loaded page that may be resent. "Cancel" is a link back to the same list.
- After a resend: success → PRG back to the list with a flash; every refusal → the page re-rendered
  with the message and the list reloaded (Notification records failed resends as entries too).
- Page navigation: page numbers only (as the Users page).

## Form fields

| Field | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| email (search) | email | yes | "Email address is required." (`validation.required`); "Enter a valid email address." (`validation.format`) when malformed, over 254 characters, or refused by Notification |
| Input.SendLogEntryId | hidden | yes | — (from the confirmed entry) |

## Screen states

1. **Search form (GET, no `email`)** — title "Email log"; "Enter an email address to see every email
   RouteGo tried to send to it."; Email address; "Search".
2. **Results** — "Emails sent to {email}"; "Showing {from}–{to} of {total} emails"; columns Tried at,
   Email type, Subject, Status, Failure reason, Failure detail, Receipt number, Sent at (IST); under
   the subject "Resend of an earlier email" when it is one, "Event ID: …", "Correlation ID: …".
3. **Empty** — "No emails were sent to {email}."
4. **Resend offered** — every entry except failure reason `missingRecipient` / `invalidEventData`.
5. **Confirm resend** — "Send this email again to {to_address}? Subject: {subject}" + "Resend email"
   / "Cancel".
6. **Resending** — the button is disabled while sent (site-wide `site.js`).
7. **Success** (flash) — "The email was sent again to {to_address}." ({to_address} from
   Notification's answer); list reloaded.
8. **Refusals** (alert, list reloaded) — `RESEND_LINK_EXPIRED`: "This email cannot be resent because
   its verification link expired on {link_expired_at}. Ask the person to use 'Send the verification
   email again' on the website." (IST); `RESEND_NOT_POSSIBLE`: "This email cannot be resent:
   {failure_reason}."; `EMAIL_REFUSED`: "The email service refused this email: {provider_reason} The
   failed try is now in the list."; `UPSTREAM_UNAVAILABLE` **with** `sendLogEntryId`: "The email
   service is not available, so the email was not sent. The failed try is now in the list. Try again
   later."; `NOT_FOUND`: "We couldn't find that email. It may have been removed."
   (`problem.not_found`, {entity} "email").
9. **Key problem** — `UNAUTHORIZED` (search or resend): "The email log is not set up correctly: the
   portal cannot connect to the Notification service. Tell the platform engineer." The admin stays
   signed in.
10. **Notification unavailable / server error** — `msg:problem.upstream` (the typed address is
    kept) / `msg:problem.internal_error`.

## Acceptance criteria

- [x] AC-1: search an address (+ every column, internal key sent) — `Get_SearchAddress_SendsItWithFirstPageAndShowsEveryColumn`, `Get_SentResend_ShowsMarkerReceiptAndSentAt`
- [x] AC-2: no emails for the address — `Get_NoEmailsForAddress_ShowsEmptyMessage`
- [x] AC-3: resend a failed email — `Post_ConfirmResend_SendsAgainAndReloadsList`
- [x] AC-4: resend hidden when there is nothing to send — `Get_EntriesWithNothingToSend_OfferNoResend`
- [x] AC-5: verification link expired — `Post_ResendRefused_TellsWhyAndReloadsList` (RESEND_LINK_EXPIRED)
- [x] AC-6: email service down during resend — `Post_ResendRefused_TellsWhyAndReloadsList` (UPSTREAM_UNAVAILABLE)
- [x] AC-7: email service refuses the address — `Post_ResendRefused_TellsWhyAndReloadsList` (EMAIL_REFUSED)
- [x] AC-8: wrong internal key does not sign the admin out — `Get_WrongInternalKey_ShowsKeyProblemAndKeepsAdminSignedIn` (+ resend: UNAUTHORIZED row)
- [x] AC-9: malformed or empty address checked before sending — `Get_MalformedOrEmptyAddress_ShowsFieldMessageWithoutCallingNotification`
- [x] AC-10: every resend is logged — `Post_Resend_LogsTheAdminAndTheEntry`
- [x] Search form state — `Get_FirstVisit_ShowsSearchFormWithoutCallingNotification`
- [x] Nothing to resend / entry not found — `Post_ResendRefused_TellsWhyAndReloadsList` (RESEND_NOT_POSSIBLE, NOT_FOUND)
- [x] Cancel — `Get_Cancel_ReturnsToListWithoutResending`
- [x] Paging (shared rule 4) — `Get_SecondPage_SendsPageAndShowsRange`
- [x] Notification unavailable (search, resend not retried) — `Get_NotificationUnreachable_ShowsUpstreamMessageKeepsAddress`, `Post_NotificationUnreachable_ShowsUpstreamMessageWithoutRetrying`
- [x] Address refused by Notification — `Get_NotificationRefusesAddress_ShowsFormatMessageOnField`
- [x] CAP-UI-HOME-002 wrong role / visitor — `Get_Customer_ShowsNoPermissionWithoutCallingNotification`, `Get_Visitor_IsSentToSignInWithReturnAddress`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchSendLog` | `routego-notification:CAP-NTF-004@0.1.0` | `GET /internal/sendLogEntries?toAddress&page&pageSize=20` (`X-Internal-Key`) → 200 `{items, totalCount, page, pageSize}` | The list | 400 `toAddress`; `UNAUTHORIZED` (key problem) |
| `resendEmail` | `routego-notification:CAP-NTF-005@0.1.0` | `POST /internal/sendLogEntries/{sendLogEntryId}/resend` (`X-Internal-Key`) → 200 new entry | Resend | `RESEND_LINK_EXPIRED` (`linkExpiredAt`), `RESEND_NOT_POSSIBLE` (`failureReason`), `EMAIL_REFUSED` (`providerReason`), `UPSTREAM_UNAVAILABLE` (`sendLogEntryId`), `NOT_FOUND`, `UNAUTHORIZED` |

Wire shapes taken from the built Notification API (`Features/Notification/SearchSendLog`,
`ResendEmail`, `Infrastructure/InternalKeyAuthenticationHandler`); not live-tested.
