using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MusicalEncryptionServer.Tests;

/// <summary>Uses its own low-limit factory (separate from the suite-wide high limit in
/// TestWebApplicationFactory) to prove the "auth" rate limiter policy actually rejects
/// requests past its configured limit, rather than just existing in Program.cs unused.</summary>
public class AuthRateLimitTests : IClassFixture<AuthRateLimitTests.LowLimitFactory>
{
    public class LowLimitFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthRateLimit:PermitLimit"] = "3",
                ["AuthRateLimit:WindowSeconds"] = "60",
            }));
        }
    }

    private readonly LowLimitFactory _factory;

    public AuthRateLimitTests(LowLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_RejectsWithTooManyRequests_PastTheConfiguredLimit()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < 3; i++)
            await client.PostAsJsonAsync("/api/users", new { username = $"rl-user-{i}", password = "Password1" });

        var fourth = await client.PostAsJsonAsync("/api/users", new { username = "rl-user-4", password = "Password1" });
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
    }
}
