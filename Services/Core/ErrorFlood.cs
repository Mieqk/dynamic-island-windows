namespace DynamicIsland.Services;

/// <summary>
/// Decides whether an unexpected error may be swallowed. One stray error (a handler that threw) should not take
/// the island down; a flood of them (the same error every frame) means something is really broken, and then the
/// app is allowed to stop instead of limping on and filling the log.
/// </summary>
public sealed class ErrorFlood(int max = 10, double windowSeconds = 10)
{
    private readonly Queue<DateTime> _recent = new();

    /// <summary>True: keep running. False: more than <c>max</c> errors within the window.</summary>
    public bool Allow(DateTime now)
    {
        while (_recent.Count > 0 && (now - _recent.Peek()).TotalSeconds > windowSeconds) _recent.Dequeue();
        _recent.Enqueue(now);
        return _recent.Count <= max;
    }
}
