using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DairyBidding.SharedKernel;

/// <summary>
/// Executes a save action and retries on <see cref="DbUpdateConcurrencyException"/> with
/// exponential jitter. The action must re-apply entity mutations on each invocation;
/// conflicting entries are reloaded from the database before each retry so the action
/// always works on fresh values.
/// </summary>
/// <remarks>
/// Use the public constants at call sites so the defaults are visible and easy to locate
/// when wiring up configuration (see ADR-039).
/// </remarks>
public static class OptimisticRetry
{
    public const int DefaultMaxAttempts = 5;
    public const int DefaultBaseDelayMs = 50;

    public static async Task ExecuteAsync(
        Func<Task> action,
        int maxAttempts = DefaultMaxAttempts,
        int baseDelayMs = DefaultBaseDelayMs,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (attempt == maxAttempts - 1)
                {
                    logger?.LogError(
                        "Optimistic concurrency exhausted after {Max} attempts. Propagating exception.",
                        maxAttempts);
                    throw;
                }

                logger?.LogWarning(
                    "Optimistic concurrency conflict on attempt {Attempt}/{Max}. Reloading and retrying.",
                    attempt + 1, maxAttempts);

                foreach (var entry in ex.Entries)
                    await entry.ReloadAsync(ct);

                // Exponential jitter: ceiling doubles each attempt to spread retries
                var delayMs = Random.Shared.Next(0, baseDelayMs * (1 << attempt));
                if (delayMs > 0)
                    await Task.Delay(delayMs, ct);
            }
        }
    }
}
