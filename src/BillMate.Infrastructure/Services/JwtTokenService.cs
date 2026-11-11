using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BillMate.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BillMate.Infrastructure.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "BillMate.Local";
    public string Audience { get; set; } = "BillMate.Clients";
    public string SigningKey { get; set; } = "BillMate_Default_Dev_Signing_Key_Change_In_Prod_32BytesMin!";
    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        _options = new JwtOptions
        {
            Issuer = section[nameof(JwtOptions.Issuer)] ?? new JwtOptions().Issuer,
            Audience = section[nameof(JwtOptions.Audience)] ?? new JwtOptions().Audience,
            SigningKey = section[nameof(JwtOptions.SigningKey)] ?? new JwtOptions().SigningKey,
            AccessTokenMinutes = int.TryParse(section[nameof(JwtOptions.AccessTokenMinutes)], out var mins) ? mins : 15
        };
    }

    public string GenerateAccessToken(
        Guid userId,
        Guid businessId,
        string email,
        string roleName,
        IEnumerable<string> permissions,
        Guid? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("business_id", businessId.ToString()),
            new(ClaimTypes.Role, roleName)
        };

        if (deviceId.HasValue)
            claims.Add(new Claim("device_id", deviceId.Value.ToString()));

        foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("permission", permission));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshTokenRaw() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
