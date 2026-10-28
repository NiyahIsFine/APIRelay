using System.Globalization;
namespace APIRelay;

internal sealed partial class RelayEngine
{
    internal static bool TryOptionalPositive(string? text, out long? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) return false;
        value = parsed;
        return true;
    }
}
