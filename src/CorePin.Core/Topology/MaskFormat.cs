using System.Globalization;

namespace CorePin.Core.Topology;

internal static class MaskFormat
{
    public static string Hex(ulong value)
        => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);
}
