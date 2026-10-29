using System.Diagnostics;

namespace techHiveBackend.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceIdentifier
        });
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var canceled = context.RequestAborted.IsCancellationRequested;
            var responseStarted = context.Response.HasStarted;
            var status = responseStarted ? context.Response.StatusCode
                : canceled ? 499 : failed ? 500 : context.Response.StatusCode;
            var outcome = canceled ? "Interrumpida" : failed ? "Fallida" : "Completada";
            var level = canceled ? LogLevel.Information : failed || status >= 500 ? LogLevel.Error
                : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            // Do not log query strings, headers or bodies: they can contain credentials or personal data.
            logger.Log(level,
                "HTTP {Method} {Path}: estado {StatusCode}, resultado {Outcome}, respuesta iniciada {ResponseStarted}, duración {ElapsedMs:F2} ms. TraceId: {TraceId}",
                context.Request.Method, context.Request.Path.Value, status,
                outcome, responseStarted, Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
        }
    }
}
