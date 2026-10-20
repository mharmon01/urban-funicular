# Targeted mutation testing: each mutant plants one realistic bug in the source, runs the test suite, and
# restores the file. A mutant must make the suite fail ("killed"); a "SURVIVED" mutant is a missing test.
# Usage (from the repo root; stop anything that locks bin/ first):
#   powershell -File tools/mutate.ps1 -Kind dotnet
#   powershell -File tools/mutate.ps1 -Kind web
#   powershell -File tools/mutate.ps1 -Kind dotnet -Only 'rounding'   # run just the mutants whose name matches
param([string]$Root = (Split-Path $PSScriptRoot -Parent), [string]$Kind = 'dotnet', [string]$Only = '')

$api = 'src\ListingSearch.Api'
$dotnetMutants = @(
  @{ F = "$api\Search\Scoring.cs"; Find = 'if (price > target) distance *= _options.OverBudgetPenalty;'; Repl = ''; D = 'drop over-budget penalty' }
  @{ F = "$api\Search\Scoring.cs"; Find = 'Math.Max(0, 1 - distance / _options.BudgetTolerance)'; Repl = '1 - distance / _options.BudgetTolerance'; D = 'no clamp on budget score' }
  @{ F = "$api\Search\Scoring.cs"; Find = 'Math.Max(0, today.DayNumber - listedDate.DayNumber)'; Repl = 'today.DayNumber - listedDate.DayNumber'; D = 'future dates not clamped' }
  @{ F = "$api\Search\Scoring.cs"; Find = 'Math.Round(combined * 100, 1)'; Repl = 'Math.Round(combined * 100, 2)'; D = 'score rounding precision' }
  @{ F = "$api\Search\Scoring.cs"; Find = '_options.BudgetWeight * b + _options.RecencyWeight * recency'; Repl = '_options.RecencyWeight * b + _options.BudgetWeight * recency'; D = 'weights swapped' }
  @{ F = "$api\Search\Scoring.cs"; Find = 'Math.Pow(0.5, ageDays / _options.RecencyHalfLifeDays)'; Repl = 'Math.Pow(0.5, ageDays / (2 * _options.RecencyHalfLifeDays))'; D = 'wrong half-life' }
  @{ F = "$api\Search\ListingFilters.cs"; Find = 'listing.Price >= criteria.MinPrice'; Repl = 'listing.Price > criteria.MinPrice'; D = 'minPrice exclusive' }
  @{ F = "$api\Search\ListingFilters.cs"; Find = 'listing.Price <= criteria.MaxPrice'; Repl = 'listing.Price < criteria.MaxPrice'; D = 'maxPrice exclusive' }
  @{ F = "$api\Search\ListingFilters.cs"; Find = 'listing.Bedrooms >= criteria.MinBedrooms'; Repl = 'listing.Bedrooms > criteria.MinBedrooms'; D = 'minBedrooms exclusive' }
  @{ F = "$api\Search\ListingFilters.cs"; Find = '.All(term =>'; Repl = '.Any(term =>'; D = 'keyword OR instead of AND' }
  @{ F = "$api\Search\ListingFilters.cs"; Find = 'description.Contains(term, StringComparison.OrdinalIgnoreCase)'; Repl = 'description.Contains(term, StringComparison.Ordinal)'; D = 'keyword case-sensitive' }
  @{ F = "$api\Search\ListingFilters.cs"; Find = 'listingCity.Trim(), city.Trim()'; Repl = 'listingCity, city'; D = 'city not trimmed' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = '.ThenByDescending(s => s.Listing.ListedDate)'; Repl = '.ThenBy(s => s.Listing.ListedDate)'; D = 'tie-break: older first' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = '.ThenBy(s => s.Listing.Price)'; Repl = '.ThenByDescending(s => s.Listing.Price)'; D = 'tie-break: pricier first' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = '.ThenBy(s => s.Listing.Source, StringComparer.Ordinal)'; Repl = ''; D = 'tie-break: source dropped' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = 'criteria.Page > Math.Max(totalPages, 1)'; Repl = 'criteria.Page > totalPages'; D = 'page 1 of empty result rejected' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = 'Math.Ceiling(totalCount'; Repl = 'Math.Floor(totalCount'; D = 'totalPages floors' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = '.Skip((criteria.Page - 1) * criteria.PageSize)'; Repl = '.Skip(criteria.Page * criteria.PageSize)'; D = 'off-by-one page offset' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = '.OrderByDescending(spelling => spelling.Count())'; Repl = '.OrderBy(spelling => spelling.Count())'; D = 'least common city spelling' }
  @{ F = "$api\Search\ListingSearchService.cs"; Find = 'timeProvider.GetLocalNow().DateTime'; Repl = 'timeProvider.GetUtcNow().DateTime'; D = 'today in UTC' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'if (minPrice > maxPrice)'; Repl = 'if (minPrice >= maxPrice)'; D = 'minPrice == maxPrice rejected' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'pageSize is < 1 or > MaxPageSize'; Repl = 'pageSize is < 1 or > MaxPageSize + 1'; D = 'pageSize 51 allowed' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'if (targetBudget <= 0)'; Repl = 'if (targetBudget < 0)'; D = 'targetBudget 0 allowed' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'if (page < 1)'; Repl = 'if (page < 0)'; D = 'page 0 allowed' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'else if (values.Count > 1)'; Repl = 'else if (values.Count > 2)'; D = 'duplicate params allowed' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'if (!SupportedParameters.Contains(name, StringComparer.OrdinalIgnoreCase))'; Repl = 'if (false)'; D = 'unknown params allowed' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'keyword?.Length > MaxKeywordLength'; Repl = 'keyword?.Length > MaxKeywordLength + 1'; D = 'keyword limit off by one' }
  @{ F = "$api\Search\SearchQueryValidator.cs"; Find = 'error(field, DecimalPattern().IsMatch(value)'; Repl = 'error(field, false'; D = 'too-large reported as NaN' }
  @{ F = "$api\Endpoints\ListingEndpoints.cs"; Find = 'request.Query, service.GetCities()'; Repl = 'request.Query, null'; D = 'unknown-city check skipped' }
)
$webMutants = @(
  @{ F = 'web\src\pageWindow.ts'; Find = 'if (p - previous === 2)'; Repl = 'if (p - previous === 3)'; D = 'single-page gap shown as ...' }
  @{ F = 'web\src\pageWindow.ts'; Find = 'page - radius'; Repl = 'page - radius + 1'; D = 'window too narrow on left' }
  @{ F = 'web\src\App.tsx'; Find = 'setRequest({ ...filters, page: 1, pageSize: request.pageSize })'; Repl = 'setRequest({ ...filters, page: request.page, pageSize: request.pageSize })'; D = 'search keeps old page' }
  @{ F = 'web\src\api.ts'; Find = "if (text !== '') params.set(key, text)"; Repl = 'params.set(key, text)'; D = 'blank params sent' }
  @{ F = 'web\src\api.ts'; Find = 'if (response.status === 400 && problem?.errors)'; Repl = 'if (false)'; D = 'validation errors not mapped to fields' }
  @{ F = 'web\src\components\Pagination.tsx'; Find = 'disabled={disabled || page <= 1}'; Repl = 'disabled={disabled}'; D = 'prev enabled on page 1' }
  @{ F = 'web\src\components\Pagination.tsx'; Find = 'disabled={disabled || page >= totalPages}'; Repl = 'disabled={disabled}'; D = 'next enabled on last page' }
  @{ F = 'web\src\App.tsx'; Find = '{!loading && data?.totalCount === 0 && ('; Repl = '{false && ('; D = 'no-results message missing' }
)

$mutants = if ($Kind -eq 'web') { $webMutants } else { $dotnetMutants }
if ($Only) { $mutants = @($mutants | Where-Object { $_.D -match $Only }) }
$results = foreach ($m in $mutants) {
  $path = Join-Path $Root $m.F
  $original = [IO.File]::ReadAllText($path)
  $count = ([regex]::Matches($original, [regex]::Escape($m.Find))).Count
  if ($count -ne 1) { [pscustomobject]@{ Mutant = $m.D; Result = "NOT APPLIED ($count matches)" }; continue }
  try {
    [IO.File]::WriteAllText($path, $original.Replace($m.Find, $m.Repl))
    if ($Kind -eq 'web') { Push-Location (Join-Path $Root 'web'); npx vitest run 2>&1 | Out-Null; $code = $LASTEXITCODE; Pop-Location }
    else { dotnet test $Root --no-restore 2>&1 | Out-Null; $code = $LASTEXITCODE }
    [pscustomobject]@{ Mutant = $m.D; Result = $(if ($code -ne 0) { 'killed' } else { 'SURVIVED' }) }
  } finally { [IO.File]::WriteAllText($path, $original) }
}
$results | Format-Table -AutoSize | Out-String -Width 200
$killed = @($results | Where-Object Result -eq 'killed').Count
"Mutation score: $killed / $(@($results).Count)"
if ($killed -ne @($results).Count) { exit 1 }
