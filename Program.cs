using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MusicalEncryptionServer.Models;
using MusicalEncryptionServer.Repositories;
using MusicalEncryptionServer.Services;
using MusicalEncryptionServer.Sockets;

var builder = Host.CreateApplicationBuilder(args);

// --- Database layer ---------------------------------------------------------
builder.Services.AddSingleton<IEncryptionRepository, FirestoreEncryptionRepository>();

// --- Encryption algorithm -------------------------------------------------------
// TODO: swap the placeholder for the real musical-encryption algorithm.
builder.Services.AddSingleton<IMusicalCipher, PlaceholderMusicalCipher>();

// --- Users --------------------------------------------------------------------
builder.Services.AddSingleton<IUserRepository, FirestoreUserRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<UserService>();

// --- Socket server ---------------------------------------------------------
builder.Services.AddSingleton<RequestRouter>();
builder.Services.AddHostedService<TcpSocketServer>();

var host = builder.Build();

if (builder.Environment.IsDevelopment())
{
    // Dev-only convenience account, matching the WPF client's logo quick-fill
    // (admin / Admin1234). Seeded once here — the client never registers it —
    // so clicking the logo always logs in instead of creating a fresh user
    // every launch. See SeedDevAccountAsync below for why that matters.
    await SeedDevAccountAsync(host.Services);
}

await host.RunAsync();

// Creates the "admin" dev account only if it doesn't exist yet — never a second time.
// This is a "check, then create" shape so the seed is safe to run on every
// restart against a real (Firestore) database, not just an in-memory one.
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
