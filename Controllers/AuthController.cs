using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using techHiveBackend.Auth;
using techHiveBackend.Contracts;
using techHiveBackend.Data;
using techHiveBackend.Models;

namespace techHiveBackend.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[ProducesResponseType(typeof(ValidationProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
[ProducesResponseType(typeof(ProblemDetails), 500)]
[ProducesResponseType(typeof(ProblemDetails), 503)]
public sealed class AuthController(AppDbContext db, IPasswordHasher<UserCredential> hasher, TokenService tokens) : ControllerBase
{
    // Equal-cost password verification for nonexistent accounts.
    private static readonly string DummyHash = new PasswordHasher<UserCredential>()
        .HashPassword(new UserCredential(), "not-a-real-account-password");

    [HttpPost("register")]
    [ProducesResponseType(typeof(User), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<User>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new User { FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant() };
        var credential = new UserCredential { User = user };
        credential.PasswordHash = hasher.HashPassword(credential, request.Password);
        db.UserCredentials.Add(credential);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            return Problem(statusCode: 409, title: "El correo ya está registrado.");
        }
        return CreatedAtAction(nameof(UsersController.GetById), "Users", new { id = user.Id }, user);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var credential = await db.UserCredentials.Include(c => c.User)
            .SingleOrDefaultAsync(c => c.User.Email == email, cancellationToken);
        var result = hasher.VerifyHashedPassword(credential ?? new UserCredential(),
            credential?.PasswordHash ?? DummyHash, request.Password);
        if (credential is null || result == PasswordVerificationResult.Failed)
            return Problem(statusCode: 401, title: "El correo o la contraseña son incorrectos.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = hasher.HashPassword(credential, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(tokens.Create(credential.UserId));
    }
}
