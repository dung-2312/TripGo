using System;

namespace Tripgo.Application.DTOs;

public class SupportCallbackRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }

    public SupportCallbackRequestDto() { }
}
