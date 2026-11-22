// Entry point of the backend. This file uses C# "top-level statements": there is
// no Main method or class around the code; the compiler generates them.
//
// ASP.NET Core "minimal API" style: build the app, map URLs to small functions
// (lambdas), run. Bigger features will move into their own files as extension
// methods (e.g. app.MapAccounts()) so this file stays a short table of contents.

// CreateBuilder sets up the standard pieces for us:
// - configuration from appsettings.json, appsettings.{Environment}.json and
//   environment variables (e.g. ASPNETCORE_URLS, Logging__LogLevel__Default),
// - logging to the console,
// - the Kestrel web server.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// GET /health: answers if the service is up. Used by Docker (and later the
// hosting platform) to check the container, and by us to test reachability
// (e.g. from the phone on the same Wi-Fi).
//
// Results.Ok(...) serializes the anonymous object to JSON: {"status":"ok"}.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
