using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using techHiveBackend.Data;
using techHiveBackend.Errors;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using Xunit;

namespace techHiveBackend.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "tests-only-signing-key-at-least-32-bytes-long";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"users-{Guid.NewGuid():N}.db");
    public Exception? SaveFailure { get; set; }
    public string ConnectionString => $"Data Source={database};Default Timeout=1";
    public string JwtKey { get; set; } = SigningKey;

    public ApiFactory(bool legacyDatabase = false)
    {
        if (!legacyDatabase) return;
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "Users" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "FirstName" TEXT NOT NULL, "LastName" TEXT NOT NULL,
                "Email" TEXT COLLATE NOCASE NOT NULL, "CreatedAt" TEXT NOT NULL);
            CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
            INSERT INTO "Users" ("FirstName", "LastName", "Email", "CreatedAt")
                VALUES ('Legacy', 'User', 'legacy@example.com', '2026-01-01 00:00:00');
            """;
        command.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(ConnectionString, sqlite => sqlite.CommandTimeout(1))
                    .AddInterceptors(new FailureInterceptor(this)));
        });
    }

    public HttpClient CreateAnonymousClient() => base.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    public new HttpClient CreateClient()
    {
        var client = CreateAnonymousClient();
        var account = new { firstName = "Test", lastName = "Account", email = "tester@example.com", password = "Testing-password-123" };
        client.PostAsJsonAsync("/api/auth/register", account).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        var response = client.PostAsJsonAsync("/api/auth/login", new { account.email, account.password }).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var token = response.Content.ReadFromJsonAsync<JsonElement>().GetAwaiter().GetResult().GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(database + suffix);
    }

    private sealed class FailureInterceptor(ApiFactory factory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (factory.SaveFailure is { } failure) throw failure;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public class UsersErrorTests
{
    private static object User(string email = "ana@example.com") => new { firstName = "Ana", lastName = "García", email };

    [Fact]
    public async Task ClientCancellationDoesNotWriteErrorResponse()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestServices = provider, RequestAborted = cancellation.Token };
        using var body = new MemoryStream();
        context.Response.Body = body;
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance,
            provider.GetRequiredService<IProblemDetailsService>());
        Assert.True(await handler.TryHandleAsync(context, new OperationCanceledException(cancellation.Token), cancellation.Token));
        Assert.Equal(0, body.Length);
        Assert.False(context.Response.HasStarted);
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        return json;
    }

    [Theory]
    [InlineData("GET", "0")]
    [InlineData("GET", "-1")]
    [InlineData("PUT", "0")]
    [InlineData("DELETE", "-1")]
    public async Task RejectsNonPositiveIds(string method, string id)
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/users/{id}");
        if (method == "PUT") request.Content = JsonContent.Create(User());
        var problem = await AssertProblem(await client.SendAsync(request), HttpStatusCode.BadRequest);
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"firstName\":\" \",\"lastName\":\"G\",\"email\":\"bad\"}")]
    public async Task RejectsInvalidBodies(string body)
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        await AssertProblem(await client.PostAsync("/api/users", new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CrudAndDuplicateEmails()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/users", User());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(location)).StatusCode);
        await AssertProblem(await client.PostAsJsonAsync("/api/users", User("ANA@example.com")), HttpStatusCode.Conflict);
        var second = await client.PostAsJsonAsync("/api/users", User("second@example.com"));
        await AssertProblem(await client.PutAsJsonAsync(second.Headers.Location!, User("ANA@example.com")), HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(location, User("new@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(location)).StatusCode);
        await AssertProblem(await client.GetAsync(location), HttpStatusCode.NotFound);
        await AssertProblem(await client.PutAsJsonAsync(location, User()), HttpStatusCode.NotFound);
        await AssertProblem(await client.DeleteAsync(location), HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/abc")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDeletionReturns404(bool delete)
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/users", User());
        app.SaveFailure = new DbUpdateConcurrencyException("Concurrent deletion");
        await AssertProblem(delete ? await client.DeleteAsync(created.Headers.Location!) :
            await client.PutAsJsonAsync(created.Headers.Location!, User()), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public async Task BusyDatabaseReturns503(int code, bool wrapped)
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var sqlite = new SqliteException("private database diagnostics", code);
        app.SaveFailure = wrapped ? new DbUpdateException("Save failed", sqlite) : sqlite;
        await AssertProblem(await client.PostAsJsonAsync("/api/users", User()), HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ActualSqliteLockReturns503()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var connection = new SqliteConnection(app.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await AssertProblem(await client.PostAsJsonAsync("/api/users", User()), HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task UnexpectedFailureDoesNotExposeDetails()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        app.SaveFailure = new InvalidOperationException("SECRET SQL file.db stack trace");
        var response = await client.PostAsJsonAsync("/api/users", User());
        await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("SECRET", await response.Content.ReadAsStringAsync());
    }
}
