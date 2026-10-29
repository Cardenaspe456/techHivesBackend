using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using techHiveBackend.Contracts;
using techHiveBackend.Data;
using techHiveBackend.Models;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace techHiveBackend.Controllers;

[ApiController]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[Route("api/users")]
[ProducesResponseType(typeof(ProblemDetails), 500)]
[ProducesResponseType(typeof(ProblemDetails), 503)]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<User>), 200)]
    public async Task<ActionResult<List<User>>> GetAll(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(User), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<User>> GetById([Range(1, int.MaxValue, ErrorMessage = "El ID debe ser mayor que cero.")] int id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? Problem(statusCode: 404, title: "El usuario no existe.") : Ok(user);
    }

    [HttpPost]
    [ProducesResponseType(typeof(User), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<User>> Create(UserRequest request, CancellationToken cancellationToken)
    {
        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant()
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateEmail(ex))
        {
            return Problem(statusCode: 409, title: "El correo ya está registrado.");
        }
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Update([Range(1, int.MaxValue, ErrorMessage = "El ID debe ser mayor que cero.")] int id, UserRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user is null) return Problem(statusCode: 404, title: "El usuario no existe.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email.Trim().ToLowerInvariant();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateEmail(ex))
        {
            return Problem(statusCode: 409, title: "El correo ya está registrado.");
        }
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Delete([Range(1, int.MaxValue, ErrorMessage = "El ID debe ser mayor que cero.")] int id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user is null) return Problem(statusCode: 404, title: "El usuario no existe.");
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static bool IsDuplicateEmail(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 };
}
