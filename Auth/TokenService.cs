using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using techHiveBackend.Contracts;

namespace techHiveBackend.Auth;

public sealed class TokenService(IOptions<JwtSettings> settings)
{
    public TokenResponse Create(int userId)
    {
        var config = settings.Value;
        var expires = DateTime.UtcNow.AddMinutes(config.ExpirationMinutes);
        var token = new JwtSecurityToken(config.Issuer, config.Audience,
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) },
            notBefore: DateTime.UtcNow, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Key)),
                SecurityAlgorithms.HmacSha256));
        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires);
    }
}
