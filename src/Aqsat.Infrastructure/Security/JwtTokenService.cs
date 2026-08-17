using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aqsat.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Aqsat.Infrastructure.Security;

/// <summary>
/// Issues a thin identity token: sub + name + exp only. It deliberately does NOT carry
/// organizations/roles/permissions — ScopeResolutionMiddleware resolves those fresh from the
/// database on every request so a permission change takes effect immediately, not only after the
/// token expires.
/// </summary>
public sealed class JwtTokenService(IConfiguration configuration)
{
    public string CreateToken(AppUser user)
    {
        var section = configuration.GetSection("Jwt");
        var key = section["Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = section["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var audience = section["Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var expiryMinutes = int.Parse(section["ExpiryMinutes"] ?? "480");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("name", user.FullName),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
