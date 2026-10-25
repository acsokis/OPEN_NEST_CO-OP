using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Il2CppInterop.Runtime;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;
using UnityEngine;

namespace OpenNestCoop.GameSync;

// Initial transfer uses the game's map save/restore routines, including the
// dynamic photo/impact objects absent from the old transform-only snapshot.
// It never calls SaveCurrentMission or restores progression/mission graphs.
public sealed class NativeMapSnapshotSync : ISyncedModule
{
    private const string Channel = "map.native.snapshot";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    public NetModulePriority NetPriority => NetModulePriority.Critical;
    public NativeMapSnapshotSync() { CoopRuntime.LogSource?.LogInfo("[NativeMapSnapshot] registered: native map initial state"); }
    private string _scene = "";
    private float _readyAge, _retry;
    private bool _applied;
    private int _request;
    private MissionSaveData _pending;
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new NativeMapSnapshotSync(), Channel);

    private static string Scene => MissionManager.Instance?.CurrentMissionSceneName ?? "";
    private static bool Ready => FireMission.Instance != null && MissionSaveManager.Instance != null
        && ImpactMarkerManager.Instance != null && MissionManager.Instance?.CurrentMission != null
        && GameObject.Find("Draggable Surface") != null;

    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || net.IsHost || net.State != SessionState.Joined || MsgType == 0) return;
        string scene = Scene;
        if (_scene != scene) { Reset(); _scene = scene; }
        if (!Ready || string.IsNullOrEmpty(scene)) { _readyAge = 0f; return; }
        float now = Time.realtimeSinceStartup;
        if (_readyAge <= 0f) _readyAge = now;
        if (now - _readyAge < 3f || _applied) return;
        if (_pending != null) { ApplyPending(); return; }
        if (now < _retry) return;
        _retry = now + 8f;
        var w = NetProtocol.Begin((MsgType)MsgType);
        w.Put((byte)0); w.Put(_scene); w.Put(++_request);
        net.Transport.Send(net.HostSteamId, NetProtocol.Snapshot(w), true);
        CoopRuntime.LogSource?.LogInfo($"[NativeMapSnapshot] requested scene='{_scene}' request={_request}");
    }

    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null) return;
        try
        {
            var r = new NetDataReader(data); r.SkipBytes(NetProtocol.TypeLen);
            byte operation = r.GetByte(); string scene = r.GetString(); int request = r.GetInt();
            if (scene != Scene || !Ready) return;
            if (operation == 0)
            {
                if (!net.IsHost) return;
                bool member = false;
                foreach (var player in net.Roster) if ((ulong)player.SteamId == from && !player.IsLocal) member = true;
                if (!member) return;
                MissionSaveManager.Instance.PrepareMapTokenIdentities();
                var state = new MissionSaveData {
                    MapTokens = MissionSaveManager.Instance.CaptureMapTokens(),
                    DynamicMapObjects = ImpactMarkerManager.Instance.CaptureMissionObjects()
                };
                var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(state));
                byte[] packed = MapSnapshotCodec.Encode(json);
                var w = NetProtocol.Begin((MsgType)MsgType);
                w.Put((byte)1); w.Put(scene); w.Put(request); w.Put(packed, 0, packed.Length);
                net.SendDirectFragmented(from, NetProtocol.Snapshot(w), true);
                CoopRuntime.LogSource?.LogInfo($"[NativeMapSnapshot] sent tokens={state.MapTokens?.Count ?? 0} objects={state.DynamicMapObjects?.Count ?? 0} bytes={packed.Length}");
            }
            else if (operation == 1 && !net.IsHost && from == net.HostSteamId && request == _request && !_applied)
            {
                var bytes = r.GetRemainingBytes();
                _pending = JsonUtility.FromJson(Encoding.UTF8.GetString(MapSnapshotCodec.Decode(bytes)), Il2CppType.Of<MissionSaveData>()).Cast<MissionSaveData>();
            }
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[NativeMapSnapshot] packet: {ex.Message}"); }
    }

    private void ApplyPending()
    {
        var state = _pending;
        // Mark consumed before invoking native events; failed restores are
        // reported rather than repeatedly recreating dynamic objects.
        _pending = null; _applied = true;
        try
        {
            if (state.DynamicMapObjects != null)
            {
                foreach (var obj in state.DynamicMapObjects)
                    if (obj != null) obj.TriggerNormalEvents = false;
                ImpactMarkerManager.Instance.RestoreMissionObjects(state.DynamicMapObjects, MissionManager.Instance.CurrentMission);
            }
            MissionSaveManager.Instance.PrepareMapTokenIdentities();
            if (state.MapTokens != null) MissionSaveManager.Instance.RestoreMapTokens(state.MapTokens);
            StateSnapshotSync.RequestSnapshot(); // drawings/entities, without loading state
            CoopRuntime.LogSource?.LogInfo($"[NativeMapSnapshot] applied tokens={state.MapTokens?.Count ?? 0} objects={state.DynamicMapObjects?.Count ?? 0}");
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogError($"[NativeMapSnapshot] restore failed; reconnect required: {ex.Message}"); }
    }
    public void OnSessionStarted() { Reset(); }
    public void OnSessionEnded() { Reset(); }
    public void Reset() { _scene = ""; _readyAge = _retry = 0f; _applied = false; _pending = null; _request = 0; }
}
