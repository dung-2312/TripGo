using System.Security.Cryptography;
using System.Text;
using Tripgo.Application.Interfaces;

namespace Tripgo.Application.Services;

public sealed class InMemoryAuthRateLimiter : IAuthRateLimiter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LimiterState> _entries = new(StringComparer.Ordinal);
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly int _maximumEntries;

    public InMemoryAuthRateLimiter(int maximumEntries = 10_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
    }

    public bool IsLoginLocked(string key, DateTimeOffset now)
    {
        lock (_sync)
        {
            CleanupExpired(now);
            return _entries.TryGetValue(HashKey(key), out var state) && state.LockoutUntil > now;
        }
    }

    public bool TryRecordLoginFailure(string key, DateTimeOffset now, int failureLimit, TimeSpan lockoutDuration)
    {
        lock (_sync)
        {
            CleanupExpired(now);
            var hashedKey = HashKey(key);
            if (!TryGetOrCreate(hashedKey, now, out var state))
            {
                return false;
            }

            var duration = lockoutDuration > TimeSpan.Zero ? lockoutDuration : TimeSpan.FromMinutes(1);
            var priorFailureCount = state.FailureExpiresAt > now ? state.FailedCount : 0;
            var count = priorFailureCount + 1;
            var lockoutUntil = count >= Math.Max(1, failureLimit) ? now.Add(duration) : DateTimeOffset.MinValue;
            var failureExpiresAt = now.Add(duration);
            _entries[hashedKey] = state with
            {
                FailedCount = count,
                LockoutUntil = lockoutUntil,
                FailureExpiresAt = failureExpiresAt,
                ExpiresAt = Max(failureExpiresAt, state.WindowExpiresAt)
            };
            return true;
        }
    }

    public void ClearLoginFailures(string key)
    {
        lock (_sync)
        {
            var hashedKey = HashKey(key);
            if (!_entries.TryGetValue(hashedKey, out var state))
            {
                return;
            }

            if (state.WindowExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(hashedKey);
                return;
            }

            _entries[hashedKey] = state with
            {
                FailedCount = 0,
                LockoutUntil = DateTimeOffset.MinValue,
                FailureExpiresAt = DateTimeOffset.MinValue,
                ExpiresAt = state.WindowExpiresAt
            };
        }
    }

    public bool TryAcquire(string key, DateTimeOffset now, int limit, TimeSpan window)
    {
        lock (_sync)
        {
            CleanupExpired(now);
            var hashedKey = HashKey(key);
            if (!TryGetOrCreate(hashedKey, now, out var state))
            {
                return false;
            }

            var duration = window > TimeSpan.Zero ? window : TimeSpan.FromMinutes(1);
            if (state.WindowExpiresAt <= now)
            {
                state = state with { WindowCount = 0, WindowExpiresAt = now.Add(duration) };
            }

            var updated = state with
            {
                WindowCount = state.WindowCount + 1,
                ExpiresAt = Max(state.FailureExpiresAt, state.WindowExpiresAt)
            };
            _entries[hashedKey] = updated;
            return updated.WindowCount <= Math.Max(1, limit);
        }
    }

    private bool TryGetOrCreate(string hashedKey, DateTimeOffset now, out LimiterState state)
    {
        if (_entries.TryGetValue(hashedKey, out state!))
        {
            return true;
        }

        if (_entries.Count >= _maximumEntries)
        {
            state = null!;
            return false;
        }

        state = new LimiterState(
            0,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            0,
            DateTimeOffset.MinValue,
            now);
        _entries.Add(hashedKey, state);
        return true;
    }

    private void CleanupExpired(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(key);
        }
    }

    private string HashKey(string key)
    {
        var digest = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(digest);
    }

    private static DateTimeOffset Max(params DateTimeOffset[] values) => values.Max();

    private sealed record LimiterState(
        int FailedCount,
        DateTimeOffset LockoutUntil,
        DateTimeOffset FailureExpiresAt,
        int WindowCount,
        DateTimeOffset WindowExpiresAt,
        DateTimeOffset ExpiresAt);
}
