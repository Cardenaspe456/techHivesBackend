using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using techHiveBackend.Middleware;
using Xunit;

namespace techHiveBackend.Tests;

public sealed class RequestLoggingTests
{
    [Theory]
    [InlineData(200, LogLevel.Information)]
    [InlineData(401, LogLevel.Warning)]
    [InlineData(503, LogLevel.Error)]
    public async Task LogsStatusDurationAndTraceWithoutSecrets(int status, LogLevel expectedLevel)
    {
        var logger = new RecordingLogger();
        var middleware = new RequestLoggingMiddleware(context =>
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }, logger);
        var context = new DefaultHttpContext { TraceIdentifier = "trace-test" };
        context.Request.Method = "POST";
        context.Request.Path = "/api/auth/login";
        context.Request.QueryString = new QueryString("?password=SECRET");
        context.Request.Headers.Authorization = "Bearer SECRET";
        using var body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("SECRET"));
        context.Request.Body = body;
        await middleware.InvokeAsync(context);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expectedLevel, entry.Level);
        Assert.Contains("POST /api/auth/login", entry.Message);
        Assert.Contains(status.ToString(), entry.Message);
        Assert.Contains("ms", entry.Message);
        Assert.Contains("trace-test", entry.Message);
        Assert.DoesNotContain("SECRET", entry.Message);
    }

    [Fact]
    public async Task UnhandledExceptionIsLoggedAndRethrown()
    {
        var logger = new RecordingLogger();
        var middleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException("SECRET"), logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Entries).Level);
        Assert.DoesNotContain("SECRET", logger.Entries[0].Message);
    }

    [Fact]
    public async Task CancellationIsInformational()
    {
        var logger = new RecordingLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(new DefaultHttpContext { RequestAborted = cancellation.Token });
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("499", entry.Message);
    }

    [Theory]
    [InlineData(false, LogLevel.Error, "Fallida")]
    [InlineData(true, LogLevel.Information, "Interrumpida")]
    public async Task FailureAfterResponseStartedPreservesHttpStatus(bool canceled, LogLevel level, string outcome)
    {
        var logger = new RecordingLogger();
        using var cancellation = new CancellationTokenSource();
        if (canceled) cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature { StatusCode = 202 });
        var middleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException("SECRET"), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(level, entry.Level);
        Assert.Contains("estado 202", entry.Message);
        Assert.Contains(outcome, entry.Message);
        Assert.Contains("respuesta iniciada True", entry.Message);
        Assert.DoesNotContain("estado 500", entry.Message);
        Assert.DoesNotContain("estado 499", entry.Message);
        Assert.DoesNotContain("SECRET", entry.Message);
        Assert.Equal(202, context.Response.StatusCode);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class RecordingLogger : ILogger<RequestLoggingMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
