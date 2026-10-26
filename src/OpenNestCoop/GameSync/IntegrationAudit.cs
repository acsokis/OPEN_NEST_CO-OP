using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;
#if MELONLOADER
using SleepyNodes = Il2CppSleepyNodes;
#endif

namespace OpenNestCoop.GameSync;

// Opt-in, read-only runtime inspection. Never spawns a plane, purchases a shell,
// enters a mission, or changes native graph state.
internal static class IntegrationAudit
{
    private static readonly bool Enabled = Environment.GetCommandLineArgs().Contains("--coop-audit");
    private static readonly HashSet<string> Seen = new();
    private static float _next;
    internal static void Tick()
    {
        if (!Enabled || Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 15;
        try
        {
            var mission = MissionManager.Instance?.CurrentMission;
            if (mission != null)
            {
                string identity = "mission=" + mission.MissionID + " scene=" + MissionManager.Instance.CurrentMissionSceneName;
                foreach (var loader in UnityEngine.Object.FindObjectsOfType<MapImageLoader>(true))
                {
                    if (loader == null) continue;
                    var sprite = loader.Image_Map?.sprite;
                    string line = identity + " image=" + loader.ImageType + " path=" + loader.resourcePath
                        + " sprite=" + (sprite == null ? "null" : sprite.name);
                    if (Seen.Add(line)) Core.CoopRuntime.LogSource?.LogInfo("[IntegrationAudit] " + line);
                }
            }
            var nodes = Resources.FindObjectsOfTypeAll<SleepyNodes.State_SpawnScoutPlane>();
            foreach (var node in nodes)
            {
                var prefab = node?.PlanePrefab;
                if (prefab == null || !Seen.Add("plane:" + prefab.name)) continue;
                var parts = new List<string>();
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                    if (component != null) parts.Add(component.gameObject.name + "=" + component.GetIl2CppType().FullName);
                Core.CoopRuntime.LogSource?.LogInfo("[IntegrationAudit] scout prefab=" + prefab.name + " components=" + string.Join(";", parts));
            }
            foreach (var pool in Resources.FindObjectsOfTypeAll<ShellSlotPool>())
            {
                if (pool == null || pool.selectors == null) continue;
                var lanes = new List<string>();
                foreach (var selector in pool.selectors)
                    if (selector != null) lanes.Add(selector.name + ":side=" + selector.ShellSlotSide + ":gun=" + selector.artilleryReloadController?.gunController?.name);
                string line = "shell pool=" + pool.name + " selectors=" + string.Join(";", lanes);
                if (Seen.Add(line)) Core.CoopRuntime.LogSource?.LogInfo("[IntegrationAudit] " + line);
            }
            Core.CoopRuntime.LogSource?.LogInfo("[IntegrationAudit] read-only scan scoutNodes=" + nodes.Length);
        }
        catch (Exception ex) { Core.CoopRuntime.LogSource?.LogWarning("[IntegrationAudit] " + ex.Message); }
    }
}
