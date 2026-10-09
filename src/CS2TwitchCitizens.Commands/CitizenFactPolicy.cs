using System.Globalization;

namespace CS2TwitchCitizens.Commands;

/// <summary>Formats only facts obtained from the game; never invents an address or age.</summary>
public static class CitizenFactPolicy
{
    public static int? AgeDays(float days) => !float.IsNaN(days) && !float.IsInfinity(days) && days >= 0 && days <= 32767
        ? (int)Math.Floor(days) : null;

    public static string Address(string? streetName, int number) =>
        string.IsNullOrWhiteSpace(streetName) || number <= 0 ? string.Empty :
        streetName!.Trim() + ", " + number.ToString(CultureInfo.InvariantCulture);
}
