using System.Globalization;

namespace CS2TwitchCitizens.Commands;

/// <summary>Presentation-only biological age model. Saved ages remain game days.</summary>
public static class ModAgeModel
{
    public const int GameDaysPerDisplayedYear = 1;

    public static int? FromGameDays(int? gameDays) => gameDays is >= 0
        ? gameDays.Value / GameDaysPerDisplayedYear : null;

    public static string Format(int? gameDays, bool russian)
    {
        var years = FromGameDays(gameDays);
        if (!years.HasValue) return string.Empty;
        var value = years.Value;
        if (!russian) return value.ToString(CultureInfo.InvariantCulture) +
            (value == 1 ? " year old" : " years old");
        var lastTwo = value % 100;
        var last = value % 10;
        var noun = lastTwo >= 11 && lastTwo <= 14 ? "лет" :
            last == 1 ? "год" : last >= 2 && last <= 4 ? "года" : "лет";
        return value.ToString(CultureInfo.InvariantCulture) + " " + noun;
    }
}
