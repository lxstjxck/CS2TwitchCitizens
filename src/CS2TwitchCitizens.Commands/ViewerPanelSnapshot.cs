using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CS2TwitchCitizens.Commands;

/// <summary>Only these plain values cross the C# to UI boundary.</summary>
[DataContract]
public sealed class ViewerPanelSnapshot
{
    [DataMember(Name = "twitchStatus")] public string TwitchStatus { get; set; } = "Disabled";
    [DataMember(Name = "channelId")] public string ChannelId { get; set; } = string.Empty;
    [DataMember(Name = "gameLoaded")] public bool GameLoaded { get; set; }
    [DataMember(Name = "viewers")] public ViewerPanelRow[] Viewers { get; set; } = Array.Empty<ViewerPanelRow>();

    public string ToJson()
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(ViewerPanelSnapshot)).WriteObject(stream, this);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ViewerPanelSnapshot Create(string status, string channelId, bool gameLoaded,
        IReadOnlyList<ViewerCitizenInfo> bindings)
    {
        var rows = new ViewerPanelRow[bindings.Count];
        for (var i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            rows[i] = new ViewerPanelRow
            {
                TwitchUserId = b.TwitchUserId,
                Login = b.Login,
                DisplayName = b.DisplayName,
                CitizenName = b.CitizenName,
                Age = b.Age,
                AgeDays = b.AgeDays,
                AgeYears = ModAgeModel.FromGameDays(b.AgeDays),
                HomeAddress = b.HomeAddress,
                HomeBuilding = SafeLabel(b.Home),
                CurrentBuilding = SafeLabel(b.CurrentBuilding),
                CurrentAddress = b.CurrentAddress,
                LocationType = b.LocationType,
                MissingReason = b.MissingReason,
                HasHome = !string.IsNullOrEmpty(b.Home),
                HasWorkplace = !string.IsNullOrEmpty(b.Workplace),
                Workplace = SafeLabel(b.Workplace),
                Employment = b.Employment,
                WorkAddress = b.WorkAddress,
                IsValid = b.IsValid,
                PositionAvailable = b.PositionAvailable,
                CurrentLifeId = b.CurrentLifeId,
                TotalLives = b.TotalLives,
                CurrentLifeStatus = b.CurrentLifeStatus,
                CurrentLife = b.CurrentLife == null ? null : ToPanelLife(b.CurrentLife),
                PreviousLives = Array.ConvertAll(b.PreviousLives, ToPanelLife)
            };
        }
        return new ViewerPanelSnapshot { TwitchStatus = status, ChannelId = channelId,
            GameLoaded = gameLoaded, Viewers = rows };
    }

    private static ViewerPanelLife ToPanelLife(ViewerLifeInfo life) => new ViewerPanelLife {
        LifeId = life.LifeId, OriginalCitizenName = life.OriginalCitizenName,
        TwitchDisplayName = life.TwitchDisplayName,
        StartGameDate = life.StartGameDate, EndGameDate = life.EndGameDate,
        Status = life.Status, LastKnownAge = life.LastKnownAge,
        LastKnownAgeDays = life.LastKnownAgeDays,
        LastKnownAgeYears = ModAgeModel.FromGameDays(life.LastKnownAgeDays),
        LastKnownWorkplace = life.LastKnownWorkplace,
        LastKnownHome = SafeLabel(life.LastKnownHome), LastKnownHomeAddress = life.LastKnownHomeAddress,
        MissingReason = life.MissingReason, CauseOfDeath = life.CauseOfDeath
    };

    private static string SafeLabel(string? value) => string.IsNullOrWhiteSpace(value) ||
        value!.StartsWith("Entity(", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
}

[DataContract]
public sealed class ViewerPanelRow
{
    [DataMember(Name = "twitchUserId")] public string TwitchUserId { get; set; } = string.Empty;
    [DataMember(Name = "login")] public string Login { get; set; } = string.Empty;
    [DataMember(Name = "displayName")] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Name = "citizenName")] public string CitizenName { get; set; } = string.Empty;
    [DataMember(Name = "age")] public string Age { get; set; } = string.Empty;
    [DataMember(Name = "ageDays", EmitDefaultValue = false)] public int? AgeDays { get; set; }
    [DataMember(Name = "ageYears", EmitDefaultValue = false)] public int? AgeYears { get; set; }
    [DataMember(Name = "homeAddress")] public string HomeAddress { get; set; } = string.Empty;
    [DataMember(Name = "homeBuilding")] public string HomeBuilding { get; set; } = string.Empty;
    [DataMember(Name = "currentBuilding")] public string CurrentBuilding { get; set; } = string.Empty;
    [DataMember(Name = "currentAddress")] public string CurrentAddress { get; set; } = string.Empty;
    [DataMember(Name = "missingReason")] public string MissingReason { get; set; } = string.Empty;
    [DataMember(Name = "hasHome")] public bool HasHome { get; set; }
    [DataMember(Name = "hasWorkplace")] public bool HasWorkplace { get; set; }
    [DataMember(Name = "workplace")] public string Workplace { get; set; } = string.Empty;
    [DataMember(Name = "employment")] public string Employment { get; set; } = string.Empty;
    [DataMember(Name = "workAddress")] public string WorkAddress { get; set; } = string.Empty;
    [DataMember(Name = "isValid")] public bool IsValid { get; set; }
    [DataMember(Name = "positionAvailable")] public bool PositionAvailable { get; set; }
    [DataMember(Name = "locationType")] public string LocationType { get; set; } = string.Empty;
    [DataMember(Name = "currentLifeId")] public string CurrentLifeId { get; set; } = string.Empty;
    [DataMember(Name = "totalLives")] public int TotalLives { get; set; }
    [DataMember(Name = "currentLifeStatus")] public string CurrentLifeStatus { get; set; } = string.Empty;
    [DataMember(Name = "currentLife")] public ViewerPanelLife? CurrentLife { get; set; }
    [DataMember(Name = "previousLives")] public ViewerPanelLife[] PreviousLives { get; set; } = Array.Empty<ViewerPanelLife>();
}

[DataContract]
public sealed class ViewerPanelLife
{
    [DataMember(Name = "lifeId")] public string LifeId { get; set; } = string.Empty;
    [DataMember(Name = "originalCitizenName")] public string OriginalCitizenName { get; set; } = string.Empty;
    [DataMember(Name = "twitchDisplayName")] public string TwitchDisplayName { get; set; } = string.Empty;
    [DataMember(Name = "startGameDate")] public string StartGameDate { get; set; } = string.Empty;
    [DataMember(Name = "endGameDate")] public string EndGameDate { get; set; } = string.Empty;
    [DataMember(Name = "status")] public string Status { get; set; } = string.Empty;
    [DataMember(Name = "lastKnownAge")] public string LastKnownAge { get; set; } = string.Empty;
    [DataMember(Name = "lastKnownAgeDays", EmitDefaultValue = false)] public int? LastKnownAgeDays { get; set; }
    [DataMember(Name = "lastKnownAgeYears", EmitDefaultValue = false)] public int? LastKnownAgeYears { get; set; }
    [DataMember(Name = "lastKnownWorkplace")] public string LastKnownWorkplace { get; set; } = string.Empty;
    [DataMember(Name = "lastKnownHome")] public string LastKnownHome { get; set; } = string.Empty;
    [DataMember(Name = "lastKnownHomeAddress")] public string LastKnownHomeAddress { get; set; } = string.Empty;
    [DataMember(Name = "missingReason")] public string MissingReason { get; set; } = string.Empty;
    [DataMember(Name = "causeOfDeath")] public string CauseOfDeath { get; set; } = string.Empty;
}

public static class ViewerPanelActions
{
    public static CitizenFocusStatus Focus(string viewerId, Func<string, CitizenFocusStatus> focus)
    {
        if (string.IsNullOrWhiteSpace(viewerId)) return CitizenFocusStatus.CitizenNotFound;
        return focus(viewerId);
    }
}
