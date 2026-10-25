using System;
using HarmonyLib;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;

namespace OpenNestCoop.GameSync;

// One atomic host snapshot replaces three independent timer value bindings.
public sealed class CounterBatteryClockSync : ISyncedModule
{
    private const string Channel = "counterbattery.clock";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    private static bool _patched;
    private static bool _applying;
    private float _timer;
    private CounterBatterySaveData _pending;
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new CounterBatteryClockSync(), Channel);

    private static bool AllowLocalTimer()
    {
        var net = CoopRuntime.Net;
        return _applying || net == null || net.IsHost || net.State != SessionState.Joined;
    }

    private static void EnsurePatches()
    {
        if (_patched) return;
        var harmony = new HarmonyLib.Harmony("opennest.counterbattery.hostclock");
        var prefix = new HarmonyMethod(typeof(CounterBatteryClockSync), nameof(AllowLocalTimer));
        foreach (var name in new[] { "Update", "Init", "StartTimer", "UnpauseTimer", "PauseTimer", "AddTime", "SetTime", "PermanentlyStop" })
        {
            var method = AccessTools.Method(typeof(CounterBatteryTimer), name);
            if (method == null) throw new MissingMethodException(typeof(CounterBatteryTimer).Name, name);
            harmony.Patch(method, prefix: prefix);
        }
        _patched = true;
    }

    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined)) return;
        EnsurePatches();
        if (!net.IsHost) { ApplyPending(); return; }
        _timer += dt;
        if (_timer < 0.5f || MsgType == 0) return;
        _timer = 0f;
        var clock = CounterBatteryTimer.Instance;
        if (clock == null) return;
        var save = clock.CaptureMissionState();
        if (save == null) return;
        var writer = NetProtocol.Begin((MsgType)MsgType);
        writer.Put(save.TotalDurationSeconds); writer.Put(save.RemainingSeconds);
        writer.Put(save.Running); writer.Put(save.Expired); writer.Put(save.PermanentlyStopped);
        var packet = NetProtocol.Snapshot(writer);
        foreach (var player in net.Roster)
            if (!player.IsLocal) net.Transport.Send(player.SteamId, packet, true);
    }

    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null || net.IsHost || from != net.HostSteamId) return;
        try
        {
            var reader = new NetDataReader(data); reader.GetByte();
            float total = reader.GetFloat(), remaining = reader.GetFloat();
            if (!float.IsFinite(total) || !float.IsFinite(remaining) || total < 0f || remaining < 0f) return;
            _pending = new CounterBatterySaveData {
                Exists = true, TotalDurationSeconds = total, RemainingSeconds = remaining,
                Running = reader.GetBool(), Expired = reader.GetBool(), PermanentlyStopped = reader.GetBool()
            };
            ApplyPending();
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[CounterBatteryClockSync] receive: {ex.Message}"); }
    }

    private void ApplyPending()
    {
        if (_pending == null || CounterBatteryTimer.Instance == null) return;
        var clock = CounterBatteryTimer.Instance;
        _applying = true;
        try
        {
            clock.RestoreMissionState(_pending);
            clock.onTimerTick?.Invoke(_pending.RemainingSeconds);
            _pending = null;
        }
        finally { _applying = false; }
    }
    public void OnSessionStarted() { Reset(); EnsurePatches(); }
    public void OnSessionEnded() { Reset(); }
    public void Reset() { _timer = 0f; _pending = null; _applying = false; }
}
