namespace techHiveBackend.Models;

public sealed class UserCredential
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string PasswordHash { get; set; } = string.Empty;
}
