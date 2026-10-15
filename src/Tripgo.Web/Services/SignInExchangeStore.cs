using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Tripgo.Web.Services;

public class SignInExchangeStore : ISignInExchangeStore
{
    private static readonly ConcurrentDictionary<string, (SignInExchangeModel Model, DateTimeOffset ExpiresAt)> _tokens = new();

    public string Store(SignInExchangeModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Clean up expired tokens
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _tokens)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                _tokens.TryRemove(entry.Key, out _);
            }
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = (model, now.AddSeconds(60));
        return token;
    }

    public bool TryRetrieve(string token, out SignInExchangeModel model)
    {
        model = null!;
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (_tokens.TryRemove(token, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                model = entry.Model;
                return true;
            }
        }

        return false;
    }
}
