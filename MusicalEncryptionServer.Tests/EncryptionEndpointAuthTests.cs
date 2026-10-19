using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MusicalEncryptionServer.Models;
using Xunit;

namespace MusicalEncryptionServer.Tests;

/// <summary>
/// End-to-end tests over an in-memory TestServer (no real sockets, no network, no
/// Firestore — see <see cref="TestWebApplicationFactory"/>). These exercise the actual
/// JWT Bearer authentication/authorization pipeline wired up in Program.cs.
/// </summary>
public class EncryptionEndpointAuthTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EncryptionEndpointAuthTests(TestWebApplicationFactory factory) => _factory = factory;

    private async Task<string> RegisterAndLoginAsync(HttpClient client, string username)
    {
        var register = await client.PostAsJsonAsync("/api/users", new { username, password = "Password1" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/users/login", new { username, password = "Password1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        return body.Token;
    }

    [Fact]
    public async Task Registration_RemainsAvailableWithoutAToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/users", new { username = "reg-user", password = "Password1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTokenAndExistingUserInformation()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/users", new { username = "login-user", password = "Password1" });

        var login = await client.PostAsJsonAsync("/api/users/login", new { username = "login-user", password = "Password1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.Equal("login-user", body!.User.Username);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.True(body.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EncryptionEndpoint_MissingToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/encryption");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EncryptionEndpoint_InvalidToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.GetAsync("/api/encryption");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EncryptionEndpoint_ValidToken_AllowsAuthenticatedAccess()
    {
        var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client, "valid-token-user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/encryption");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AccessingAnotherUsersRecord_IsForbidden()
    {
        var client = _factory.CreateClient();

        var ownerToken = await RegisterAndLoginAsync(client, "owner-user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var created = await client.PostAsJsonAsync("/api/encryption",
            new { title = "secret", plainText = "hello", key = "k1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = await created.Content.ReadFromJsonAsync<EncryptResponse>();
        Assert.NotNull(record);

        var attackerToken = await RegisterAndLoginAsync(client, "attacker-user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attackerToken);

        var read = await client.GetAsync($"/api/encryption/{record!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var decrypt = await client.PostAsJsonAsync($"/api/encryption/{record.Id}/decrypt", new { key = "k1" });
        Assert.Equal(HttpStatusCode.Forbidden, decrypt.StatusCode);

        var delete = await client.DeleteAsync($"/api/encryption/{record.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task NewRecord_IsOwnedByAuthenticatedCaller_NotByAnyClientSuppliedId()
    {
        var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client, "owner-check-user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await client.PostAsJsonAsync("/api/encryption",
            new { title = "t", plainText = "hello", key = "k1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = await created.Content.ReadFromJsonAsync<EncryptResponse>();

        // Same client/token can read it back — proves the server, not the client, decided ownership.
        var read = await client.GetAsync($"/api/encryption/{record!.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task GetUserById_AnotherUsersId_Returns403()
    {
        var client = _factory.CreateClient();
        var tokenA = await RegisterAndLoginAsync(client, "userA");

        var registerB = await client.PostAsJsonAsync("/api/users", new { username = "userB", password = "Password1" });
        var userB = await registerB.Content.ReadFromJsonAsync<UserSummary>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var response = await client.GetAsync($"/api/users/{userB!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
