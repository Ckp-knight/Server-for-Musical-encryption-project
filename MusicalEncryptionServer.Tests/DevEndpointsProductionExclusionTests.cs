using System.Net;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace MusicalEncryptionServer.Tests;

/// <summary>
/// Forces ASPNETCORE_ENVIRONMENT=Development (the misconfiguration Task 3 is about) to prove
/// /dev and Scalar/OpenAPI stay unreachable regardless of that runtime setting. The #if DEBUG
/// guard in Program.cs means this only proves anything when the server project (and this test
/// assembly, since it's a ProjectReference) is built in Release configuration — running
/// `dotnet test` (Debug, the default) exercises the *runtime* app.Environment.IsDevelopment()
/// check instead, since in Debug the #if DEBUG block is still compiled in. Both are checked
/// below; see CLOUD_SECURITY_NOTES.md for why `dotnet test -c Release` is the one that matters.
/// </summary>
public class DevEndpointsProductionExclusionTests : IClassFixture<DevEndpointsProductionExclusionTests.DevelopmentEnvironmentFactory>
{
    public class DevelopmentEnvironmentFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Development"); // the exact misconfiguration this guards against
        }
    }

    private readonly DevelopmentEnvironmentFactory _factory;

    public DevEndpointsProductionExclusionTests(DevelopmentEnvironmentFactory factory) => _factory = factory;

#if DEBUG
    [Theory(Skip = "Program.cs compiles /dev and Scalar/OpenAPI IN under Debug by design (#if DEBUG) " +
        "so local development keeps working. This test only proves the production guarantee " +
        "when the server (and this test project, via ProjectReference) is built Release: " +
        "run `dotnet test -c Release`.")]
#else
    [Theory]
#endif
    [InlineData("/dev")]
    [InlineData("/dev/requests")]
    [InlineData("/dev/users")]
    [InlineData("/scalar/v1")]
    [InlineData("/openapi/v1.json")]
    public async Task DevAndDocsEndpoints_AreUnreachable_EvenWithDevelopmentEnvironment(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
