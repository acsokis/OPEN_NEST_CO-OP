using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace OpenNestCoop.Core;

// Explicit local-development mode only. The launcher gives each game copy its
// own progression/checkpoint directories; normal Steam launches are unchanged.
internal static class DevelopmentProfile
{
    internal static string Root { get; private set; }
    private static bool _verified;
    private static float _previewCheckAt;
    internal static void Initialize()
    {
        var args = Environment.GetCommandLineArgs();
        int flag = Array.IndexOf(args, "--coop-profile");
        if (flag < 0) return;
        int local = Array.IndexOf(args, "--local");
        if (flag + 1 >= args.Length || local < 0 || local + 1 >= args.Length || (args[local + 1] != "host" && args[local + 1] != "join"))
            throw new InvalidOperationException("A development profile requires --local host/join");
        string game = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string expected = Path.Combine(game, "DevProfile");
        string supplied = Path.GetFullPath(args[flag + 1]).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(expected, supplied, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The development profile must be this game copy's DevProfile directory");
        Directory.CreateDirectory(Path.Combine(supplied, "Live")); Directory.CreateDirectory(Path.Combine(supplied, "Legacy"));
        Directory.CreateDirectory(Path.Combine(supplied, "Checkpoints")); Root = supplied;
        var harmony = new HarmonyLib.Harmony("opennest.local.development.profile");
        harmony.Patch(AccessTools.PropertyGetter(typeof(ProgressionManager), "ProgressionSaveRoot"), prefix: new HarmonyMethod(typeof(DevelopmentProfile), nameof(ProgressionRoot)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(ProgressionManager), "LegacyProgressionSaveRoot"), prefix: new HarmonyMethod(typeof(DevelopmentProfile), nameof(LegacyRoot)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(MissionSaveManager), "SaveRoot"), prefix: new HarmonyMethod(typeof(DevelopmentProfile), nameof(CheckpointRoot)));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 20;
        CoopRuntime.LogSource?.LogInfo("[DevelopmentProfile] isolated profile=" + Root + " role=" + args[local + 1]);
    }
    private static bool ProgressionRoot(ref string __result) { __result = Path.Combine(Root, "Live"); return false; }
    private static bool LegacyRoot(ref string __result) { __result = Path.Combine(Root, "Legacy"); return false; }
    private static bool CheckpointRoot(ref string __result) { __result = Path.Combine(Root, "Checkpoints"); return false; }
    internal static void Verify()
    {
        // Native startup/settings can override Unity's command-line window size
        // and the initial frame cap. Keep only the isolated test copies bounded;
        // never write PlayerPrefs or the user's normal graphics configuration.
        if (Root != null && Time.realtimeSinceStartup >= _previewCheckAt)
        {
            _previewCheckAt = Time.realtimeSinceStartup + 10;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 20;
            if (Screen.fullScreen || Screen.width != 960 || Screen.height != 540)
                Screen.SetResolution(960, 540, false);
            CoopRuntime.LogSource?.LogInfo($"[DevelopmentProfile] preview={Screen.width}x{Screen.height} fullscreen={Screen.fullScreen} cap={Application.targetFrameRate} vsync={QualitySettings.vSyncCount}");
        }
        if (Root == null || _verified || ProgressionManager.Instance == null || MissionSaveManager.Instance == null) return;
        string progression = ProgressionManager.Instance.ProgressionSaveRoot, checkpoints = MissionSaveManager.Instance.SaveRoot;
        if (!progression.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !checkpoints.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Development save path isolation failed");
        _verified = true;
        CoopRuntime.LogSource?.LogInfo("[DevelopmentProfile] VERIFIED progression=" + progression + " checkpoints=" + checkpoints);
    }
}
