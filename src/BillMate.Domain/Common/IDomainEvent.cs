namespace BillMate.Domain.Common;

/// <summary>
/// Marker interface for domain events raised by aggregates.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOnUtc { get; }
}
