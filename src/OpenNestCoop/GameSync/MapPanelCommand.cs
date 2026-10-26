using System;

namespace OpenNestCoop.GameSync;

internal sealed class MapPanelCommand
{
    internal string Action, Product, Gear;
    internal int Side = -1;
    internal static bool TryParse(string wire, out MapPanelCommand command)
    {
        command = null;
        if (wire == null || wire.Length > 128) return false;
        string[] parts = wire.Split('|');
        if (parts.Length < 3 || parts[0] != "map") return false;
        var result = new MapPanelCommand { Action = parts[1], Product = parts[parts.Length - 1] };
        if (result.Product.Length == 0 || result.Product.Length > 40) return false;
        foreach (char c in result.Product)
            if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '+') return false;
        if (result.Action == "select") { if (parts.Length != 3) return false; }
        else if (result.Action == "detent")
        {
            if (parts.Length != 4 || (parts[2] != "L1" && parts[2] != "L2" && parts[2] != "R1" && parts[2] != "R2" && parts[2] != "N")) return false;
            result.Gear = parts[2]; result.Side = parts[2] == "N" ? -1 : (parts[2][0] == 'L' ? 0 : 1);
        }
        else if (result.Action == "acquire" || result.Action == "external")
        {
            if (parts.Length != 4 || (parts[2] != "0" && parts[2] != "1")) return false;
            result.Side = parts[2] == "0" ? 0 : 1;
        }
        else return false;
        command = result; return true;
    }
}
