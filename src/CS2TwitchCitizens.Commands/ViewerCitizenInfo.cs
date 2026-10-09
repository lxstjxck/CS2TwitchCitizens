namespace CS2TwitchCitizens.Commands;

/// <summary>Safe snapshot for a future UI. TwitchUserId is the action key; no ECS object is exposed.</summary>
public sealed class ViewerCitizenInfo
{
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CitizenName { get; set; } = string.Empty;
    public string OriginalNameDescriptor { get; set; } = string.Empty;
    public string Age { get; set; } = string.Empty;
    public int? AgeDays { get; set; }
    public string Home { get; set; } = string.Empty;
    public string HomeAddress { get; set; } = string.Empty;
    public string Workplace { get; set; } = string.Empty;
    public string WorkAddress { get; set; } = string.Empty;
    public string CurrentBuilding { get; set; } = string.Empty;
    public string CurrentAddress { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public string HouseholdSize { get; set; } = string.Empty;
    public string Employment { get; set; } = string.Empty;
    public string School { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public bool PositionAvailable { get; set; }
    public CitizenPosition? Position { get; set; }
    public string CurrentLifeId { get; set; } = string.Empty;
    public int TotalLives { get; set; }
    public string CurrentLifeStatus { get; set; } = string.Empty;
    public string MissingReason { get; set; } = string.Empty;
    public ViewerLifeInfo? CurrentLife { get; set; }
    public ViewerLifeInfo[] PreviousLives { get; set; } = Array.Empty<ViewerLifeInfo>();
}

public sealed class ViewerLifeInfo
{
    public string LifeId { get; set; } = string.Empty;
    public string OriginalCitizenName { get; set; } = string.Empty;
    public string TwitchDisplayName { get; set; } = string.Empty;
    public string StartGameDate { get; set; } = string.Empty;
    public string EndGameDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string LastKnownAge { get; set; } = string.Empty;
    public int? LastKnownAgeDays { get; set; }
    public string LastKnownWorkplace { get; set; } = string.Empty;
    public string LastKnownHome { get; set; } = string.Empty;
    public string LastKnownHomeAddress { get; set; } = string.Empty;
    public string MissingReason { get; set; } = string.Empty;
    public string CauseOfDeath { get; set; } = string.Empty;
}
