using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using OpenNestCoop.Core;
using OpenNestCoop.Net;
using UnityEngine;

namespace OpenNestCoop.GameSync;

// Purchase the exact card in the exact station requested by the player. Never
// substitute the first occupied slot, and never debounce distinct operations
// using a global time window.
public sealed class PurchaseSync : ISyncedModule
{
    public int MsgType => 132;
    public static bool IsApplying;
    private static PurchaseSync _instance;
    private sealed class Request
    {
        internal byte Op;
        internal string Id, Scene, Slot, Card;
        internal ulong From;
        internal float Expires, Before;
        internal int RunningBefore;
    }
    private readonly HashSet<string> _seen = new();
    private readonly Dictionary<string, Request> _pending = new();
    private readonly Dictionary<IntPtr, Request> _local = new();
    private float _timer;
    public PurchaseSync() { _instance = this; }
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => CoopSyncRegistry.PendingRegister(false, () => new PurchaseSync());
    private static string Scene => MissionManager.Instance?.CurrentMissionSceneName ?? "";
    private static Request Describe(RequisitionSlot slot) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Scene = Scene, Slot = PunchcardSync.SlotKey(slot.itemSlot),
        Card = slot.CurrentCard?.CurrentDefinition?.ID ?? "", Before = slot.lastRedemptionTime,
        RunningBefore = RequisitionSlot.RunningPunchcards?.Count ?? 0, Expires = Time.realtimeSinceStartup + 6
    };
    public static bool OnLocalPurchase(RequisitionSlot slot)
    {
        var net = CoopRuntime.Net;
        if (IsApplying || net == null || (net.State != SessionState.Hosting && net.State != SessionState.Joined) || slot == null) return true;
        if (_instance == null) return false;
        var request = Describe(slot);
        if (request.Slot.Length == 0 || request.Card.Length == 0) return net.IsHost;
        if (net.IsHost) { _instance._local[slot.Pointer] = request; return true; }
        request.Op = 2; _instance.Send(net.HostSteamId, request);
        CoopRuntime.LogSource?.LogInfo("[Purchase] guest request card=" + request.Card + " slot=" + request.Slot);
        return false;
    }
    public static void AfterLocalPurchase(RequisitionSlot slot)
    {
        if (IsApplying || slot == null || _instance == null || CoopRuntime.Net?.IsHost != true) return;
        if (!_instance._local.TryGetValue(slot.Pointer, out var request)) return;
        _instance._local.Remove(slot.Pointer);
        if (Accepted(slot, request)) _instance.Broadcast(request);
    }
    private static bool Accepted(RequisitionSlot slot, Request request) => slot.lastRedemptionTime != request.Before
        || (RequisitionSlot.RunningPunchcards?.Count ?? 0) > request.RunningBefore;
    private static RequisitionSlot Find(Request request)
    {
        RequisitionSlot match = null;
        foreach (var slot in UnityEngine.Object.FindObjectsOfType<RequisitionSlot>(true))
            if (slot != null && PunchcardSync.SlotKey(slot.itemSlot) == request.Slot && slot.CurrentCard?.CurrentDefinition?.ID == request.Card)
            { if (match != null) throw new InvalidOperationException("Ambiguous requisition station"); match = slot; }
        return match;
    }
    private void Send(ulong to, Request request)
    {
        var writer = NetProtocol.Begin((MsgType)MsgType);
        writer.Put(request.Op); writer.Put(request.Id); writer.Put(request.Scene); writer.Put(request.Slot); writer.Put(request.Card);
        CoopRuntime.Net.SendDirectFragmented(to, NetProtocol.Snapshot(writer), true);
    }
    private void Broadcast(Request request)
    {
        request.Op = 1; _seen.Add(request.Id);
        foreach (var player in CoopRuntime.Net.Roster) if (!player.IsLocal) Send(player.SteamId, request);
        CoopRuntime.LogSource?.LogInfo("[Purchase] accepted exact card=" + request.Card + " slot=" + request.Slot);
    }
    private bool Apply(Request request)
    {
        if (request.Scene != Scene) return true;
        var slot = Find(request); if (slot == null) return false;
        if (!_seen.Add(request.Id)) return true;
        request.Before = slot.lastRedemptionTime; request.RunningBefore = RequisitionSlot.RunningPunchcards?.Count ?? 0;
        IsApplying = true;
        try { slot.AttemptRequisition(); }
        finally { IsApplying = false; }
        if (CoopRuntime.Net.IsHost)
        {
            if (Accepted(slot, request)) Broadcast(request);
            else { request.Op = 3; Send(request.From, request); CoopRuntime.LogSource?.LogWarning("[Purchase] host rejected card=" + request.Card); }
        }
        return true;
    }
    public void OnPacket(ulong from, byte[] data)
    {
        var net = CoopRuntime.Net; if (net == null) return;
        bool member = false; foreach (var peer in net.Roster) if (!peer.IsLocal && (ulong)peer.SteamId == from) member = true;
        if (!member || (!net.IsHost && from != net.HostSteamId)) return;
        try
        {
            var reader = new NetDataReader(data); reader.SkipBytes(NetProtocol.TypeLen);
            var request = new Request { Op = reader.GetByte(), Id = reader.GetString(), Scene = reader.GetString(), Slot = reader.GetString(), Card = reader.GetString(), From = from, Expires = Time.realtimeSinceStartup + 6 };
            if (!Guid.TryParseExact(request.Id, "N", out _) || request.Scene != Scene || request.Slot.Length > 2048 || request.Card.Length > 256 || request.Card.Length == 0) return;
            if (request.Op == 3 && !net.IsHost) { CoopRuntime.LogSource?.LogWarning("[Purchase] request rejected by host: " + request.Card); return; }
            if ((net.IsHost && request.Op != 2) || (!net.IsHost && request.Op != 1) || _seen.Contains(request.Id) || _pending.ContainsKey(request.Id)) return;
            if (_seen.Count >= 8192) throw new InvalidOperationException("Requisition request history full; reconnect required");
            if (!Apply(request) && _pending.Count < 128) _pending[request.Id] = request;
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning("[Purchase] " + ex.Message); }
    }
    public void Tick(float dt)
    {
        _timer += dt; if (_timer < .2f) return; _timer = 0;
        foreach (var request in new List<Request>(_pending.Values))
        {
            try
            {
                if (Apply(request)) _pending.Remove(request.Id);
                else if (Time.realtimeSinceStartup >= request.Expires)
                {
                    _pending.Remove(request.Id);
                    if (CoopRuntime.Net?.IsHost == true) { request.Op = 3; Send(request.From, request); }
                    CoopRuntime.LogSource?.LogWarning("[Purchase] exact card/slot not ready: " + request.Card);
                }
            }
            catch (Exception ex) { _pending.Remove(request.Id); CoopRuntime.LogSource?.LogWarning("[Purchase] " + ex.Message); }
        }
    }
    public void OnSessionStarted() => Reset();
    public void OnSessionEnded() => Reset();
    public void Reset() { IsApplying = false; _seen.Clear(); _pending.Clear(); _local.Clear(); _timer = 0; }
}
