namespace Tripgo.Application.Interfaces;

public interface IAuthRateLimiter
{
    bool IsLoginLocked(string key, DateTimeOffset now);
    bool TryRecordLoginFailure(string key, DateTimeOffset now, int failureLimit, TimeSpan lockoutDuration);
    void ClearLoginFailures(string key);
    bool TryAcquire(string key, DateTimeOffset now, int limit, TimeSpan window);
}
