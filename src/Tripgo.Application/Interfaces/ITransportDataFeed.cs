namespace Tripgo.Application.Interfaces;

/// <summary>
/// The boundary for an authorized operator API or data feed. There is deliberately no implementation:
/// none exists until an operator has authorized one. See docs/integrations.md for what it takes.
/// An implementation supplies data; it never marks an operator verified or opens a trip for sale.
/// </summary>
public interface ITransportDataFeed
{
    string FeedName { get; }
    string OperatorCode { get; }
    Task<IReadOnlyList<FeedDeparture>> GetDeparturesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record FeedDeparture(
    string ExternalTripId,
    string OriginCode,
    string DestinationCode,
    DateTimeOffset DepartureTime,
    DateTimeOffset ExpectedArrivalTime,
    decimal? Fare,
    string SourceReference);
