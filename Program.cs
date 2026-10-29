using Microsoft.EntityFrameworkCore;
using techHiveBackend.Data;
using techHiveBackend.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using techHiveBackend.Middleware;
using techHiveBackend.Auth;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails { Status = 429, Title = "El límite de intentos fue alcanzado. Inténtalo más tarde." } });
    };
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        // Keep field names but replace parser diagnostics, which may expose implementation details.
        foreach (var entry in context.ModelState.Values)
        {
            for (var i = 0; i < entry.Errors.Count; i++)
            {
                var error = entry.Errors[i];
                if (error.Exception is not null || !error.ErrorMessage.StartsWith("El ", StringComparison.Ordinal))
                    entry.Errors[i] = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelError("El valor enviado no es válido.");
            }
        }
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState,
            statusCode: 400, title: "El cuerpo o los parámetros de la solicitud no son válidos.");
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    };
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=techhive.db"));

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Obtén un token en /api/auth/login e ingrésalo aquí."
    });
    options.OperationFilter<BearerSecurityOperationFilter>();
});

var app = builder.Build();
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    // Additive upgrade for databases created before authentication was introduced.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "UserCredentials" (
            "UserId" INTEGER NOT NULL CONSTRAINT "PK_UserCredentials" PRIMARY KEY,
            "PasswordHash" TEXT NOT NULL,
            CONSTRAINT "FK_UserCredentials_Users_UserId" FOREIGN KEY ("UserId")
                REFERENCES "Users" ("Id") ON DELETE CASCADE
        );
        """);
}
catch (Exception exception)
{
    app.Logger.LogCritical(exception, "No se pudo inicializar SQLite. La API no puede iniciar.");
    throw;
}
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

public partial class Program { }
