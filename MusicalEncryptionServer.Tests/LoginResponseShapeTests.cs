using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MusicalEncryptionServer.Tests;

/// <summary>
/// A contract test for the exact JSON shape of POST /api/users/login's response — this is
/// what ApiClient.cs (WPF client) was found to not match before this task (it deserialized the
/// response directly as the flat {id, username, createdAt} shape, but the real response nests
/// those fields under "user" alongside "token"/"expiresAtUtc"). If this JSON shape ever drifts
/// again without the client being updated to match, this test fails loudly instead of the
/// client silently getting a null username after a "successful" login.
/// </summary>
public class LoginResponseShapeTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LoginResponseShapeTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginResponse_HasTheNestedShapeTheWpfClientExpects()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/users", new { username = "shape-user", password = "Password1" });

        var login = await client.PostAsJsonAsync("/api/users/login", new { username = "shape-user", password = "Password1" });
        var json = await login.Content.ReadFromJsonAsync<JsonElement>();

        // Top level: "user" (object), "token" (string), "expiresAtUtc" (string/date) — not a
        // flat {id, username, ...} object, which is what the old client code assumed.
        Assert.Equal(JsonValueKind.Object, json.GetProperty("user").ValueKind);
        Assert.Equal(JsonValueKind.String, json.GetProperty("token").ValueKind);
        Assert.True(json.TryGetProperty("expiresAtUtc", out _));

        var user = json.GetProperty("user");
        Assert.True(user.TryGetProperty("id", out _));
        Assert.Equal("shape-user", user.GetProperty("username").GetString());

        // The flat shape the old ApiClient.RegisteredUser record expected does NOT exist at
        // the top level anymore — confirms why deserializing straight into it silently broke.
        Assert.False(json.TryGetProperty("username", out _));
    }
}
