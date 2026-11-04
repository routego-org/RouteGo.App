# CAP-UI-ADM-003 — Operator applications page: review, approve and reject

- Capability: CAP-UI-ADM-003
- Canonical spec: `spec-RouteGo.App` (routego-app 0.1.0) → `capabilities/administration/features/operator-application-review.md`
- Area: Admin
- Slice: `Features/Admin/Operators/` — `Operators.cshtml.cs` (query, input, validators, page model)
  and `Operators.Handler.cs` (wire records, Identity calls); tests in `Operators.Tests.cs` (list,
  access) and `Operators.Actions.Tests.cs` (approve, reject).

**Text provenance:** every string is projected from the canonical spec's `## UI states`, or from
`design/message-catalog.md` where the spec says `msg:<key>`. Item labels are the spec's own words
(Company name, GSTIN, Contact email, Email, Full name, Submitted at, Status, Reviewed at, Rejection
reason); `label.operator_application.reason` → "Reason". Status words are `state.*`, kept in this
slice (not `Common/`, which is at the structure checker's budget), as are `validation.reason_length`,
`empty.filtered` ({items} "applications") and `problem.not_found` ({entity} "operator").

## Contract
- URL: `/admin/operators` — page name `/Admin/Operators/Operators`
- Auth: admins only (page access table: `/Admin/` prefix). A customer or operator sees the
  no-permission message (403) with no Identity call; a visitor is sent to sign in.
- Address: `?status=&pageNumber=` holds the list; `&approve={operatorId}` or `&reject={operatorId}`
  opens the confirmation. No `status` in the address means the review queue: Identity is sent no
  status (its default is `pendingApproval`) and the filter shows "Waiting for approval". Identity
  is always sent `page` and `pageSize=20`.
- Confirmation: a box above the list reached by a link, so it works without JavaScript; shown only
  when the application is on the loaded page and still waiting. "Cancel" is a link back to the
  same list (nothing is sent).
- Redirect after a decision (success or "decided meanwhile"): back to the list with the same filter
  and page (PRG); the message is a flash. The company name in the success message is Identity's.
- Page navigation: page numbers only, as on the users page.

## Form fields

| Field | Type | Required | Validation message (+ `msg:` key) |
|---|---|---|---|
| status (filter) | select | no — default "Waiting for approval" | `state.pending_approval`, `state.pending_verification`, `state.approved`, `state.rejected`; "Enter a valid status." (`validation.format`) if refused |
| Input.Reason (reject only) | textarea | yes | "Reason must be between 10 and 500 characters." (`validation.reason_length`), counted like Identity (an emoji is one character) |

## Screen states

1. **List (GET)** — title "Operator applications"; filter Status + "Search"; each application shows
   Company name, GSTIN, Contact email, Email, Full name, Submitted at (IST) and Status; decided ones
   also Reviewed at, rejected ones Rejection reason; each earlier rejection "Earlier rejection:
   {reason}" with "Submitted on {submitted_at}" and "Rejected on {rejected_at}"; waiting ones offer
   "Approve" and "Reject"; "Showing {from}–{to} of {total} applications"; page numbers when more than
   one page.
2. **Empty** — waiting view: "No applications are waiting for review."; another status: "No
   applications match the current filters." (`msg:empty.filtered`).
3. **Confirm approve** — "Approve {company_name}? They will be able to sign in and sell bus
   tickets. We will email them." + "Approve" / "Cancel".
4. **Confirm reject** — "Reject {company_name}? We will email them the reason you write here. They
   can re-apply after 7 days." + Reason + "Reject application" / "Cancel".
5. **Working** — the confirm button is disabled while sent (site-wide `site.js`).
6. **Success** — "{company_name} is approved. We will email them, and they can sign in now." /
   "{company_name} is rejected. We will email them the reason. They can re-apply after 7 days.";
   list reloaded.
7. **Validation error** — the reason message (page check or Identity's 400); the confirmation stays
   open with the typed reason.
8. **Decided meanwhile** (flash, list reloaded) — `APPLICATION_NOT_PENDING`: "This application is
   not waiting for review any more. It is now {state}. The list has been updated."; `NOT_FOUND`:
   "We couldn't find that operator. It may have been removed." (`msg:problem.not_found`).
9. **Not allowed / unavailable / server error** — `FORBIDDEN` (`msg:problem.forbidden`),
   `msg:problem.upstream`, `msg:problem.internal_error`: on a decision the confirmation and reason
   are kept; on the list the filter is kept and no list is shown.

## Acceptance criteria

- [x] AC-1: waiting applications first — `Get_Admin_ListsWaitingApplicationsWithTheirDetails`
- [x] AC-2: earlier rejections shown — `Get_EarlierRejection_ShowsReasonWithBothDates`
- [x] AC-3: approve — `Post_Approve_ApprovesAndReloadsList`
- [x] AC-4: reject with a reason — `Post_RejectWithReason_RejectsAndReloadsList`
- [x] AC-5: reason too short — `Post_ReasonTooShort_ShowsMessageWithoutCallingIdentity`
- [x] AC-6: another admin decided first (+ not found) — `Post_DecidedMeanwhile_TellsWhyAndReloadsList`
- [x] AC-7: other statuses — `Get_RejectedFilter_ShowsReviewAndReasonWithoutDecisions`
- [x] AC-8: nothing waiting (+ other filter empty) — `Get_NothingMatches_ShowsEmptyMessage`
- [x] Paging — `Get_SecondPage_SendsSameFilterAndShowsRange`
- [x] Cancel does nothing — `Get_Cancel_ReturnsToListWithoutDeciding`
- [x] Service unavailable (list) — `Get_IdentityUnreachable_ShowsUpstreamMessageKeepsFilter`
- [x] Service unavailable (decision) — `Post_IdentityUnreachable_KeepsConfirmationAndReason`
- [x] Not for other roles — `Get_Customer_ShowsNoPermissionWithoutCallingIdentity`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `listOperatorApplications` | `routego-identity:CAP-OPR-003@0.1.0` | `GET /operators/admin/applications?status&page&pageSize=20` (admin token) → 200 `{items, totalCount, page, pageSize}` | The list | 400 filter errors; `FORBIDDEN` |
| `approveOperator` | `routego-identity:CAP-OPR-004@0.1.0` | `POST /operators/admin/applications/{operatorId}/approve` (no body) → 200 application | Approve | `APPLICATION_NOT_PENDING` (`currentStatus`), `NOT_FOUND` |
| `rejectOperator` | `routego-identity:CAP-OPR-005@0.1.0` | `POST /operators/admin/applications/{operatorId}/reject` `{reason}` → 200 application | Reject | `APPLICATION_NOT_PENDING` (`currentStatus`), `NOT_FOUND`; 400 reason |

Wire shapes taken from the built Identity API (`Features/OperatorOnboarding/ListOperatorApplications`,
`ApproveOperator`, `RejectOperator`, `Common/OperatorApplications`).
