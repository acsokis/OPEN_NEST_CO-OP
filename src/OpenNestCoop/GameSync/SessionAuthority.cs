namespace OpenNestCoop.GameSync;

internal static class SessionAuthority
{
    internal static bool AcceptHostState(bool localIsHost, bool senderIsHost) => !localIsHost && senderIsHost;
    internal static bool LeaveBeforeLocalNavigation(bool localIsHost, bool joined, bool applyingHostEvent)
        => !localIsHost && joined && !applyingHostEvent;
}
