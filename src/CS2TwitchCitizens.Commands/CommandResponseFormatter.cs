using System.Globalization;

namespace CS2TwitchCitizens.Commands;

/// <summary>Formats game-thread snapshots only; no ECS or network access.</summary>
public static class CommandResponseFormatter
{
    public static string Format(string verb, ViewerCitizenInfo? info, CommandSettings settings)
    {
        var options = settings.For(verb);
        var ru = settings.Language == "ru";
        if (info == null || info.TotalLives == 0)
            return Limit(ru ? "Сначала напишите !join." : "Use !join first.", options.MaxResponseLength);
        var fields = new List<string>();
        var locationWritten = false;
        foreach (var id in options.SelectedFields)
        {
            if (id == "locationType" || id == "currentBuilding")
            {
                if (locationWritten) continue;
                locationWritten = true;
                var location = Location(info, ru);
                if (location.Length == 0 && settings.MissingData == "omit") continue;
                fields.Add((ru ? "местоположение" : "location") + ": " +
                    (location.Length == 0 ? (ru ? "неизвестно" : "unknown") : location));
                continue;
            }
            var value = Value(id, info, options, ru);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (settings.MissingData == "omit") continue;
                value = ru ? "нет данных" : "no data";
            }
            fields.Add(Label(id, ru) + ": " + value);
        }
        var prefix = "@" + (string.IsNullOrWhiteSpace(info.DisplayName) ? info.Login : info.DisplayName);
        var result = prefix + (fields.Count == 0 ? "" : ", " + string.Join(". ", fields) + ".");
        if (verb == "!history" && result.Length > options.MaxResponseLength &&
            options.SelectedFields.Contains("previousLives"))
        {
            var shorter = options.Copy();
            while (shorter.PreviousLivesLimit > 0 && result.Length > options.MaxResponseLength)
            {
                var last = fields.FindLastIndex(f => f.StartsWith(Label("previousLives", ru) + ":", StringComparison.Ordinal));
                if (last < 0) break;
                shorter.PreviousLivesLimit--;
                var value = Value("previousLives", info, shorter, ru);
                if (string.IsNullOrEmpty(value)) fields.RemoveAt(last);
                else fields[last] = Label("previousLives", ru) + ": " + value;
                result = prefix + (fields.Count == 0 ? "" : ", " + string.Join(". ", fields) + ".");
            }
        }
        return Limit(result, options.MaxResponseLength);
    }

    private static string Value(string id, ViewerCitizenInfo info, CommandOptions options, bool ru)
    {
        if (!info.IsValid && (id == "name" || id == "ageGroup" || id == "home" || id == "work" ||
            id == "currentBuilding" || id == "locationType" || id == "coordinates" ||
            id == "householdSize" || id == "employment" || id == "school")) return "";
        switch (id)
        {
            case "name": return info.CitizenName;
            case "ageGroup":
                var ageGroup = TranslateAge(info.Age, ru);
                var displayedAge = ModAgeModel.Format(info.AgeDays, ru);
                return displayedAge.Length > 0 ? displayedAge : ageGroup;
            case "status": return TranslateStatus(info.CurrentLifeStatus, ru);
            case "home": return info.HomeAddress;
            case "householdSize": return info.HouseholdSize;
            case "employment": return info.Employment == "yes" ? (ru ? "работает" : "employed") :
                info.Employment == "student" ? (ru ? "учится" : "student") : "";
            case "work": return info.Workplace.Length > 0
                ? info.Workplace + (info.WorkAddress.Length > 0 ? ", " + info.WorkAddress : "")
                : info.Employment == "yes" ? (ru ? "работает" : "employed") :
                    info.Employment == "student" ? (ru ? "учится" : "student") : "";
            case "school": return info.School;
            case "currentBuilding": return info.CurrentBuilding.Length == 0 ? info.CurrentAddress :
                info.CurrentAddress.Length == 0 ? info.CurrentBuilding :
                info.CurrentBuilding + ", " + info.CurrentAddress;
            case "locationType": return info.LocationType switch {
                "home" => ru ? "дома" : "home", "work" => ru ? "на работе" : "at work",
                "building" => ru ? "в здании" : "in a building",
                "transport" => ru ? "в транспорте" : "in transport", _ => "" };
            case "coordinates": return info.PositionAvailable && info.Position.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0}, {2:0}", info.Position.Value.X, info.Position.Value.Y, info.Position.Value.Z) : "";
            case "totalLives": return info.TotalLives.ToString(CultureInfo.InvariantCulture);
            case "currentLife": return info.TotalLives.ToString(CultureInfo.InvariantCulture);
            case "startDate": return ShortDate(info.CurrentLife?.StartGameDate);
            case "endDate": return ShortDate(info.CurrentLife?.EndGameDate);
            case "causeOfDeath": return info.CurrentLife?.CauseOfDeath ?? "";
            case "previousLives":
                var previous = info.PreviousLives.Select((life, i) =>
                    "#" + (i + 1).ToString(CultureInfo.InvariantCulture) + " " + TranslateStatus(life.Status, ru))
                    .Skip(Math.Max(0, info.PreviousLives.Length - options.PreviousLivesLimit));
                return string.Join(", ", previous);
            default: return "";
        }
    }
    private static string Location(ViewerCitizenInfo info, bool ru)
    {
        if (!info.IsValid) return "";
        switch (info.LocationType)
        {
            case "home": return (ru ? "дома" : "at home") +
                (info.HomeAddress.Length > 0 ? " — " + info.HomeAddress : "");
            case "work": return (ru ? "на работе" : "at work") +
                (info.Workplace.Length > 0 ? " — " + info.Workplace : "") +
                (info.CurrentAddress.Length > 0 ? ", " + info.CurrentAddress : "");
            case "building": return string.Join(" — ", new[] { info.CurrentBuilding, info.CurrentAddress }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            case "transport": return ru ? "в транспорте" : "in transport";
            default: return "";
        }
    }
    private static string ShortDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
    private static string TranslateAge(string value, bool ru) => value switch {
        "Child" => ru ? "ребёнок" : "child", "Teen" => ru ? "подросток" : "teen",
        "Adult" => ru ? "взрослый" : "adult", "Elderly" => ru ? "пожилой" : "elderly", _ => "" };
    private static string TranslateStatus(string value, bool ru) => value switch {
        "Active" => ru ? "жив" : "alive", "Deceased" => ru ? "умер" : "deceased",
        "Missing" => ru ? "отсутствует" : "missing", "Unbound" => ru ? "отвязан" : "unbound", _ => "" };
    private static string Label(string id, bool ru) => id switch {
        "name" => ru ? "житель" : "citizen", "ageGroup" => ru ? "возраст" : "age",
        "status" => ru ? "статус" : "status", "home" => ru ? "дом" : "home",
        "householdSize" => ru ? "членов семьи" : "household members",
        "employment" => ru ? "занятость" : "employment", "school" => ru ? "учёба" : "school",
        "work" => ru ? "работа" : "work", "currentBuilding" => ru ? "здание" : "building",
        "locationType" => ru ? "место" : "location", "coordinates" => ru ? "координаты" : "coordinates",
        "totalLives" => ru ? "жизней" : "lives", "currentLife" => ru ? "текущая жизнь" : "current life",
        "startDate" => ru ? "начало" : "start", "endDate" => ru ? "конец" : "end",
        "causeOfDeath" => ru ? "причина смерти" : "cause of death",
        "previousLives" => ru ? "прошлые жизни" : "previous lives", _ => id };
    public static string Limit(string value, int max) => value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 1)).TrimEnd() + "…";
}
