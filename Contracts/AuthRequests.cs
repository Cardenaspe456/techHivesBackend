using System.ComponentModel.DataAnnotations;

namespace techHiveBackend.Contracts;

public sealed class RegisterRequest : UserRequest
{
    [Required(ErrorMessage = "El password es obligatorio.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "El password debe tener entre 12 y 128 caracteres.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(254, ErrorMessage = "El correo no puede superar 254 caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El password es obligatorio.")]
    [StringLength(128, ErrorMessage = "El password no puede superar 128 caracteres.")]
    public string Password { get; set; } = string.Empty;
}

public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAt);
