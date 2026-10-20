using ListingSearch.Api.Data;
using ListingSearch.Api.Endpoints;
using ListingSearch.Api.Search;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOptions<ScoringOptions>()
    .Bind(builder.Configuration.GetSection("Scoring"))
    .Validate(o => o.IsValid(), "Scoring options are invalid: weights must be non-negative (not both zero), tolerance and half-life positive, penalty >= 1.")
    .ValidateOnStart();

// Resolved eagerly so a bad time zone id fails at startup, not on the first request.
var marketTimeZone = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["Listings:TimeZone"] ?? "America/New_York");
builder.Services.AddSingleton<TimeProvider>(new MarketTimeProvider(marketTimeZone));

builder.Services.AddSingleton<IListingRepository>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();
    var path = Path.Combine(env.ContentRootPath, config["Listings:DataPath"] ?? "Data/sample_listings.json");
    return InMemoryListingRepository.LoadFromJsonFile(path);
});
builder.Services.AddSingleton<IListingScorer, BudgetRecencyScorer>();
builder.Services.AddSingleton<ListingSearchService>();

var app = builder.Build();

// Unhandled exceptions and bare status codes become ProblemDetails JSON, so the UI always gets a parseable error.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapListingEndpoints();

app.Run();
