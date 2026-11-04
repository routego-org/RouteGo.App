# CAP-UI-SRCH-001 / CAP-UI-SRCH-002 — Bus search form and results

- Capabilities: CAP-UI-SRCH-001 (the form, also on Home `/`), CAP-UI-SRCH-002 (the results)
- Canonical spec: `spec-RouteGo.App` (routego-app 0.3.0) → `capabilities/search/features/bus-search.md` (feature spec 0.1.0)
- Area: Search
- Slice: `Features/Search/BusSearch/` — `BusSearch.cshtml.cs` (wire records, handler, page model),
  `BusSearch.Listing.cs` (how a result and the empty states are shown), tests in `BusSearch.Tests.cs`
  (results) and `BusSearch.Form.Tests.cs` (form checks, no-script city choice, suggestions)
- Shared with Home: `Common/UI/SearchForm.cs` (words, choices, addresses, Search's query),
  `Common/UI/SearchFormChecks.cs` (checks, picks, Search's field errors), `Common/UI/SearchFormHandler.cs`
  (Search calls: suggestions, "Search"), `Common/UI/SearchFormModels.cs`, `Features/Shared/_SearchForm.cshtml`,
  `Features/Shared/_SearchCityField.cshtml`, `wwwroot/js/bus-search.js` (suggestions as you type).

**Text provenance:**

| Words | Source |
|---|---|
| "Find a bus", "Search" | UI state Form, rule 3 |
| "From city", "To city", "Travel date", "Bus company", "Rating", "AC", "Departs", "Arrives", "Journey time", "Via" | `msg:label.trip_listing.*`, `msg:label.listing_stop.city_name` |
| "Bus type", "Departure time", "Price from", "Price up to", "Sort by" and every filter/sort choice | rules 7–8 |
| "{field} is required." / "Enter a valid {field}." | `msg:validation.required`, `msg:validation.format` |
| "Choose a city from the list.", "Choose a To city different from the From city.", "Choose today or a later date.", "Price from must not be more than Price up to." | UI state Form check failed |
| "No matching cities.", "We can't suggest cities right now. Please try again in a moment." | UI states No matching cities / Suggestions unavailable |
| "Buses from {from} to {to} on {date}", "Showing {from}–{to} of {total} buses." | UI state Results, rule 11 |
| "Rating {r} out of 5", "from ₹{fare}", "{n} seats left" / "1 seat left", "Via {city} {time}", "3 h 0 min", "1:00 AM IST, 26 Oct", "None", "See seats" | rule 10 |
| Seater / Sleeper / Semi-sleeper, Yes / No, amenity words | `msg:bus_type.*`, `msg:flag.yes_no`, `msg:amenity.*` |
| "Sold out" | `msg:state.sold_out` |
| "No buses from … Try another date." / "No buses for this route on … Try another date." | rule 12 |
| "No buses match the current filters." + "Clear filters" | `msg:empty.filtered`, rule 12 |
| "There are no buses on this page." + "Go to the first page" | rule 11 |
| upstream / internal-error messages | `msg:problem.upstream`, `msg:problem.internal_error` |

## Contract
- URL: `/bus-search` (absolute `@page`, fixed by the product — never under `/search`) — page name `/Search/BusSearch/BusSearch`.
- Auth: **everyone** (`PageAccessLevel.Everyone`). Search is called **without** the person's token: the Search
  client (`HttpClients.Search`, `ConfigKeys.SearchUrl`, SSM `search-url`, local `http://localhost:6221/`) has no
  token handler at all.
- Address (rule 5): `fromCityId`, `fromCityName`, `toCityId`, `toCityName`, `date`, and when chosen `busTypes`
  (comma-separated), `isAc`, `minFare`, `maxFare`, `departure` (earlyMorning/morning/afternoon/night → Search's
  departAfter/departBefore), `minRating`, `sort`, `page` (written only past page 1; the key is `page` as the spec
  says — read from the query by hand, because Razor reserves `page` as a route value).
- GET without cities: form only, no call. GET with cities: the address is checked first (city identifiers, same
  city, date today or later, prices) — a failure shows at the field with no call; otherwise `searchTrips` with
  the address's values as written (a hand-made `sort=cheapest` reaches Search and comes back as its title).
  An unknown `departure` value counts as "Any time".
- `POST /bus-search` (the form posts to the page's own address): checks, then 302 to the results address
  (always page 1); a failure draws the form again at the same address, no `searchTrips`.
  verb-justified: `OnPostAsync` sends nothing to Search's trip search — it is a POST so a failed check keeps the
  address unchanged (AC-7); the shareable address is the GET it redirects to.
- `GET /bus-search?handler=Cities&prefix=&cityRole=&fromCityId=` — JSON `{ items: [{cityId, name}], note }` for
  the page script (on Home too); words made server-side. Blank prefix → no call; over 50 letters → "No matching
  cities." without a call; any Search failure → the unavailable note.

## Decisions (agreed 2026-10-21)
- **Without the page script** (user choice): a city typed but not picked comes back from "Search" with
  "Choose a city from the list." and Search's matching cities as radio choices (`Search.FromPick` /
  `Search.ToPick`, value `{id}|{name}`); picking one and pressing "Search" again works. A pick counts only while
  the box still shows the picked name (hidden `FromCityId`/`FromCityName`).
- "Changing From clears To" is done by the page script; without it there is no way to "change" a picked From
  except by typing, which makes it unpicked anyway.
- Travel date is a browser date box (`type=date`, value `yyyy-MM-dd`), so it shows the browser's own date format
  rather than "25 Oct 2026"; the page title and messages use "25 Oct 2026".
- Filters and sort sit in the same form under the city bar; one "Search" applies all (rule 9 proposal).

## Screen states

1. **Form** — `/bus-search` without cities: heading "Find a bus", form with today's date and the filters.
2. **Results** — heading "Buses from … on …", form with the search, "Showing …", one card per result, page numbers.
3. **Sold-out result** — "Sold out" in place of the seats left, price and links still shown.
4. **No buses / Filtered empty / Past the last page** — the message with its link where there is one.
5. **Form check failed / Past date in the address** — messages at the fields, no call, no results.
6. **Refused by Search** — field messages for From/To/date/prices; anything else the problem's title at the top.
7. **Service unavailable / server error** — the message in place of the results; the form keeps the search.

## Acceptance criteria

- [x] AC-1 (home form, everyone, no call) — `Home.Search.Tests.cs`: `Get_Visitor_ShowsSearchFormWithTodayWithoutCallingSearch`, `Get_SignedIn_ShowsSearchFormWithWelcome`
- [x] AC-2 / AC-3 (suggestions, no token; To follows From) — `GetCities_FromPrefix_AsksSearchWithoutTokenAndAnswersNames`, `GetCities_ToPrefixAfterFrom_SendsFromCity`, `Post_ToTypedNotPicked_AsksSuggestionsFollowingFrom` (Home)
- [ ] AC-4 (changing From clears To) — page script only (`bus-search.js`), not covered by the .NET tests
- [x] AC-5 / AC-6 — `GetCities_NoMatch_AnswersNoMatchingCities`, `GetCities_SearchUnreachable_AnswersSuggestionsUnavailable`, `Post_TextTypedOverPickedCity_ShowsChooseFromList` (Home)
- [x] AC-7 — Home: `Post_FromEmpty_ShowsRequiredAndStaysHome`, `Post_TextTypedOverPickedCity_ShowsChooseFromList`, `Post_SameCityTwice_ShowsDifferentCityMessage`, `Post_YesterdayAsTravelDate_ShowsTodayOrLater`
- [x] AC-8 — `Post_PickedCitiesAndDate_GoesToResultsAddress` (Home); no-script pick `Post_CityChosenFromList_GoesToResults`, `Post_CityTypedNotPicked_OffersMatchingCitiesToChoose`
- [x] AC-9 — `Get_ResultsAddress_ShowsEveryTripWithoutToken`, `Get_ResultsAddress_ShowsTripDetailsAndLinks`, `Get_ResultsAddress_ShowsSameDayTripWithoutStopsOrAmenities`
- [x] AC-10 — `Get_SoldOutTrip_ShowsSoldOutWithPriceAndLinks`
- [x] AC-11 / AC-17 — `Post_FiltersAndSortOnPageTwo_GoesToAddressWithEveryChoiceOnPageOne`, `Get_FilteredAddress_ShowsChoicesInFormAfterReload`
- [x] AC-12 — `Post_PriceFromAbovePriceUpTo_ShowsMessageWithoutSearching`, `Post_PriceNotWholeRupees_ShowsFormatMessage`, `Get_PriceUpToAndRating_SendsThemToSearch`
- [x] AC-13 — `Get_NoBusesWithoutFilter_ShowsTryAnotherDateAndKeepsForm`
- [x] AC-14 — `Get_FiltersHideEveryBus_ShowsClearFiltersKeepingSort`
- [x] AC-15 — `Get_PageTwo_ShowsShowingTextAndPageNavigation`
- [x] AC-16 — `Get_PagePastTheEnd_ShowsGoToFirstPage`
- [x] AC-18 — `Get_PastDateInAddress_ShowsDateMessageWithoutSearching`
- [x] AC-19 — `Get_SearchRefusesSort_ShowsTitleAtTopAndKeepsForm`
- [x] AC-20 — `Get_AddressWithoutNames_UsesNamesOfFirstResult`, `Get_AddressWithoutNamesAndNoTrips_ShowsRouteMessageAndEmptyCities`
- [x] AC-21 — `Get_NoCitiesInAddress_ShowsFormWithTodayWithoutSearching`
- [x] AC-22 — `Get_SearchUnreachable_ShowsUpstreamMessageAndKeepsForm`
- [x] AC-23 + home-and-navigation AC-14 — `Get_SignedIn_ShowsSameResultsWithoutToken` (Customer, Operator, Admin) and the access-table row in `Foundation/SessionRulesTests.cs`

## Backend capabilities consumed

| Backend capability | Source | Route / verb | Used for | Problem codes with specific messages |
|---|---|---|---|---|
| `searchTrips` | `routego-search:CAP-SRCH-001@0.1.0` | `GET /search/trips` (no token) → 200 page of TripListing | Results | `VALIDATION_FAILED` (fields shown → field messages; others → title) |
| `searchCities` | `routego-search:CAP-SRCH-002@0.1.0` | `GET /search/cities` (no token) → 200 `{items}` | Suggestions, no-script choices | any problem → suggestions unavailable |

Wire shapes from RouteGo.Search.Api `Features/TripSearch/SearchTrips` and `SearchCities` (built): camelCase string
enums (`busType`, `amenities`), times UTC, `serviceDate` yyyy-MM-dd. Tested against `FakeBackend` only; not live-tested.
