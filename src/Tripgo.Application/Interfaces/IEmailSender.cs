using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;

namespace Tripgo.Application.Interfaces;

public interface IEmailSender
{
    Task<bool> SendPasswordResetAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);

    Task<bool> SendBookingConfirmationAsync(
        BookingConfirmationEmailDto confirmation,
        CancellationToken cancellationToken = default);
}
