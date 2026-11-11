namespace BillMate.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? BusinessId { get; }
    Guid? DeviceId { get; }
    string? IpAddress { get; }
    IReadOnlyCollection<string> Permissions { get; }
}

public interface IPasswordHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string hash);
}
