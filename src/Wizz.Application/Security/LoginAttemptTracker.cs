namespace Wizz.Application.Security;

using System.Collections.Concurrent;

/// <summary>
/// حماية من تخمين كلمات المرور: بعد <see cref="MaxFailures"/> محاولات فاشلة
/// يُقفل الحساب مؤقتًا لمدة <see cref="LockoutDuration"/>. الحالة في الذاكرة وتُصفَّر
/// بإعادة تشغيل التطبيق — كافية لنقطة بيع محلية أحادية العملية.
/// </summary>
public sealed class LoginAttemptTracker
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, AttemptState> _attempts =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsLocked(string username, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;

        var now = nowUtc ?? DateTime.UtcNow;
        if (!_attempts.TryGetValue(Normalize(username), out var state)) return false;

        if (state.LockedUntilUtc is { } lockedUntil)
        {
            if (lockedUntil > now) return true;
            _attempts.TryRemove(Normalize(username), out _);
            return false;
        }

        if (now - state.LastFailureUtc > FailureWindow)
        {
            _attempts.TryRemove(Normalize(username), out _);
            return false;
        }

        return false;
    }

    /// <summary>يسجل فشلًا ويعيد عدد المحاولات المتبقية قبل القفل.</summary>
    public int RecordFailure(string username, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var key = Normalize(username);

        var state = _attempts.AddOrUpdate(
            key,
            _ => new AttemptState(1, now, null),
            (_, existing) =>
            {
                var failures = now - existing.LastFailureUtc > FailureWindow ? 1 : existing.Failures + 1;
                var lockedUntil = failures >= MaxFailures ? now.Add(LockoutDuration) : (DateTime?)null;
                return new AttemptState(failures, now, lockedUntil);
            });

        return state.LockedUntilUtc is { } until && until > now
            ? 0
            : Math.Max(0, MaxFailures - state.Failures);
    }

    public void Reset(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        _attempts.TryRemove(Normalize(username), out _);
    }

    private static string Normalize(string username) => username.Trim().ToLowerInvariant();

    private sealed record AttemptState(int Failures, DateTime LastFailureUtc, DateTime? LockedUntilUtc);
}
