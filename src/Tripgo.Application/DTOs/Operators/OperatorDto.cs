namespace Tripgo.Application.DTOs;

public class OperatorDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Hotline { get; set; }
    public string? Address { get; set; }

    public OperatorDto() { }
    public OperatorDto(long id, string name)
    {
        Id = id;
        Name = name;
    }
}
