using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using techHiveBackend.Data;

namespace techHiveBackend.Tests;

public sealed class AuthenticationTests
{
    private static object Account => new { firstName = "Ana", lastName = "Garcia", email = "ana@example.com", password = "Secure-password-123" };

    [Fact]
    public async Task UpgradesLegacyDatabaseWithoutClaimingExistingAccounts()
    {
        using var app = new ApiFactory(legacyDatabase: true);
        using var client = app.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", Account)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register",
            new { firstName = "Legacy", lastName = "User", email = "legacy@example.com", password = "Secure-password-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "legacy@example.com", password = "Secure-password-123" })).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void RefusesMissingOrShortSigningKey(string key)
    {
        using var app = new ApiFactory { JwtKey = key };
        var exception = Assert.ThrowsAny<Exception>(() => app.CreateAnonymousClient());
        Assert.Contains("Jwt:Key", exception.ToString());
    }

    [Fact]
    public void SwaggerRequiresBearerOnlyForProtectedEndpoints()
    {
        using var app = new ApiFactory();
        using var client = app.CreateAnonymousClient();
        using var scope = app.Services.CreateScope();
        var swagger = scope.ServiceProvider.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>().GetSwagger("v1");
        Assert.NotEmpty(swagger.Paths["/api/users"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Security);
        Assert.True(swagger.Paths["/api/auth/login"].Operations[Microsoft.OpenApi.Models.OperationType.Post].Security is not { Count: > 0 });
    }

    [Fact]
    public async Task RegisterLoginAndProtectedCrud()
    {
        using var app = new ApiFactory();
        using var client = app.CreateAnonymousClient();
        var unauthorized = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Contains(unauthorized.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.True((await unauthorized.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("traceId", out _));
        var registered = await client.PostAsJsonAsync("/api/auth/register", Account);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var body = await registered.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        using (var scope = app.Services.CreateScope())
        {
            var credential = await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserCredentials.SingleAsync();
            Assert.NotEqual("Secure-password-123", credential.PasswordHash);
        }
        var loggedIn = await client.PostAsJsonAsync("/api/auth/login", new { email = "ANA@example.com", password = "Secure-password-123" });
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var token = (await loggedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(registered.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task InvalidCredentialsUseSameMessage()
    {
        using var app = new ApiFactory();
        using var client = app.CreateAnonymousClient();
        await client.PostAsJsonAsync("/api/auth/register", Account);
        foreach (var email in new[] { "ana@example.com", "missing@example.com" })
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("El correo o la contraseña son incorrectos.", body.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task RejectsDuplicateAndWeakRegistration()
    {
        using var app = new ApiFactory();
        using var client = app.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", Account)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", Account)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register",
            new { firstName = "Ana", lastName = "G", email = "new@example.com", password = "short" })).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("malformed")]
    public async Task RejectsInvalidTokens(string scenario)
    {
        using var app = new ApiFactory();
        using var client = app.CreateAnonymousClient();
        await client.PostAsJsonAsync("/api/auth/register", Account);
        var key = scenario == "signature" ? "wrong-signing-key-at-least-32-bytes-long" : ApiFactory.SigningKey;
        var token = new JwtSecurityToken(scenario == "issuer" ? "wrong" : "techHiveBackend",
            scenario == "audience" ? "wrong" : "techHiveClients", new[] { new Claim("sub", "1") },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scenario == "malformed" ? "invalid" : new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task LimitsLoginAttempts()
    {
        using var app = new ApiFactory();
        using var client = app.CreateAnonymousClient();
        HttpResponseMessage? response = null;
        for (var i = 0; i < 11; i++)
            response = await client.PostAsJsonAsync("/api/auth/login", new { email = "missing@example.com", password = "wrong" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("traceId", out _));
    }
}
