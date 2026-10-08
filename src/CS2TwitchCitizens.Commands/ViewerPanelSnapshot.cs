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
                HasHome = !string.IsNullOrEmpty(b.Home),
                HasWorkplace = !string.IsNullOrEmpty(b.Workplace),
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
        LastKnownWorkplace = life.LastKnownWorkplace,
        LastKnownHome = life.LastKnownHome, CauseOfDeath = life.CauseOfDeath
    };
}

[DataContract]
public sealed class ViewerPanelRow
{
    [DataMember(Name = "twitchUserId")] public string TwitchUserId { get; set; } = string.Empty;
    [DataMember(Name = "login")] public string Login { get; set; } = string.Empty;
    [DataMember(Name = "displayName")] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Name = "citizenName")] public string CitizenName { get; set; } = string.Empty;
    [DataMember(Name = "age")] public string Age { get; set; } = string.Empty;
    [DataMember(Name = "hasHome")] public bool HasHome { get; set; }
    [DataMember(Name = "hasWorkplace")] public bool HasWorkplace { get; set; }
    [DataMember(Name = "isValid")] public bool IsValid { get; set; }
    [DataMember(Name = "positionAvailable")] public bool PositionAvailable { get; set; }
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
    [DataMember(Name = "lastKnownWorkplace")] public string LastKnownWorkplace { get; set; } = string.Empty;
    [DataMember(Name = "lastKnownHome")] public string LastKnownHome { get; set; } = string.Empty;
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
