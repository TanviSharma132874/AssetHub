using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AssetHub.Shared;
using Microsoft.IdentityModel.Tokens;

namespace AssetHub.Identity;

public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options) => _options = options;

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);

        // Short claim names on purpose; the other modules read sub / name / role.
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("name", user.FullName),
            new("email", user.Email),
            new("role", user.Role),
            new("jti", Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
