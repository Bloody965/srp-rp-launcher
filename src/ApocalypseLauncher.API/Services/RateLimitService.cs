using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ApocalypseLauncher.API.Services;

public class RateLimitService
{
    private readonly ConcurrentDictionary<string, RateLimitInfo> _attempts = new();
    private readonly Timer _cleanupTimer;

    public RateLimitService()
    {
        // Очистка старых записей каждые 5 минут
        _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public bool IsRateLimited(string key, int maxAttempts = 5, TimeSpan? window = null)
    {
        var timeWindow = window ?? TimeSpan.FromMinutes(15);
        var now = DateTime.UtcNow;

        var info = _attempts.GetOrAdd(key, _ => new RateLimitInfo());

        lock (info)
        {
            // Удаляем старые попытки
            info.Attempts.RemoveAll(a => now - a > timeWindow);

            // Проверяем лимит
            if (info.Attempts.Count >= maxAttempts)
            {
                info.BlockedUntil = now.Add(timeWindow);
                return true;
            }

            // Добавляем новую попытку
            info.Attempts.Add(now);
            return false;
        }
    }

    public void ResetLimit(string key)
    {
        _attempts.TryRemove(key, out _);
    }

    public TimeSpan? GetBlockedTimeRemaining(string key)
    {
        if (_attempts.TryGetValue(key, out var info))
        {
            lock (info)
            {
                if (info.BlockedUntil.HasValue)
                {
                    var remaining = info.BlockedUntil.Value - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero)
                        return remaining;
                }
            }
        }
        return null;
    }

    private void CleanupOldEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _attempts)
        {
            lock (kvp.Value)
            {
                // Удаляем записи старше 1 часа
                if (kvp.Value.Attempts.Count == 0 ||
                    (kvp.Value.Attempts.Count > 0 && now - kvp.Value.Attempts[^1] > TimeSpan.FromHours(1)))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            _attempts.TryRemove(key, out _);
        }
    }

    private class RateLimitInfo
    {
        public List<DateTime> Attempts { get; } = new();
        public DateTime? BlockedUntil { get; set; }
    }
}
