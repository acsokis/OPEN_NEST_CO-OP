using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;
using UnityEngine;

namespace OpenNestCoop.GameSync;

// Calculator printouts are FireMissionCard, not requisition PunchcardRuntime.
// Transfer native save data so the receiver restores the actual printed card.
public sealed class FireMissionCardSync : ISyncedModule
{
    private const string Channel = "firemissioncards";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new FireMissionCardSync(), Channel);
    private readonly HashSet<string> _seen = new();
    private readonly Dictionary<string, string> _pending = new();
    private readonly Dictionary<string, bool> _dragging = new();
    private readonly Dictionary<string, float> _releaseUntil = new();
    private readonly Dictionary<string, GameObject> _tracked = new();
    private readonly Dictionary<string, bool> _wasActive = new();
    private readonly CardReplicationState _state = new();
    private float _timer;
    private float _heartbeat;
    private string _scene = "";
    private IntPtr _missionPointer;
    private int _sceneHandle = -1;
    public FireMissionCardSync() { CoopRuntime.LogSource?.LogInfo("[FireMissionCardSync] registered: creation and shared movement"); }

    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined)) return;
        if (MsgType == 0) return;
        if (!AlignScene()) return;
        _timer += dt;
        _heartbeat += dt;
        if (_timer < 0.15f) return;
        _timer = 0f;
        try
        {
            DetectClosedCards();
            foreach (var item in new Dictionary<string, string>(_pending))
                if (Restore(item.Key, item.Value)) _pending.Remove(item.Key);
            bool heartbeat = net.IsHost && _heartbeat >= 4f;
            if (heartbeat) _heartbeat = 0f;
            if (heartbeat)
                foreach (var id in _state.Deleted) Send(id, "");
            foreach (var card in UnityEngine.Object.FindObjectsOfType<FireMissionCard>(true))
            {
                if (card == null) continue;
                var save = card.CaptureMissionState();
                if (save == null || string.IsNullOrEmpty(save.Id)) continue;
                if (_state.IsDeleted(save.Id))
                {
                    card.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(card.gameObject);
                    continue;
                }
                Track(save.Id, card.gameObject);
                bool fresh = _seen.Add(save.Id);
                var item = card.GetComponent<DraggableItem>();
                bool dragging = item != null && item.IsBeingDragged;
                bool wasDragging = _dragging.TryGetValue(save.Id, out var was) && was;
                _dragging[save.Id] = dragging;
                bool dropped = wasDragging && !dragging;
                if (dropped) _releaseUntil[save.Id] = Time.realtimeSinceStartup + 1f;
                if (!fresh && !heartbeat && !dragging && !dropped) continue;
                var json = JsonUtility.ToJson(save);
                Send(save.Id, json);
            }
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[FireMissionCardSync] tick: {ex.Message}"); }
    }

    private void Send(string id, string json)
    {
        var net = CoopRuntime.Net;
        if (net == null || MsgType == 0) return;
        var w = NetProtocol.Begin((MsgType)MsgType);
        w.Put(_scene); w.Put(id); w.Put(json);
        var data = NetProtocol.Snapshot(w);
        if (net.IsHost)
        {
            foreach (var player in net.Roster)
                if (!player.IsLocal) net.Transport.Send(player.SteamId, data, true);
        }
        else if (net.HostSteamId != 0) net.Transport.Send(net.HostSteamId, data, true);
    }

    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null) return;
        if (net.State != SessionState.Joined && net.State != SessionState.Hosting) return;
        if (!net.IsHost && from != net.HostSteamId) return;
        try
        {
            var reader = new NetDataReader(data);
            reader.SkipBytes(NetProtocol.TypeLen);
            string scene = reader.GetString();
            if (scene != (MissionManager.Instance?.CurrentMissionSceneName ?? "")) return;
            if (!AlignScene()) return;
            string id = reader.GetString();
            string json = reader.GetString();
            if (string.IsNullOrEmpty(id) || json.Length > 65536) return;
            DetectClosedCards();
            if (json.Length == 0)
            {
                Remove(id);
                Forward(net, from, data);
                return;
            }
            if (_state.IsDeleted(id))
            {
                // Tell an out-of-date sender to discard its copy too.
                if (net.IsHost) Send(id, "");
                return;
            }
            if (Restore(id, json)) _pending.Remove(id);
            else if (_pending.Count < 128) _pending[id] = json;
            Forward(net, from, data);
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[FireMissionCardSync] packet: {ex.Message}"); }
    }

    private bool Restore(string id, string json)
    {
        if (_state.IsDeleted(id)) return true;
        var save = JsonUtility.FromJson(json, Il2CppType.Of<FireMissionCardSaveData>()).Cast<FireMissionCardSaveData>();
        if (save == null || save.Id != id) return false;
        // Existing cards are never recreated: do not interrupt player handling.
        foreach (var card in UnityEngine.Object.FindObjectsOfType<FireMissionCard>(true))
        {
            if (card != null && card.CaptureMissionState()?.Id == id)
            {
                Track(id, card.gameObject);
                // A heartbeat is not a fresh placement. Reapplying identical
                // native surface state can reopen a locally closed card view.
                if (!_state.ShouldApply(id, json)) return true;
                var item = card.GetComponent<DraggableItem>();
                if (item != null && item.IsBeingDragged) return true;
                if (_releaseUntil.TryGetValue(id, out var until) && Time.realtimeSinceStartup < until) return true;
                if (item != null)
                {
                    var surface = new MissionSaveManager.DraggableSurfaceSaveState {
                        SurfaceTag = save.SurfaceTag, SurfaceLocalPosition = save.SurfaceLocalPosition,
                        SurfaceLocalRotation = save.SurfaceLocalRotation, WorldPosition = save.WorldPosition,
                        WorldRotation = save.WorldRotation, LocalScale = save.LocalScale, Active = save.Active,
                        Location = save.Location, StackingNormalOffset = save.StackingNormalOffset
                    };
                    MissionSaveManager.RestoreDraggableSurfaceState(item, surface, Surfaces());
                }
                _state.Applied(id, json);
                _seen.Add(id); return true;
            }
        }
        var printers = UnityEngine.Object.FindObjectsOfType<FireMissionCardPrinter>(true);
        if (printers == null || printers.Length != 1) return false;
        printers[0].RestoreCard(save, Surfaces());
        foreach (var card in UnityEngine.Object.FindObjectsOfType<FireMissionCard>(true))
            if (card != null && card.CaptureMissionState()?.Id == id)
            {
                _seen.Add(id);
                Track(id, card.gameObject);
                _state.Applied(id, json);
                CoopRuntime.LogSource?.LogInfo($"[FireMissionCardSync] restored card '{id}'");
                return true;
            }
        return false;
    }

    private static void Forward(OpenNestCoop.Net.NetManager net, ulong from, byte[] data)
    {
        if (!net.IsHost) return;
        foreach (var player in net.Roster)
            if (!player.IsLocal && (ulong)player.SteamId != from)
                net.Transport.Send(player.SteamId, data, true);
    }

    private void Track(string id, GameObject go)
    {
        _tracked[id] = go;
        _wasActive[id] = go != null && go.activeSelf;
    }

    private bool AlignScene()
    {
        var mission = FireMission.Instance;
        string scene = MissionManager.Instance?.CurrentMissionSceneName ?? "";
        if (mission == null || string.IsNullOrEmpty(scene)) { Reset(); return false; }
        int handle = mission.gameObject.scene.handle;
        if (scene != _scene || handle != _sceneHandle || mission.Pointer != _missionPointer)
        {
            Reset();
            _scene = scene; _sceneHandle = handle; _missionPointer = mission.Pointer;
        }
        return true;
    }

    private void DetectClosedCards()
    {
        // Observe before restoring pending/heartbeat updates so an already
        // destroyed or explicitly hidden card cannot be recreated first.
        foreach (var pair in new Dictionary<string, GameObject>(_tracked))
        {
            bool hidden = pair.Value == null;
            if (!hidden && _wasActive.TryGetValue(pair.Key, out bool active) && active)
                hidden = !pair.Value.activeSelf;
            if (!hidden) continue;
            if (Remove(pair.Key)) Send(pair.Key, "");
        }
    }

    private bool Remove(string id)
    {
        bool fresh = _state.Delete(id);
        _pending.Remove(id);
        _dragging.Remove(id);
        _releaseUntil.Remove(id);
        _seen.Remove(id);
        if (_tracked.TryGetValue(id, out var go) && go != null)
        {
            // Hide immediately; Unity destroys at the end of the frame.
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
        _tracked.Remove(id);
        _wasActive.Remove(id);
        if (fresh) CoopRuntime.LogSource?.LogInfo($"[FireMissionCardSync] closed card '{id}'");
        return fresh;
    }

    public void OnSessionStarted() { Reset(); }
    public void OnSessionEnded() { Reset(); }
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DragSurface> Surfaces()
    {
        var found = UnityEngine.Object.FindObjectsOfType<DragSurface>(true);
        var result = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DragSurface>(found.Length);
        for (int i = 0; i < found.Length; i++) result[i] = found[i];
        return result;
    }
    public void Reset()
    {
        _seen.Clear(); _pending.Clear(); _dragging.Clear(); _releaseUntil.Clear();
        _tracked.Clear(); _wasActive.Clear(); _state.Reset();
        _timer = _heartbeat = 0f; _scene = ""; _missionPointer = IntPtr.Zero; _sceneHandle = -1;
    }
}
