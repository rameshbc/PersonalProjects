namespace Domain.Events;

public abstract record DomainEvent(Guid EventId, DateTime OccurredAt)
{
    protected DomainEvent() : this(Guid.NewGuid(), DateTime.UtcNow) { }
}
