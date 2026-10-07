namespace Tripgo.Application.DTOs;

public class BusTypeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public BusTypeDto() { }
}
