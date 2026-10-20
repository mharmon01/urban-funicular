# Listing Search

A full-stack search over property listings from several MLS feeds. It supports filtering, a relevance ranking that combines price fit and recency, and pagination, with deliberate handling of invalid input end to end.

| | |
|---|---|
| **Backend** | ASP.NET Core minimal API, .NET 10, C# |
| **Frontend** | React 19 + TypeScript, Vite |
| **Data** | In memory, loaded once from `sample_listings.json` |
| **Tests** | 101 backend (xUnit) + 19 UI (Vitest + Testing Library), all passing |
| **Verification** | Differential fuzzing against an independent oracle, mutation testing, concurrency test |

---

## Requirements coverage

| Requirement | How it's met |
|---|---|
| Filter by `minPrice`, `maxPrice`, `minBedrooms`, `city`, `keyword` | Inclusive bounds. City is case- and whitespace-insensitive. Keyword requires every term to appear in the description |
| Rank by `targetBudget` and recency | A transparent score from 0 to 100, explained in [Relevance scoring](#relevance-scoring). Each result carries its score breakdown |
| Pagination the user can see and move through | Prev/Next, page numbers (collapsed with "…" when there are many), page size, "Page X of Y · N results" |
| Real inputs and ranked results (address, price, bedrooms, score) | The form has an input for every parameter. Results also show city, baths, sqft, listed date, status and source feed |
| Loading, no-results and error states | Loading keeps the previous page on screen, dimmed. No-results is an explicit message. Errors appear under the field they belong to, or in a banner |
| Deliberate handling of invalid input | Every bad input returns a clear 400 with a message per field. See [Input validation](#input-validation) |
| Tests of core logic and edge cases | No matches, tied scores, invalid filters and pagination boundaries are all covered, plus fuzzing and mutation testing |
| README explaining scoring and trade-offs | This document |

---

## Quick start

**Prerequisites:** .NET SDK 10 and Node.js 22.12 or later.

Start the API (http://localhost:5151):

```bash
dotnet run --project src/ListingSearch.Api
```

Then, in a second terminal, start the UI (http://localhost:5173). Vite proxies `/api` to the API, so no CORS setup is needed.

```bash
cd web
npm install
npm run dev
```

Run the backend tests from the repo root:

```bash
dotnet test
```

Run the UI tests from `web/`:

```bash
npm test
```

> **Windows:** clone or extract the project to a short path such as `C:\src\ListingSearch`. Build output adds about 120 characters to the folder path. Without long-path support enabled, a deep folder can exceed Windows' 260-character limit, and MSBuild then fails with *"Could not copy … runtimeconfig.json"*.

---

## Approach

1. **Read the data before designing.** The sample set contains the same properties in both feeds (A1/B7, A2/B8, A3/B9, A5/B11), IDs that are only unique within a feed, a `pending` listing, and descriptions with negations ("No pets"). These shaped the identity key `(source, id)`, the assumptions below, and the extension plan.
2. **Make ambiguities explicit.** Where the spec is open, I chose the behaviour least likely to return *wrong data silently*, wrote it down, and kept it easy to change.
3. **Design for change.** A search is an explicit pipeline of small, pure steps behind narrow seams (`IListingScorer`, `IListingRepository`, `TimeProvider`):

   ```
   validate → filter → score → rank → paginate
   ```

   A new requirement becomes a new step, not a rewrite.
4. **Core logic first, test-driven.** I built the validator, filters, scorer, ranking and pagination as plain classes with unit tests before adding HTTP, then the UI on top.
5. **Verify beyond the happy path.** Integration tests, a differential fuzzer, mutation testing and a concurrency check (see [Testing and verification](#testing-and-verification)).

## Assumptions

These are the questions I'd confirm with a product owner. Each is a one-line change if the answer differs.

- **"A city with no matches"** covers two cases. A city that doesn't exist in the data returns **400** with the list of valid cities, because it is almost certainly a typo. A real city whose other filters exclude everything returns **200 with an empty page**, and the UI says so explicitly.
- **Status:** `active`, `pending` and `sold` are all returned and shown. The spec defines the field but asks for no status filter, and hiding listings silently would be wrong data.
- **Keyword:** case-insensitive substring match on `description`, with every term required. Negation isn't handled (see [Trade-offs](#trade-offs-and-known-limitations)).
- **Bounds are inclusive.** `minPrice=500000` includes a $500,000 listing.
- **Cross-feed duplicates** are shown once per feed, with the source visible. De-duplication is designed but deliberately not built yet (see [Roadmap](#roadmap)).
- **"Today"** for recency is the market's calendar date (America/New_York, configurable), not the server's clock or UTC.

---

## Relevance scoring

Each listing gets a score from 0 to 100. It is a weighted average of two components, each between 0 and 1:

```
budget  = max(0, 1 − d / 0.5)        d = |price − target| / target   (d × 1.5 when price > target)
recency = 0.5 ^ (ageDays / 30)       ageDays = today − listedDate     (future dates count as 0)

score   = 100 × (0.6 · budget + 0.4 · recency)    when targetBudget is given
score   = 100 × recency                            otherwise
```

**Worked example:** target $500,000, a $450,000 listing, listed 30 days ago.
budget = 1 − 0.10 / 0.5 = **0.80** · recency = 0.5¹ = **0.50** · score = 100 × (0.48 + 0.20) = **68.0**

**Why this shape:**

- **Relative, not absolute, distance.** $25k off matters on a $300k budget far more than on a $3M one.
- **Over budget is penalised more.** Buyers can usually go under budget but not over it, so being 10% over scores the same as being 15% under.
- **Linear with a tolerance.** The budget score is easy to explain and to check by hand. It reaches 0 at 50% under the target, or about 33% over it.
- **Exponential decay for recency.** A 30-day half-life means a month-old listing is worth half a new one, and the score never drops abruptly to 0.
- **Weighted 60/40 towards budget.** A stated budget is the stronger signal. With no budget, ranking is by recency alone; the scorer never invents a target.
- **Transparent and tunable.** The API returns the breakdown, and the UI shows it as a tooltip. Weights, tolerance, penalty and half-life live in `appsettings.json` and are validated at startup.

**Ties and determinism.** Ranking sorts by the score *as displayed* (rounded to 1 decimal), so two listings that both show 72.0 are genuinely tied. Ties break by **newer listing**, then **lower price**, then **source and id**. The order is total, so a query always returns the same order, and offset pagination can never repeat or skip a listing.

---

## Input validation

Every rule below has tests. All errors from a request are reported together, so the user can fix everything at once.

| Input | Response | Reasoning |
|---|---|---|
| `minPrice > maxPrice` | 400, with an error on both fields | Contradictory input. An empty result would hide the mistake |
| Negative price or bedrooms, `targetBudget ≤ 0` | 400 | Meaningless values. A zero budget would divide by zero |
| `pageSize ≤ 0` or `> 50` · `page < 1` | 400 | The cap stops one request pulling the whole dataset |
| `page` past the last page | 400, stating how many pages exist | A client bug, not "no results". Page 1 of an empty result is a valid 200 |
| Not a number (`abc`, `1,000`, `$500`) | 400 "must be a number" | Parameters are parsed explicitly for clear messages. Thousands separators mean different things in different cultures |
| Number out of range | 400 "is too large" | More accurate than "not a number" |
| Unknown parameter (`max_price`) | 400, listing the supported parameters | Ignoring a typo would silently return unfiltered results |
| Repeated parameter (`minPrice=1&minPrice=2`) | 400 "given 2 times; specify it once" | Otherwise the framework joins the values and drops empty ones |
| City not in the data | 400, listing the known cities | Almost certainly a typo |
| Real city, nothing else matches | 200, empty page | A genuine "no matches" |
| Blank value (`minPrice=`) | Treated as absent | An empty form field means "no filter" |

Unexpected faults return a ProblemDetails 500 through `UseExceptionHandler`, so the UI always receives JSON it can parse. A differential fuzzer (below) checked the absence of 500s against 20,000 random and malformed requests.

---

## API reference

`GET /api/listings/search`

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `minPrice`, `maxPrice` | number ≥ 0 | none | Inclusive |
| `minBedrooms` | integer ≥ 0 | none | Inclusive |
| `city` | string | none | Must exist in the data; case-insensitive |
| `keyword` | string ≤ 200 chars | none | All terms must appear in `description` |
| `targetBudget` | number > 0 | none | Enables the budget component of the score |
| `page` | integer ≥ 1 | 1 | |
| `pageSize` | integer 1–50 | 10 | |

Parameter names are case-insensitive.

**200 OK**

```json
{
  "items": [
    { "id": "A1", "source": "MLS_A", "address": "123 Main St, Apt 4B", "city": "Springfield",
      "price": 450000, "bedrooms": 2, "listedDate": "2026-08-29", "status": "active",
      "score": 94.0, "scoreBreakdown": { "budget": 1.0, "recency": 0.851, "total": 94.0 } }
  ],
  "page": 1, "pageSize": 10, "totalCount": 4, "totalPages": 1
}
```

**400 Bad Request:** RFC 7807 `ValidationProblemDetails`, with errors keyed by parameter.

```json
{
  "title": "One or more validation errors occurred.", "status": 400,
  "errors": {
    "minPrice": ["minPrice (600000) cannot be greater than maxPrice (500000)."],
    "city":     ["No listings exist in city 'Atlantis'. Known cities: Chantilly, Fairfax, Manassas, Reston, Springfield, Vienna."]
  }
}
```

`GET /api/listings/cities` returns the distinct cities, used for suggestions in the UI.

---

## Architecture

```
Browser (React)                     ASP.NET Core API
───────────────                     ──────────────────────────────────────────────────────────
SearchForm ─┐                       ListingEndpoints   HTTP ↔ Result<T>
Pagination ─┼─ SearchRequest ──GET─▶  SearchQueryValidator   raw strings → SearchCriteria | errors
ResultsTable┘   (one fetch per        ListingSearchService   filter → score → rank → paginate
                 change, aborted        ├ ListingFilters        predicates
                 when superseded)       ├ IListingScorer        BudgetRecencyScorer (options-driven)
                                        └ IListingRepository    InMemoryListingRepository (JSON, read-only)
                                      TimeProvider           MarketTimeProvider ("today" in market time)
```

```
src/ListingSearch.Api/     Domain/ · Data/ · Search/ · Endpoints/ · Program.cs
tests/ListingSearch.Tests/ validator · filters · scoring · ranking · pagination · guards · HTTP integration
web/src/                   App.tsx (state) · api.ts (typed client) · pageWindow.ts · components/
tools/                     fuzz.mjs · load.mjs · mutate.ps1
```

**Design notes:**

- **Validation in one place.** The validator is pure: the endpoint passes in what it needs from outside (the parameters as sent, and the known cities), so every input error appears in a single response. The only check left to the service is "page past the last page", which depends on how many results match.
- **Expected failures are values.** `Result<T>` carries field errors back to the endpoint, and exceptions are reserved for genuine faults.
- **Thread safety by immutability.** The listing data is loaded once and never mutated, so the singleton services are safe under concurrent requests. The concurrency test confirms it.
- **Frontend state.** The current `SearchRequest` is the single source of truth, and each change triggers one fetch. Loading is derived ("is the latest result for an older request?") rather than stored as separate state. An `AbortController` per request stops a slow, stale response from overwriting a newer one.

---

## Testing and verification

| Check | What it does | Result |
|---|---|---|
| **Unit and integration tests** | `dotnet test` (validator, filters, scorer, ranking, pagination, guards, HTTP via `WebApplicationFactory`) and `npm test` (rendering, query building, every UI state) | **101 + 19 passing.** Hand-written C#: 100% of lines, and every branch except two config-default fallbacks. UI: ~99% of lines |
| **Differential fuzzing**, `node tools/fuzz.mjs` | Random valid, edge-case and malformed queries against the running API. Every response is compared with an **independent oracle**, a separate re-implementation of the rules. Status, error fields, counts, ordering and scores must match exactly | **20,000 requests over 4 seeds: 0 mismatches, 0 server errors.** It found one real bug (repeated parameters), since fixed |
| **Mutation testing**, `tools/mutate.ps1` | Plants 37 realistic bugs one at a time, such as exclusive bounds, a reversed tie-break, an off-by-one page offset or a missing validation. The suite must fail for each | **37 / 37 caught.** The first run exposed two missing tests, since added |
| **Concurrency**, `node tools/load.mjs` | 20,000 requests with 200 in flight. Every response must be a 200 and byte-identical | **0 errors, 0 mismatches** (~3,700 req/s locally) |
| **Clean build** | Fresh extract → Release build with warnings as errors → tests → `npm ci` → lint → build | **0 warnings.** No known-vulnerable packages (`npm audit`, `dotnet list package --vulnerable`) |

The edge cases the brief calls out each have dedicated tests:
- **No matches:** an empty dataset, filters that exclude everything, and a real city with nothing matching.
- **Tied scores:** ties broken by date, by price, and by source and id, plus ties that exist only after rounding.
- **Invalid filters:** every row of the validation table.
- **Pagination boundaries:** first page, last partial page, an exact multiple of the page size, a page past the end, and all pages together covering every result exactly once.

---

## Trade-offs and known limitations

- **Cross-feed duplicates aren't merged yet.** This is intentional; see [Roadmap](#roadmap). Each duplicate shows its source feed, so the user isn't misled.
- **Keyword search has no negation awareness.** "pets" also matches "No pets". A proper fix uses structured attributes extracted at ingestion (e.g. `petsAllowed`) rather than a fragile regex.
- **Linear scan per request.** Each search costs O(n log n). That's ideal for an in-memory dataset of this size. At scale, filtering would move into the database or search engine (indexes on price, city and bedrooms; full-text search on descriptions), and only scoring would stay in code. The scoring maps directly onto search-engine decay functions.
- **Offset pagination.** It's simple and supports jumping to a page. The total sort order keeps it stable, but data changing between requests could still shift items. Cursor pagination on the sort key would fix that.
- **City matching is exact.** It ignores case and whitespace, but there's no fuzzy matching. A typo gets the list of valid cities rather than a guess.

## Roadmap

**Cross-feed de-duplication** (designed, ready to build):
- **Identity.** A normalised address (lowercased, street suffixes unified such as Street→St and Avenue→Ave, unit markers such as Apt and Unit stripped) plus city and state, confirmed by matching coordinates. Zip is excluded because the feeds disagree on it (22150 vs 22151).
- **Survivor.** The most recently listed record, or a preferred feed, keeping a `sources` list so the merge is visible.
- **Placement.** Duplicates are a property of the data, so de-duplication runs once at ingestion as an `IListingRepository` decorator, and the search pipeline stays unchanged. If it must be per query (e.g. a "show all feeds" toggle), it becomes the pipeline's first stage, before filtering, so the record kept doesn't depend on which filters matched.

**Natural next steps:**
- A status filter.
- Sort options.
- A radius filter using the existing coordinates.
- Shareable search URLs (query state in the address bar).
- Cursor pagination.

---

<details>
<summary><strong>Configuration and other options</strong></summary>

- **Scoring:** `appsettings.json` → `Scoring` (`BudgetWeight`, `RecencyWeight`, `BudgetTolerance`, `OverBudgetPenalty`, `RecencyHalfLifeDays`). Validated at startup.
- **Data and time zone:** `appsettings.json` → `Listings` (`DataPath`, `TimeZone`, default `America/New_York`).
- **Access from other devices on your network:** run the API with `--urls http://0.0.0.0:5151` and the UI with `npm run dev -- --host`, then open `http://<LAN-IP>:5173`. Only the UI port needs to be reachable.
- **Verification tools:** start the API, then from the repo root run `node tools/fuzz.mjs`, `node tools/load.mjs` or `powershell -File tools/mutate.ps1 -Kind dotnet|web`.

</details>
