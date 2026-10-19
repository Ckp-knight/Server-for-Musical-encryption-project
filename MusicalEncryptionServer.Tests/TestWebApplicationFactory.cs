using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicalEncryptionServer.Repositories;

namespace MusicalEncryptionServer.Tests;

/// <summary>
/// Hosts the real app in-process (TestServer — no sockets, no real network) with the
/// Firestore repositories swapped for the project's own in-memory ones, so tests never
/// need Google Cloud credentials or network access. The signing key below is a fixed,
/// test-only value used exclusively inside this in-memory test host — it is never used
/// by any real environment and is not the production secret, which is supplied via
/// `dotnet user-secrets` (dev) or the Jwt__SigningKey env var (prod) and is never
/// checked into source control.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "test-only-signing-key-not-used-anywhere-else-32bytes+";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = TestSigningKey,
                // The real default (10/60s) is far too low for a test run that logs
                // in/registers many users back-to-back from the same loopback "IP" —
                // raised here, for tests only, so the rate limiter itself isn't under test.
                ["AuthRateLimit:PermitLimit"] = "100000",
                ["AuthRateLimit:WindowSeconds"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Avoid ever constructing FirestoreUserRepository/FirestoreEncryptionRepository
            // (which would try to resolve Application Default Credentials) in tests.
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.RemoveAll<IEncryptionRepository>();
            services.AddSingleton<IEncryptionRepository, InMemoryEncryptionRepository>();
        });

        builder.UseEnvironment("Testing");
    }
}
