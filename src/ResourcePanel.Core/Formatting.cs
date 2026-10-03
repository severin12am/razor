using System.Globalization;

namespace ResourcePanel;

public static class Formatting
{
    public static string Percent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "—";
        return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    public static string Bytes(long bytes)
    {
        if (bytes < 0)
            bytes = 0;
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Rate(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond))
            return "—";
        if (bytesPerSecond < 0)
            bytesPerSecond = 0;
        return Bytes((long)bytesPerSecond) + "/s";
    }

    public static string Watts(double? watts)
    {
        if (watts is null || double.IsNaN(watts.Value) || watts.Value < 0.05)
            return "—";
        return watts.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W";
    }

    public static string RamPair(ulong used, ulong total) =>
        Bytes((long)Math.Min(used, long.MaxValue)) + " / " + Bytes((long)Math.Min(total, long.MaxValue));
}
