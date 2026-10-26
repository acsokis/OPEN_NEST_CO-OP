using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNestCoop.GameSync;

internal static class GunKeyOrder
{
    internal static string[] Canonical(IEnumerable<string> keys)
    {
        string[] ordered = keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (ordered.Any(string.IsNullOrWhiteSpace) || ordered.Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidOperationException("Gun identities must be nonempty and unique");
        return ordered;
    }
}
