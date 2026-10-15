using System;

namespace Tripgo.Web.Services;

public class SignInExchangeModel
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
    public string? GuestOwnerIdentifier { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface ISignInExchangeStore
{
    string Store(SignInExchangeModel model);
    bool TryRetrieve(string token, out SignInExchangeModel model);
}
