namespace Tripgo.Application.DTOs;

public class LocationDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Province { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; }

    public LocationDto() { }
    public LocationDto(long id, string name, string? province)
    {
        Id = id;
        Name = name;
        Province = province;
    }
}
