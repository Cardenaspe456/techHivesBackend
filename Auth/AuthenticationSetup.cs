using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using techHiveBackend.Data;
using techHiveBackend.Models;

namespace techHiveBackend.Auth;

public static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>().Bind(configuration.GetSection("Jwt"))
            .Validate(settings => Encoding.UTF8.GetByteCount(settings.Key) >= 32,
                "Configura Jwt:Key con un secreto de al menos 32 bytes mediante variables de entorno o user-secrets.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer) && !string.IsNullOrWhiteSpace(settings.Audience),
                "Jwt:Issuer y Jwt:Audience son obligatorios.")
            .Validate(settings => settings.ExpirationMinutes is >= 1 and <= 120,
                "Jwt:ExpirationMinutes debe estar entre 1 y 120.").ValidateOnStart();
        services.AddScoped<IPasswordHasher<UserCredential>, PasswordHasher<UserCredential>>();
        services.AddScoped<TokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, settings) =>
            {
                var config = settings.Value;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = config.Issuer,
                    ValidateAudience = true, ValidAudience = config.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Key)),
                    ValidateLifetime = true, RequireExpirationTime = true,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ClockSkew = TimeSpan.FromSeconds(15)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirst("sub")?.Value;
                        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        if (!int.TryParse(subject, out var id) ||
                            !await db.UserCredentials.AnyAsync(c => c.UserId == id, context.HttpContext.RequestAborted))
                            context.Fail("La cuenta ya no existe.");
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await WriteProblem(context.HttpContext, 401, "Se requiere un token válido para acceder.");
                    },
                    OnForbidden = context => WriteProblem(context.HttpContext, 403, "No tienes permiso para acceder.")
                };
            });
        services.AddAuthorization();
        return services;
    }

    private static Task WriteProblem(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context, ProblemDetails = new ProblemDetails { Status = status, Title = title }
        }).AsTask();
    }
}
