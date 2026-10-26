namespace OpenNestCoop.Core;

// Only the logger is substituted. LocalTransport itself is linked unchanged
// from the game's production source and uses real loopback TCP sockets.
internal static class CoopRuntime
{
    internal static readonly TestLog LogSource = new();
}
internal sealed class TestLog
{
    internal void LogInfo(string message) { }
    internal void LogWarning(string message) { }
}
