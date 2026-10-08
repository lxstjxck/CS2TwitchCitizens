namespace CS2TwitchCitizens.Commands;

/// <summary>Game-independent decision model; the game adapter supplies verified ECS facts.</summary>
public enum CitizenRejection
{
    None, InvalidCitizen, Claimed, Dead, Visitor, MovingAway,
    HouseholdUnavailable, HomeUnavailable, InvalidReference,
    OutsideConnection, PositionUnavailable
}

public readonly struct CitizenEligibilityFacts
{
    public CitizenEligibilityFacts(bool validCitizen, bool claimed, bool dead, bool visitor,
        bool movingAway, bool validHousehold, bool validResidentialHome,
        bool validOptionalReferences, bool outsideConnection, bool positionAvailable,
        bool adult)
    {
        ValidCitizen = validCitizen;
        Claimed = claimed;
        Dead = dead;
        Visitor = visitor;
        MovingAway = movingAway;
        ValidHousehold = validHousehold;
        ValidResidentialHome = validResidentialHome;
        ValidOptionalReferences = validOptionalReferences;
        OutsideConnection = outsideConnection;
        PositionAvailable = positionAvailable;
        Adult = adult;
    }

    public bool ValidCitizen { get; }
    public bool Claimed { get; }
    public bool Dead { get; }
    public bool Visitor { get; }
    public bool MovingAway { get; }
    public bool ValidHousehold { get; }
    public bool ValidResidentialHome { get; }
    public bool ValidOptionalReferences { get; }
    public bool OutsideConnection { get; }
    public bool PositionAvailable { get; }
    public bool Adult { get; }
}

public static class CitizenEligibilityPolicy
{
    public static CitizenRejection Check(in CitizenEligibilityFacts facts)
    {
        if (!facts.ValidCitizen) return CitizenRejection.InvalidCitizen;
        if (facts.Claimed) return CitizenRejection.Claimed;
        if (facts.Dead) return CitizenRejection.Dead;
        if (facts.Visitor) return CitizenRejection.Visitor;
        if (facts.MovingAway) return CitizenRejection.MovingAway;
        if (!facts.ValidHousehold) return CitizenRejection.HouseholdUnavailable;
        if (!facts.ValidResidentialHome) return CitizenRejection.HomeUnavailable;
        if (!facts.ValidOptionalReferences) return CitizenRejection.InvalidReference;
        if (facts.OutsideConnection) return CitizenRejection.OutsideConnection;
        if (!facts.PositionAvailable) return CitizenRejection.PositionUnavailable;
        return CitizenRejection.None;
    }
}
