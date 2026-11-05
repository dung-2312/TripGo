using Microsoft.AspNetCore.DataProtection;
using Tripgo.Application.Interfaces;

namespace Tripgo.Web.Services;

public sealed class TicketTokenProtector : ITicketTokenProtector
{
    private readonly IDataProtector _protector;

    public TicketTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Tripgo.TicketToken.v1");
    }

    public string Protect(string token) => _protector.Protect(token);

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Happens if the key ring was lost. The caller reports it rather than showing a broken code.
            return null;
        }
    }
}
