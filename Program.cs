using MusicalEncryptionServer.Diagnostics;
using MusicalEncryptionServer.Middleware;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Repositories;
using MusicalEncryptionServer.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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
// Build and test against the in-memory store now.
// When Firestore is ready, change ONLY this line to:
//   builder.Services.AddSingleton<IEncryptionRepository, FirestoreEncryptionRepository>();
builder.Services.AddSingleton<IEncryptionRepository, InMemoryEncryptionRepository>();

// --- Encryption algorithm -------------------------------------------------------
// TODO: swap the placeholder for the real musical-encryption algorithm.
builder.Services.AddSingleton<IMusicalCipher, PlaceholderMusicalCipher>();

// --- Users --------------------------------------------------------------------
// Same story: in-memory now, swap for a Firestore-backed IUserRepository later.
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

// --- Dev control panel ----------------------------------------------------------
// Keeps the last 200 requests in memory so /dev can show a live feed of them.
builder.Services.AddSingleton<IRequestLogStore, InMemoryRequestLogStore>();

var app = builder.Build();

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
        return all.Select(u => new UserSummary(u.Id, u.Username, u.CreatedAt));
    }).ExcludeFromDescription();
    // The trash button in the "Registered users" panel calls this.
    app.MapDelete("/dev/users/{id}", async (string id, IUserRepository users) =>
        await users.DeleteAsync(id) ? Results.NoContent() : Results.NotFound())
        .ExcludeFromDescription();
}

// Logs one line per request: method, path, user, status code, milliseconds.
app.UseMiddleware<RequestLoggingMiddleware>();

// No HTTPS locally: we run plain HTTP on one port (see ASPNETCORE_URLS), which
// avoids the "trust the localhost certificate" Windows prompt. When you deploy,
// the hosting platform terminates HTTPS for you; re-add app.UseHttpsRedirection()
// then if you also expose an HTTPS port.
app.UseAuthorization();
app.MapControllers();

app.Run();

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
