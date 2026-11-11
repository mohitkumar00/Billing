using System.Security.Claims;
using BillMate.Application.Common.Interfaces;

namespace BillMate.Api.Services;

public sealed class HttpCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId => TryGetGuidClaim(ClaimTypes.NameIdentifier);
    public Guid? BusinessId => TryGetGuidClaim("business_id");
    public Guid? DeviceId => TryGetGuidClaim("device_id");
    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public IReadOnlyCollection<string> Permissions =>
        _httpContextAccessor.HttpContext?.User.FindAll("permission").Select(c => c.Value).ToArray()
        ?? [];

    private Guid? TryGetGuidClaim(string claimType)
    {
        var raw = _httpContextAccessor.HttpContext?.User.FindFirstValue(claimType);
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
