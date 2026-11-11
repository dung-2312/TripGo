using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tripgo.Application.Services;

/// <summary>
/// Short-lived process-wide cache for rarely-changing reference data (locations, routes). Entries expire
/// after <see cref="Ttl"/>, so changes made elsewhere (admin screens, SQL scripts) show up within a minute;
/// code paths in this process that change the data call <see cref="Clear"/> to show it immediately.
/// </summary>
public sealed class ReferenceDataCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, object Value)> _entries = new();

    public async Task<List<T>> GetOrAddListAsync<T>(string key, Func<Task<List<T>>> factory)
    {
        var now = DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
        {
            return new List<T>((List<T>)entry.Value);
        }

        var value = await factory();
        _entries[key] = (now.Add(Ttl), value);
        return new List<T>(value);
    }

    public void Clear() => _entries.Clear();
}
