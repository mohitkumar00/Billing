namespace BillMate.Domain.Common;

/// <summary>
/// Base entity with tenant isolation, soft-delete tracking, UTC audit timestamps, and domain events.
/// </summary>
public abstract class BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid BusinessId { get; protected set; }
    public bool IsDeleted { get; protected set; }
    public DateTimeOffset? DeletedAt { get; protected set; }
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public void MarkUpdated(DateTimeOffset? utcNow = null) => UpdatedAt = utcNow ?? DateTimeOffset.UtcNow;

    public virtual void SoftDelete(DateTimeOffset? utcNow = null)
    {
        if (IsDeleted) return;
        var now = utcNow ?? DateTimeOffset.UtcNow;
        IsDeleted = true;
        DeletedAt = now;
        UpdatedAt = now;
    }
}
