using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Endpoints.Auth;

namespace TwitchVault.Api.Auth;

public sealed class TokenGeneratorService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public JwtTokenResponse GenerateToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        var key = Encoding.UTF8.GetBytes(_options.Secret);
        var expiresIn = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, user.Username),
            new Claim(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresIn.DateTime,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return new JwtTokenResponse(
            tokenHandler.WriteToken(token),
            expiresIn.ToUnixTimeMilliseconds());
    }
}