namespace Tripgo.Domain.Enums;

/// <summary>Where a piece of transport data came from, and therefore how much it can be trusted.</summary>
public enum TransportDataSource
{
    Unverified = 0,
    SandboxFixture = 1,
    ReferenceSchedule = 2,
    OperatorVerified = 3,
    AuthorizedFeed = 4
}
