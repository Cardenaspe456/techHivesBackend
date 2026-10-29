using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace techHiveBackend.Errors;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException)
            return true;

        var status = StatusCodes.Status500InternalServerError;
        var title = "Ocurrió un error interno. Inténtalo más tarde.";

        if (exception is DbUpdateConcurrencyException)
        {
            status = StatusCodes.Status404NotFound;
            title = "El usuario ya no existe.";
        }
        else if (IsDatabaseBusy(exception))
        {
            status = StatusCodes.Status503ServiceUnavailable;
            title = "La base de datos está ocupada. Inténtalo más tarde.";
        }

        if (status == 500)
            logger.LogError(exception, "Error no controlado. TraceId: {TraceId}", context.TraceIdentifier);
        else if (status == 503)
            logger.LogWarning(exception, "SQLite no disponible. TraceId: {TraceId}", context.TraceIdentifier);

        context.Response.StatusCode = status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
        return true;
    }

    private static bool IsDatabaseBusy(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: 5 or 6 }) return true;
        }
        return false;
    }
}
