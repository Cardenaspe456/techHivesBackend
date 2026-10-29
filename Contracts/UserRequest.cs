using System.ComponentModel.DataAnnotations;

namespace techHiveBackend.Contracts;

public class UserRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(100, ErrorMessage = "El nombre no puede superar 100 caracteres.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El apellido es obligatorio."), StringLength(100, ErrorMessage = "El apellido no puede superar 100 caracteres.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo es obligatorio."), EmailAddress(ErrorMessage = "El correo no tiene un formato válido."), StringLength(254, ErrorMessage = "El correo no puede superar 254 caracteres.")]
    public string Email { get; set; } = string.Empty;
}
