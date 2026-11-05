using Tripgo.Application.DTOs;

namespace Tripgo.Tests;

internal sealed class TestCreateBookingRequestDto : CreateBookingRequestDto
{
    public TestCreateBookingRequestDto()
    {
        PassengerEmail = "test@example.com";
        ContactEmailConfirmed = true;
    }
}
