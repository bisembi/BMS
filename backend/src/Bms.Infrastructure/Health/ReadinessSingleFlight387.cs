namespace Bms.Infrastructure.Health;

/// <summary>Coalesces overlapping probes, never caches a completed verdict.</summary>
internal sealed class ReadinessSingleFlight387(TimeSpan operationTimeout)
{
    private readonly object sync = new();
    private Task<DatabaseSchemaReadiness>? running;

    internal Task<DatabaseSchemaReadiness> RunAsync(
        Func<CancellationToken, Task<DatabaseSchemaReadiness>> inspect,
        CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        Task<DatabaseSchemaReadiness> current;
        lock (sync)
        {
            if (running is null || running.IsCompleted)
            {
                // Caller cancellation belongs to WaitAsync below, not to the
                // shared operation. Its independent lifetime is bounded here.
                running = InspectWithDeadlineAsync(inspect, CancellationToken.None);
                // Every waiter may leave. Observe a later failure without
                // suppressing it for any waiter that is still present.
                _ = running.ContinueWith(task => { _ = task.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            current = running;
        }
        return current.WaitAsync(callerToken);
    }

    private async Task<DatabaseSchemaReadiness> InspectWithDeadlineAsync(
        Func<CancellationToken, Task<DatabaseSchemaReadiness>> inspect,
        CancellationToken lifetimeToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        deadline.CancelAfter(operationTimeout);
        // Await the actual core including its await-using cleanup. Requesting
        // cancellation must not mark the flight completed before SQL releases.
        return await inspect(deadline.Token);
    }
}
