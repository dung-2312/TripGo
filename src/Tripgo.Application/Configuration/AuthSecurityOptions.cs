namespace Tripgo.Application.Configuration;

public sealed class AuthSecurityOptions
{
    public int LoginFailureLimit { get; set; } = 5;
    public int LoginLockoutMinutes { get; set; } = 5;
    public int LoginRequestLimit { get; set; } = 30;
    public int LoginRequestWindowMinutes { get; set; } = 5;
    public int LoginIpRequestLimit { get; set; } = 100;
    public int PasswordResetRequestLimit { get; set; } = 3;
    public int PasswordResetIpRequestLimit { get; set; } = 10;
    public int PasswordResetWindowMinutes { get; set; } = 15;
    public int PasswordResetTokenLifetimeMinutes { get; set; } = 15;
    public int MaximumRateLimitEntries { get; set; } = 10_000;
}
