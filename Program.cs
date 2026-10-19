using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MusicalEncryptionServer.Diagnostics;
using MusicalEncryptionServer.Middleware;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Repositories;
using MusicalEncryptionServer.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Cloud Run injects PORT and expects the container to listen on 0.0.0.0:$PORT.
// Locally PORT isn't set, so this is a no-op and launchSettings.json / ASPNETCORE_URLS
// (http://localhost:5266) keep controlling the address as before.
var cloudRunPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(cloudRunPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{cloudRunPort}");
}

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    // This becomes the "how do I use this page" text at the very top of the
    // Scalar UI (http://localhost:5266/scalar/v1), above the endpoint list.
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Musical Encryption Server";
        document.Info.Version = "v1";
        document.Info.Description = """
            **This page is a test console for the server — not the app itself.**
            Use it to try each endpoint by hand and see exactly what it sends/returns.
            The real program is the WPF client (Musical-Encryption-Project).

            ### How to read a request panel
            - **Parameters** — things like `X-User-Id` that go in the URL/headers. Hover the ⓘ
              next to a field's name for what it means.
            - **Body** — the JSON sent for POST requests. Each field has a grey description
              under it explaining what to put there.
            - **Responses** (right side) — every status code this endpoint can return, and why.

            ### Try it in this order
            1. `POST /api/users` — create an account (Username, Password).
            2. `POST /api/users/login` — log in with the same Username/Password.
            3. Copy that Username into the **X-User-Id** field for every call below —
               it stands in for real login until authentication is added.
            4. `POST /api/encryption` — encrypt some text; copy the returned **Id**.
            5. `POST /api/encryption/{id}/decrypt` — paste that Id and the same Key to get the text back.
            """;
        return Task.CompletedTask;
    });
});

// --- Database layer ---------------------------------------------------------
builder.Services.AddSingleton<IEncryptionRepository, FirestoreEncryptionRepository>();

// --- Encryption algorithm -------------------------------------------------------
// TODO: swap the placeholder for the real musical-encryption algorithm.
builder.Services.AddSingleton<IMusicalCipher, PlaceholderMusicalCipher>();

// --- Users --------------------------------------------------------------------
builder.Services.AddSingleton<IUserRepository, FirestoreUserRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

// --- Dev control panel ----------------------------------------------------------
// Keeps the last 200 requests in memory so /dev can show a live feed of them.
builder.Services.AddSingleton<IRequestLogStore, InMemoryRequestLogStore>();

// --- JWT authentication ---------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured via IOptions<JwtOptions> (resolved lazily, after the DI container is fully
// built) rather than a plain local variable read from builder.Configuration beforehand —
// that would capture configuration as it stood before builder.Build() runs, which is too
// early for sources added by some hosts (e.g. the test host's WebApplicationFactory).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
    {
        var jwt = jwtOptionsAccessor.Value;
        bearerOptions.MapInboundClaims = false; // keep short claim names ("sub") as issued, not ClaimTypes.*
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // Only accept the algorithm we sign with — blocks "alg: none" and algorithm-confusion attacks.
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        };
    });
builder.Services.AddAuthorization();

// --- Rate limiting ----------------------------------------------------------------
// Register/Login/ChangePassword are anonymous by necessity (see Controllers/UsersController.cs),
// so they're the one place still open to unlimited guessing/brute-force from a single caller.
// Partitioned per client IP so one abusive caller can't also lock out everyone else.
builder.Services.AddOptions<AuthRateLimitOptions>().Bind(builder.Configuration.GetSection("AuthRateLimit"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
    {
        // Resolved per-request (post-Build()) rather than captured at registration time,
        // so hosts that add configuration later (e.g. TestWebApplicationFactory) are honored.
        var limit = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

// Force JwtTokenService to construct now (its constructor validates Jwt:Issuer,
// Jwt:Audience and Jwt:SigningKey) so missing/invalid JWT configuration fails
// startup instead of the first /api/users/login call.
app.Services.GetRequiredService<IJwtTokenService>();

// #if DEBUG means a `dotnet build/publish -c Release` binary (what Dockerfile produces for
// Cloud Run) has this code — Scalar, OpenAPI, and the whole /dev control panel — compiled
// out entirely. A misconfigured ASPNETCORE_ENVIRONMENT=Development on a Release-built
// container can no longer resurrect it; there is no code there to run. The runtime
// app.Environment.IsDevelopment() check is kept too (defense in depth for Debug builds,
// e.g. an accidental `dotnet run` with the wrong environment variable locally).
#if DEBUG
if (app.Environment.IsDevelopment())
{
    // Raw OpenAPI document at /openapi/v1.json ...
    app.MapOpenApi();
    // ... and a clickable UI to try every endpoint at /scalar/v1
    app.MapScalarApiReference(options => options.WithTitle("Musical Encryption — API Console"));

    // Dev-only convenience account, matching the WPF client's logo quick-fill
    // (admin / Admin1234). Seeded once here — the client never registers it —
    // so clicking the logo always logs in instead of creating a fresh user
    // every launch. See SeedDevAccountAsync below for why that matters.
    await SeedDevAccountAsync(app.Services);

    // --- Dev control panel: open http://localhost:5266/dev directly in a browser tab ---
    // This is a page to look at, not an API to call from Scalar — .ExcludeFromDescription()
    // keeps it out of the OpenAPI document so it doesn't show up in Scalar's endpoint list
    // (Scalar only shows a raw response body anyway; it doesn't run the page's JavaScript).
    // Dev-only: none of this is mapped when the app isn't in Development.
    app.MapGet("/dev", () => Results.Content(DevDashboardHtml.Content, "text/html"))
        .ExcludeFromDescription();
    app.MapGet("/dev/requests", (IRequestLogStore store) => store.GetRecent())
        .ExcludeFromDescription();
    app.MapGet("/dev/users", async (IUserRepository users) =>
    {
        var all = await users.GetAllAsync();
        return all.Select(u => new UserSummary(u.Id, u.Username, u.CreatedAt, u.LastLoginAt, u.IsBanned));
    }).ExcludeFromDescription();
    // The trash button in the "Registered users" panel calls this.
    app.MapDelete("/dev/users/{id}", async (string id, IUserRepository users) =>
        await users.DeleteAsync(id) ? Results.NoContent() : Results.NotFound())
        .ExcludeFromDescription();
    // The per-row menu's "Change password" action: force-sets a password, no old-password check.
    app.MapPost("/dev/users/{id}/password", async (string id, DevSetPasswordRequest body, IUserRepository users, IPasswordHasher hasher) =>
    {
        var user = await users.GetByIdAsync(id);
        if (user is null) return Results.NotFound();

        var (hash, salt) = hasher.Hash(body.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await users.UpdateAsync(user);
        return Results.NoContent();
    }).ExcludeFromDescription();
    // The per-row menu's "Ban / Unban" action: toggles the flag Login checks.
    app.MapPost("/dev/users/{id}/ban", async (string id, IUserRepository users) =>
    {
        var user = await users.GetByIdAsync(id);
        if (user is null) return Results.NotFound();

        user.IsBanned = !user.IsBanned;
        await users.UpdateAsync(user);
        return Results.Ok(new { user.IsBanned });
    }).ExcludeFromDescription();
    // The per-row menu's "Reset" action: deletes every encryption record owned by this user.
    // NOTE (OwnerUserId migration): records created before JWT auth was added store
    // OwnerUserId = username; records created since store OwnerUserId = the immutable
    // User.Id (see Controllers/EncryptionController.cs CallerId). GetByOwnerAsync below is
    // called with the username, so this button only still finds/deletes the old-style
    // records. No data was migrated — see CLOUD_SECURITY_NOTES.md for why and what's left.
    app.MapDelete("/dev/users/{id}/records", async (string id, IUserRepository users, IEncryptionRepository records) =>
    {
        var user = await users.GetByIdAsync(id);
        if (user is null) return Results.NotFound();

        var mine = await records.GetByOwnerAsync(user.Username);
        foreach (var record in mine)
            await records.DeleteAsync(record.Id);
        return Results.Ok(new { deleted = mine.Count });
    }).ExcludeFromDescription();
}
#endif

// In Production, turn any unhandled exception into a generic, constant 500 JSON body
// instead of whatever ASP.NET Core's bare default happens to return — the framework
// already doesn't leak stack traces outside Development, but this makes "don't leak
// details" an explicit, testable behavior rather than an implicit default to trust.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
    }));
}

// Logs one line per request: method, path, user, status code, milliseconds.
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseRateLimiter();

// No HTTPS locally: we run plain HTTP on one port (see ASPNETCORE_URLS), which
// avoids the "trust the localhost certificate" Windows prompt. When you deploy,
// the hosting platform terminates HTTPS for you; re-add app.UseHttpsRedirection()
// then if you also expose an HTTPS port.
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

#if DEBUG
// Creates the "admin" dev account only if it doesn't exist yet — never a second time.
// This is the same "check, then create" shape you'll use once this repository is
// Firestore-backed: a plain unconditional AddAsync would either duplicate the
// account or throw a "document already exists" error against a real database on
// every restart. Doing the check here means the seed is safe against both.
static async Task SeedDevAccountAsync(IServiceProvider services)
{
    const string username = "admin";
    const string password = "Admin1234"; // must match the WPF client's LogoButton_Click

    var users = services.GetRequiredService<IUserRepository>();
    if (await users.GetByUsernameAsync(username) is not null)
        return; // already seeded (e.g. a previous run) — leave it alone

    var hasher = services.GetRequiredService<IPasswordHasher>();
    var (hash, salt) = hasher.Hash(password);
    await users.AddAsync(new User { Username = username, PasswordHash = hash, PasswordSalt = salt });
}
#endif

// Exposes the top-level-statements entry point to WebApplicationFactory<Program> in tests.
public partial class Program { }
