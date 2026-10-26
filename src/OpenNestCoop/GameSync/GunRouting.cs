using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenNestCoop.GameSync;

// Native turret.guns order is not a network identity. Both peers derive wire
// indices from the same hierarchy keys, without rearranging the native list.
internal static class GunRouting
{
    private static IntPtr _turret;
    private static IntPtr[] _native = Array.Empty<IntPtr>();
    private static IReadOnlyList<GunController> _ordered = Array.Empty<GunController>();
    internal static string Key(GunController gun)
    {
        string key = "";
        for (var transform = gun?.transform; transform != null; transform = transform.parent)
            key = transform.name + (key.Length == 0 ? "" : "/" + key);
        return key;
    }
    internal static IReadOnlyList<GunController> Guns
    {
        get
        {
            var turret = TurretController.Instance;
            if (turret == null || turret.guns == null) { _turret = IntPtr.Zero; _native = Array.Empty<IntPtr>(); return _ordered = Array.Empty<GunController>(); }
            bool same = turret.Pointer == _turret && turret.guns.Count == _native.Length;
            if (same)
                for (int i = 0; i < _native.Length; i++)
                    if ((turret.guns[i]?.Pointer ?? IntPtr.Zero) != _native[i]) { same = false; break; }
            if (same) return _ordered;
            var native = new IntPtr[turret.guns.Count]; var byKey = new Dictionary<string, GunController>(StringComparer.Ordinal);
            for (int i = 0; i < native.Length; i++)
            {
                var gun = turret.guns[i]; native[i] = gun?.Pointer ?? IntPtr.Zero;
                if (gun == null) continue;
                string key = Key(gun);
                if (string.IsNullOrWhiteSpace(key) || byKey.ContainsKey(key)) throw new InvalidOperationException("Ambiguous gun hierarchy: " + key);
                byKey.Add(key, gun);
            }
            string[] keys = GunKeyOrder.Canonical(byKey.Keys);
            _ordered = keys.Select(key => byKey[key]).ToArray(); _native = native; _turret = turret.Pointer;
            Core.CoopRuntime.LogSource?.LogInfo("[GunRouting] canonical wire order: " + string.Join(" | ", keys));
            return _ordered;
        }
    }
    internal static int IndexOf(GunController gun)
    {
        if (gun == null) return -1;
        var guns = Guns;
        for (int i = 0; i < guns.Count; i++) if (guns[i].Pointer == gun.Pointer) return i;
        return -1;
    }
}
