using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;
using UnityEngine;

namespace OpenNestCoop.GameSync;

// Clicks remain bidirectional in ButtonClickSync. Only the host publishes
// settled control states, so a joining guest cannot overwrite the room.
public sealed class WorldControlsSync : ISyncedModule
{
    private const string Channel = "world.controls";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    private float _poll, _scan, _heartbeat;
    private string _scene = "";
    private readonly Dictionary<string, Component> _objects = new();
    private readonly Dictionary<string, byte> _known = new(), _pending = new();
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new WorldControlsSync(), Channel);
    private static string Key(Component c)
    {
        string path = "";
        for (var t = c.transform; t != null; t = t.parent)
            path = t.name + "[" + t.GetSiblingIndex() + "]/" + path;
        int index = 0;
        foreach (var other in c.GetComponents<Component>())
        { if (other == c) break; if (other.GetType() == c.GetType()) index++; }
        return c.GetType().Name + ":" + index + ":" + path;
    }
    private void Scan()
    {
        _objects.Clear();
        foreach (var t in UnityEngine.Object.FindObjectsOfType<AnimatorBoolToggler>(true))
        {
            if (t == null || t.parameterName != "IsOpen") continue;
            string path = Key(t);
            bool door = false;
            foreach (string word in new[] { "Door", "Gate", "Hatch", "Stair", "Ladder" })
                if (path.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) door = true;
            if (door) _objects[path] = t;
        }
        foreach (var c in UnityEngine.Object.FindObjectsOfType<SwingLightFlickerController>(true)) _objects[Key(c)] = c;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<SwingLightFlicker>(true)) _objects[Key(c)] = c;
    }
    private static byte Read(Component c)
    {
        if (c is AnimatorBoolToggler t) return (byte)(t.GetBool() ? 1 : 0);
        if (c is SwingLightFlickerController controller)
            return (byte)((controller._switchOn ? 1 : 0) | (controller._masterPowerOn ? 2 : 0));
        var light = (SwingLightFlicker)c;
        return (byte)((light._manualOverrideOff ? 1 : 0) | (light._emergencyPlayerOverride ? 2 : 0) | (light._masterPowerOn ? 4 : 0));
    }
    private static void Apply(Component c, byte value)
    {
        if (Read(c) == value) return;
        if (c is AnimatorBoolToggler t) { t.SetBool((value & 1) != 0); return; }
        if (c is SwingLightFlickerController controller)
        {
            controller.SetSwitch((value & 1) != 0, false);
            controller.SetMasterPower((value & 2) != 0, false); return;
        }
        var light = (SwingLightFlicker)c;
        light.SetMasterPowerState((value & 4) != 0, false);
        light.SetManualOverride((value & 1) != 0);
        light.SetEmergencyManualOn((value & 2) != 0);
    }
    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined)) return;
        string scene = MissionManager.Instance?.CurrentMissionSceneName ?? "";
        if (_scene != scene) { Reset(); _scene = scene; }
        _poll += dt; _scan -= dt; _heartbeat -= dt;
        if (_poll < .5f || MsgType == 0) return;
        _poll = 0;
        try
        {
            if (_scan <= 0) { Scan(); _scan = 4; }
            if (!net.IsHost)
            {
                foreach (var pair in new List<KeyValuePair<string, byte>>(_pending))
                    if (_objects.TryGetValue(pair.Key, out var obj) && obj != null)
                    { Apply(obj, pair.Value); _pending.Remove(pair.Key); }
                return;
            }
            bool all = _heartbeat <= 0;
            if (all) _heartbeat = 4;
            foreach (var pair in _objects)
            {
                if (pair.Value == null) continue;
                byte value = Read(pair.Value);
                if (!all && _known.TryGetValue(pair.Key, out byte old) && old == value) continue;
                _known[pair.Key] = value;
                var w = NetProtocol.Begin((MsgType)MsgType);
                w.Put(MissionManager.Instance?.CurrentMissionSceneName ?? ""); w.Put(pair.Key); w.Put(value);
                net.EnqueueBatch(NetProtocol.Snapshot(w), true);
            }
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning("[WorldControls] " + ex.Message); }
    }
    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null || net.IsHost || from != net.HostSteamId) return;
        var r = new NetDataReader(data); r.SkipBytes(NetProtocol.TypeLen);
        if (r.GetString() != (MissionManager.Instance?.CurrentMissionSceneName ?? "")) return;
        string key = r.GetString(); byte value = r.GetByte();
        if (key.Length < 2048 && _pending.Count < 4096) _pending[key] = value;
    }
    public void OnSessionStarted() => Reset();
    public void OnSessionEnded() => Reset();
    public void Reset() { _objects.Clear(); _known.Clear(); _pending.Clear(); _poll = _scan = _heartbeat = 0; }
}
