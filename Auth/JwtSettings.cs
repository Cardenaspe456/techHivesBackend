namespace techHiveBackend.Auth;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "techHiveBackend";
    public string Audience { get; set; } = "techHiveClients";
    public string Key { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 30;
}
