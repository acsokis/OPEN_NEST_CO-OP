using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;
using UnityEngine;
#if MELONLOADER
using SleepyNodes = Il2CppSleepyNodes;
using Emitter = Il2CppFMODUnity.StudioEventEmitter;
#else
using Emitter = FMODUnity.StudioEventEmitter;
#endif

namespace OpenNestCoop.GameSync;

// Replicate the game's complete flyover prefab, not a replacement sound or a
// second mission graph. Native animations/reveal children remain native.
public sealed class ScoutPlaneSync : ISyncedModule
{
    private const string Channel = "scout.lifecycle.v1", PrefabName = "Scout Plane flyover";
    public int MsgType => CoopRuntime.Net?.ChannelType(Channel) ?? 0;
    private static ScoutPlaneSync _instance;
    private static bool _patched, _allowSound;
    private ReliableDocumentSync _replica;
    private readonly Dictionary<IntPtr, string> _ids = new();
    private readonly Dictionary<IntPtr, long> _soundSequence = new();
    private readonly Dictionary<string, MapReconClearHandle> _remote = new();
    private readonly Dictionary<string, long> _heard = new();
    private readonly HashSet<string> _warnings = new();
    private GameObject _prefab;
    private long _nextId, _nextSound;
    private float _timer;
    private string _scene = "";
    private IntPtr _mission;
    public ScoutPlaneSync() { _instance = this; }
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new ScoutPlaneSync(), Channel);
    private static bool IsScout(MapReconClearHandle handle) => handle != null && handle.name.StartsWith(PrefabName, StringComparison.Ordinal);
    private static IEnumerable<MapReconClearHandle> Roots() => UnityEngine.Object.FindObjectsOfType<MapReconClearHandle>(true).Where(IsScout);
    private static string Path(Transform transform, Transform root = null)
    {
        string path = "";
        for (var current = transform; current != null && current != root; current = current.parent)
            path = current.name + (path.Length == 0 ? "" : "/" + path);
        return path;
    }
    private static Dictionary<string, T> Parts<T>(MapReconClearHandle root) where T : Component
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var component in root.GetComponentsInChildren<T>(true)) result["root/" + Path(component.transform, root.transform)] = component;
        if (root._allChildren != null)
            foreach (var child in root._allChildren)
            {
                if (child == null || child.transform.IsChildOf(root.transform)) continue;
                foreach (var component in child.GetComponentsInChildren<T>(true))
                    result["registered/" + child.name + "/" + Path(component.transform, child.transform)] = component;
            }
        return result;
    }
    private static MapReconClearHandle Owner(Component component)
    {
        var parent = component.GetComponentInParent<MapReconClearHandle>();
        if (IsScout(parent)) return parent;
        foreach (var root in Roots())
            if (root._allChildren != null)
                foreach (var child in root._allChildren)
                    if (child != null && (component.transform == child.transform || component.transform.IsChildOf(child.transform))) return root;
        return null;
    }
    private static bool SoundPrefix(Emitter __instance)
    {
        if (__instance.gameObject.name != "sfx_deploy_scout" && __instance.gameObject.name != "sfx_tm_map_reveal") return true;
        var net = CoopRuntime.Net;
        return _allowSound || net == null || net.IsHost || net.State != SessionState.Joined || Owner(__instance) == null;
    }
    private static void SoundPostfix(Emitter __instance)
    {
        if (__instance.gameObject.name != "sfx_deploy_scout" && __instance.gameObject.name != "sfx_tm_map_reveal") return;
        var net = CoopRuntime.Net;
        if (net?.IsHost != true || net.State != SessionState.Hosting || _instance == null || Owner(__instance) == null) return;
        _instance._soundSequence[__instance.Pointer] = ++_instance._nextSound;
    }
    private static void EnsurePatches()
    {
        if (_patched) return;
        var harmony = new HarmonyLib.Harmony("opennest.scout.audio");
        foreach (string method in new[] { "Play", "PlayInstance" })
            harmony.Patch(AccessTools.Method(typeof(Emitter), method), prefix: new HarmonyMethod(typeof(ScoutPlaneSync), nameof(SoundPrefix)),
                postfix: new HarmonyMethod(typeof(ScoutPlaneSync), nameof(SoundPostfix)));
        _patched = true;
    }
    private string Capture(string unused)
    {
        var frames = new List<ScoutFrame>();
        foreach (var root in Roots())
        {
            if (root.transform.parent == null) continue;
            if (!_ids.TryGetValue(root.Pointer, out string id)) _ids[root.Pointer] = id = (++_nextId).ToString(CultureInfo.InvariantCulture);
            var position = root.transform.localPosition; var rotation = root.transform.localRotation; var scale = root.transform.localScale;
            var frame = new ScoutFrame { Id = id, Parent = Path(root.transform.parent), Active = root.gameObject.activeSelf,
                Position = new[] { position.x, position.y, position.z }, Rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w }, Scale = new[] { scale.x, scale.y, scale.z } };
            var animations = new List<ScoutAnimation>();
            foreach (var pair in Parts<Animator>(root))
            {
                var animator = pair.Value;
                if (animator.runtimeAnimatorController == null || animator.layerCount == 0) continue;
                var state = animator.GetCurrentAnimatorStateInfo(0);
                animations.Add(new ScoutAnimation { Key = pair.Key, State = state.fullPathHash, Time = state.normalizedTime,
                    Speed = animator.speed, Active = animator.gameObject.activeSelf });
            }
            frame.Animations = animations.ToArray();
            var audio = new List<ScoutAudio>();
            foreach (var pair in Parts<Emitter>(root))
            {
                var emitter = pair.Value; int timeline = 0;
                if (emitter.EventInstance.isValid()) emitter.EventInstance.getTimelinePosition(out timeline);
                audio.Add(new ScoutAudio { Key = pair.Key, Playing = emitter.IsPlaying(), Timeline = Math.Max(0, timeline),
                    Sequence = _soundSequence.TryGetValue(emitter.Pointer, out long sequence) ? sequence : 0 });
            }
            frame.Audio = audio.ToArray(); frames.Add(frame);
            frame.Parts = Parts<Transform>(root).Select(pair =>
            {
                var child = pair.Value; var p = child.localPosition; var r = child.localRotation; var s = child.localScale;
                return new ScoutPart { Key = pair.Key, Position = new[] { p.x, p.y, p.z }, Rotation = new[] { r.x, r.y, r.z, r.w },
                    Scale = new[] { s.x, s.y, s.z }, Active = child.gameObject.activeSelf };
            }).ToArray();
        }
        return ScoutStateCodec.Encode(frames.ToArray());
    }
    private void ResolvePrefab()
    {
        if (_prefab != null) return;
        foreach (var node in Resources.FindObjectsOfTypeAll<SleepyNodes.State_SpawnScoutPlane>())
            if (node != null && node.PlanePrefab != null && node.PlanePrefab.name == PrefabName) { _prefab = node.PlanePrefab; return; }
        throw new InvalidOperationException("Native scout prefab not loaded yet");
    }
    private void Apply(string unused, string payload)
    {
        var frames = ScoutStateCodec.Decode(payload);
        if (frames.Length > 0) ResolvePrefab();
        // Resolve every parent before mutating the live world. A missing map
        // hierarchy remains retryable instead of acknowledging a partial spawn.
        var parents = new Dictionary<string, Transform>();
        foreach (var frame in frames)
        {
            var parent = GameObject.Find(frame.Parent)?.transform;
            if (parent == null)
                parent = UnityEngine.Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => Path(t) == frame.Parent);
            if (parent == null) throw new InvalidOperationException("Scout parent not ready: " + frame.Parent);
            parents[frame.Id] = parent;
        }
        foreach (var frame in frames)
        {
            bool created = !_remote.TryGetValue(frame.Id, out var root) || root == null;
            if (created)
            {
                var clone = UnityEngine.Object.Instantiate(_prefab, parents[frame.Id]);
                root = clone.GetComponent<MapReconClearHandle>();
                if (root == null) { UnityEngine.Object.Destroy(clone); throw new InvalidOperationException("Scout prefab has no native handle"); }
                _remote[frame.Id] = root;
                CoopRuntime.LogSource?.LogInfo("[ScoutSync] native flyover created id=" + frame.Id);
            }
            root.transform.SetParent(parents[frame.Id], false);
            root.transform.localPosition = new Vector3(frame.Position[0], frame.Position[1], frame.Position[2]);
            root.transform.localRotation = new Quaternion(frame.Rotation[0], frame.Rotation[1], frame.Rotation[2], frame.Rotation[3]);
            root.transform.localScale = new Vector3(frame.Scale[0], frame.Scale[1], frame.Scale[2]);
            root.gameObject.SetActive(frame.Active);
            var animations = Parts<Animator>(root);
            foreach (var state in frame.Animations)
            {
                if (!animations.TryGetValue(state.Key, out var animator)) continue;
                animator.gameObject.SetActive(state.Active); animator.speed = state.Speed;
                var local = animator.GetCurrentAnimatorStateInfo(0);
                if (state.State != 0 && (created || local.fullPathHash != state.State || Math.Abs(local.normalizedTime - state.Time) > .1f))
                { animator.Play(state.State, 0, state.Time); animator.Update(0); }
            }
            var audio = Parts<Emitter>(root);
            var parts = Parts<Transform>(root);
            foreach (var state in frame.Parts)
            {
                if (!parts.TryGetValue(state.Key, out var child)) continue;
                child.gameObject.SetActive(state.Active);
                child.localPosition = new Vector3(state.Position[0], state.Position[1], state.Position[2]);
                child.localRotation = new Quaternion(state.Rotation[0], state.Rotation[1], state.Rotation[2], state.Rotation[3]);
                child.localScale = new Vector3(state.Scale[0], state.Scale[1], state.Scale[2]);
            }
            foreach (var state in frame.Audio)
            {
                if (!audio.TryGetValue(state.Key, out var emitter)) continue;
                string key = frame.Id + ":" + state.Key;
                long previous = _heard.TryGetValue(key, out long value) ? value : -1;
                bool play = ScoutStateCodec.PlaySound(created, previous, state, emitter.IsPlaying());
                _allowSound = true;
                try
                {
                    if (play)
                    {
                        emitter.hasTriggered = false; emitter.Play();
                        if (state.Playing && emitter.EventInstance.isValid()) emitter.EventInstance.setTimelinePosition(state.Timeline);
                    }
                    else if (!state.Playing && emitter.IsPlaying()) emitter.Stop();
                }
                finally { _allowSound = false; }
                _heard[key] = Math.Max(previous, state.Sequence);
            }
        }
        var keep = new HashSet<string>(frames.Select(f => f.Id), StringComparer.Ordinal);
        foreach (var pair in _remote.ToArray())
            if (!keep.Contains(pair.Key)) { Remove(pair.Value); _remote.Remove(pair.Key); }
    }
    private static void Remove(MapReconClearHandle root)
    {
        if (root == null) return;
        root.gameObject.SetActive(false); root.DestroyAll();
        if (root != null) UnityEngine.Object.Destroy(root.gameObject);
    }
    private void Report(string warning)
    { if (_warnings.Count < 32 && _warnings.Add(warning)) CoopRuntime.LogSource?.LogWarning("[ScoutSync] " + warning); }
    private void Send(ulong to, DocumentPacket packet)
    {
        var writer = NetProtocol.Begin((MsgType)MsgType); writer.Put(_scene);
        byte[] bytes = DocumentPacketCodec.Encode(packet); writer.Put(bytes, 0, bytes.Length);
        CoopRuntime.Net.SendDirectFragmented(to, NetProtocol.Snapshot(writer), true);
    }
    public void Tick(float dt)
    {
        var net = CoopRuntime.Net;
        if (net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined) || MsgType == 0) return;
        _timer += dt; if (_timer < .25f) return; _timer = 0;
        try
        {
            string scene = MissionManager.Instance?.CurrentMissionSceneName ?? "";
            IntPtr mission = FireMission.Instance?.Pointer ?? IntPtr.Zero;
            if (_scene != scene || _mission != mission) { Reset(); _scene = scene; _mission = mission; }
            if (scene.Length == 0 || mission == IntPtr.Zero) return;
            EnsurePatches();
            _replica ??= new ReliableDocumentSync(net.IsHost, net.HostSteamId, new[] { "scout" }, new[] { "scout" }, Capture, Apply, Send,
                _ => throw new InvalidOperationException("Scout lifecycle has no guest controls"), Report);
            _replica.Peers.Clear(); foreach (var peer in net.Roster) if (!peer.IsLocal) _replica.Peers.Add(peer.SteamId);
            _replica.Tick(Time.realtimeSinceStartup);
            if (!net.IsHost)
            {
                var bound = new HashSet<IntPtr>(_remote.Values.Where(root => root != null).Select(root => root.Pointer));
                foreach (var root in Roots()) if (!bound.Contains(root.Pointer)) Remove(root);
            }
        }
        catch (Exception ex) { Report(ex.Message); }
    }
    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net;
        if (net == null || _replica == null || !net.Roster.Any(peer => !peer.IsLocal && (ulong)peer.SteamId == from)) return;
        try
        {
            var reader = new NetDataReader(data); reader.SkipBytes(NetProtocol.TypeLen);
            if (reader.GetString() != _scene) return;
            _replica.Receive(from, DocumentPacketCodec.Decode(reader.GetRemainingBytes()));
        }
        catch (Exception ex) { Report(ex.Message); }
    }
    public void OnSessionStarted() { Reset(); EnsurePatches(); }
    public void OnSessionEnded() => Reset();
    public void Reset()
    {
        _replica = null; _ids.Clear(); _soundSequence.Clear(); _remote.Clear(); _heard.Clear(); _warnings.Clear();
        _prefab = null; _scene = ""; _mission = IntPtr.Zero; _nextId = _nextSound = 0; _timer = 0;
    }
}
