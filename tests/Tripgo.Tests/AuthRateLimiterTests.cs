using System.Net;
using Microsoft.AspNetCore.Http;
using Tripgo.Application.Services;
using Tripgo.Web.Services;

namespace Tripgo.Tests;

public sealed class AuthRateLimiterTests
{
    [Fact]
    public void LoginLockout_ExpiresAndAllowsANewAttemptWindow()
    {
        var limiter = new InMemoryAuthRateLimiter();
        var now = DateTimeOffset.UtcNow;
        const string hashedIdentifier = "hashed-login-identifier";

        Assert.True(limiter.TryRecordLoginFailure(hashedIdentifier, now, 2, TimeSpan.FromMinutes(1)));
        Assert.False(limiter.IsLoginLocked(hashedIdentifier, now));
        Assert.True(limiter.TryRecordLoginFailure(hashedIdentifier, now.AddSeconds(1), 2, TimeSpan.FromMinutes(1)));
        Assert.True(limiter.IsLoginLocked(hashedIdentifier, now.AddSeconds(2)));
        Assert.False(limiter.IsLoginLocked(hashedIdentifier, now.AddMinutes(2)));

        Assert.True(limiter.TryRecordLoginFailure(hashedIdentifier, now.AddMinutes(2), 2, TimeSpan.FromMinutes(1)));
        Assert.False(limiter.IsLoginLocked(hashedIdentifier, now.AddMinutes(2)));
    }

    [Fact]
    public void ResetWindow_IsAtomicUnderConcurrentRequests_AndExpires()
    {
        var limiter = new InMemoryAuthRateLimiter();
        var now = DateTimeOffset.UtcNow;
        var accepted = 0;

        Parallel.For(0, 100, _ =>
        {
            if (limiter.TryAcquire("hashed-reset-key", now, 5, TimeSpan.FromMinutes(1)))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.Equal(5, accepted);
        Assert.True(limiter.TryAcquire("hashed-reset-key", now.AddMinutes(2), 5, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void NewLimiterInstanceDoesNotRetainPriorProcessState()
    {
        var firstProcess = new InMemoryAuthRateLimiter();
        var now = DateTimeOffset.UtcNow;
        Assert.True(firstProcess.TryAcquire("hashed-reset-key", now, 1, TimeSpan.FromMinutes(5)));
        Assert.False(firstProcess.TryAcquire("hashed-reset-key", now, 1, TimeSpan.FromMinutes(5)));

        var afterRestart = new InMemoryAuthRateLimiter();

        Assert.True(afterRestart.TryAcquire("hashed-reset-key", now, 1, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void CapacityDoesNotEvictActiveProtectionAndExpiredEntriesAreCleaned()
    {
        var limiter = new InMemoryAuthRateLimiter(maximumEntries: 1);
        var now = DateTimeOffset.UtcNow;

        Assert.True(limiter.TryAcquire("active-identity", now, 1, TimeSpan.FromMinutes(5)));
        Assert.True(limiter.TryRecordLoginFailure("active-identity", now, 1, TimeSpan.FromMinutes(1)));

        Assert.False(limiter.TryAcquire("new-identity", now, 1, TimeSpan.FromMinutes(5)));
        Assert.True(limiter.IsLoginLocked("active-identity", now));

        Assert.True(limiter.TryAcquire("new-identity", now.AddMinutes(6), 1, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void LoginFailureCannotBeRecordedWhenLimiterIsFull()
    {
        var limiter = new InMemoryAuthRateLimiter(maximumEntries: 1);
        var now = DateTimeOffset.UtcNow;

        Assert.True(limiter.TryAcquire("first", now, 5, TimeSpan.FromMinutes(1)));
        Assert.False(limiter.TryRecordLoginFailure("second", now, 1, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ClientAddressProviderUsesConnectionAddressAndIgnoresUnprocessedForwardedHeaders()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.12";
        var provider = new HttpContextAuthClientAddressProvider(new HttpContextAccessor { HttpContext = context });

        Assert.Equal("10.0.0.5", provider.GetClientAddress());
    }

    [Fact]
    public void ClientAddressProviderNormalizesMappedIpv4Addresses()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");
        var provider = new HttpContextAuthClientAddressProvider(new HttpContextAccessor { HttpContext = context });

        Assert.Equal("192.0.2.10", provider.GetClientAddress());
    }
}
