namespace Tripgo.Application.DTOs;

public class CreateOperatorDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Hotline { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    public CreateOperatorDto() { }
}
