namespace Tripgo.Domain.Entities;

public class Location
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Province { get; set; }

    public string? Country { get; set; } = "Vietnam";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;
}