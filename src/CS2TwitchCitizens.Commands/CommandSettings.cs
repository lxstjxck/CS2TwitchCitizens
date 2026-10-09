using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CS2TwitchCitizens.Commands;

[DataContract]
public sealed class CommandOptions
{
    [DataMember(Name = "enabled")] public bool Enabled { get; set; } = true;
    [DataMember(Name = "viewerCooldownSeconds")] public int ViewerCooldownSeconds { get; set; } = 15;
    [DataMember(Name = "responseEnabled")] public bool ResponseEnabled { get; set; } = true;
    [DataMember(Name = "permission")] public string Permission { get; set; } = "everyone";
    [DataMember(Name = "selectedFields")] public string[] SelectedFields { get; set; } = Array.Empty<string>();
    [DataMember(Name = "maxResponseLength")] public int MaxResponseLength { get; set; } = 450;
    [DataMember(Name = "previousLivesLimit")] public int PreviousLivesLimit { get; set; } = 3;
    [DataMember(Name = "alreadyJoinedResponse")] public bool AlreadyJoinedResponse { get; set; } = true;

    public CommandOptions Copy() => new CommandOptions { Enabled = Enabled,
        ViewerCooldownSeconds = ViewerCooldownSeconds, ResponseEnabled = ResponseEnabled,
        Permission = Permission, SelectedFields = (string[])SelectedFields.Clone(),
        MaxResponseLength = MaxResponseLength, PreviousLivesLimit = PreviousLivesLimit,
        AlreadyJoinedResponse = AlreadyJoinedResponse };
}

[DataContract]
public sealed class CommandSettings
{
    public const int CurrentVersion = 1;
    [DataMember(Name = "version")] public int Version { get; set; } = CurrentVersion;
    [DataMember(Name = "responsesEnabled")] public bool ResponsesEnabled { get; set; } = true;
    [DataMember(Name = "language")] public string Language { get; set; } = "ru";
    [DataMember(Name = "missingData")] public string MissingData { get; set; } = "omit";
    [DataMember(Name = "sharedCooldownSeconds")] public int SharedCooldownSeconds { get; set; }
    [DataMember(Name = "outgoingIntervalSeconds")] public int OutgoingIntervalSeconds { get; set; } = 2;
    [DataMember(Name = "maxOutgoingQueue")] public int MaxOutgoingQueue { get; set; } = 50;
    [DataMember(Name = "join")] public CommandOptions Join { get; set; } = new CommandOptions { ViewerCooldownSeconds = 30 };
    [DataMember(Name = "me")] public CommandOptions Me { get; set; } = new CommandOptions { SelectedFields = new[] { "name", "ageGroup", "status", "home", "work" } };
    [DataMember(Name = "find")] public CommandOptions Find { get; set; } = new CommandOptions { SelectedFields = new[] { "locationType", "currentBuilding" } };
    [DataMember(Name = "history")] public CommandOptions History { get; set; } = new CommandOptions { ViewerCooldownSeconds = 30,
        SelectedFields = new[] { "totalLives", "currentLife", "status", "previousLives" } };

    public CommandOptions For(string verb) => verb switch {
        "!join" => Join, "!me" => Me, "!find" => Find, "!history" => History,
        _ => throw new ArgumentOutOfRangeException(nameof(verb)) };
    public CommandSettings Copy() => new CommandSettings { Version = Version,
        ResponsesEnabled = ResponsesEnabled, Language = Language, MissingData = MissingData,
        SharedCooldownSeconds = SharedCooldownSeconds, OutgoingIntervalSeconds = OutgoingIntervalSeconds,
        MaxOutgoingQueue = MaxOutgoingQueue, Join = Join.Copy(), Me = Me.Copy(), Find = Find.Copy(), History = History.Copy() };
    public static CommandSettings Defaults() => new CommandSettings();

    public void Normalize()
    {
        Version = CurrentVersion;
        Language = Language == "en" ? "en" : "ru";
        MissingData = MissingData == "label" ? "label" : "omit";
        SharedCooldownSeconds = Clamp(SharedCooldownSeconds, 0, 3600);
        OutgoingIntervalSeconds = Clamp(OutgoingIntervalSeconds, 1, 60);
        MaxOutgoingQueue = Clamp(MaxOutgoingQueue, 1, 200);
        Join = NormalizeCommand(Join, Defaults().Join, Array.Empty<string>());
        Me = NormalizeCommand(Me, Defaults().Me, CitizenFieldCatalog.MeIds);
        Find = NormalizeCommand(Find, Defaults().Find, CitizenFieldCatalog.FindIds);
        History = NormalizeCommand(History, Defaults().History, CitizenFieldCatalog.HistoryIds);
    }
    private static CommandOptions NormalizeCommand(CommandOptions? value, CommandOptions fallback, string[] allowed)
    {
        if (value == null) return fallback;
        value.ViewerCooldownSeconds = Clamp(value.ViewerCooldownSeconds, 0, 3600);
        value.MaxResponseLength = Clamp(value.MaxResponseLength, 1, 500);
        value.PreviousLivesLimit = Clamp(value.PreviousLivesLimit, 0, 10);
        if (value.Permission != "moderators" && value.Permission != "broadcaster") value.Permission = "everyone";
        value.SelectedFields = (value.SelectedFields ?? fallback.SelectedFields)
            .Where(allowed.Contains).Distinct().ToArray();
        return value;
    }
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    public string ToJson()
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(CommandSettings)).WriteObject(stream, this);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static CommandSettings FromJson(string json)
    {
        if (json.Length > 65536) throw new InvalidDataException("Command settings too large.");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = (CommandSettings?)new DataContractJsonSerializer(typeof(CommandSettings)).ReadObject(stream)
            ?? throw new InvalidDataException("Empty command settings.");
        if (result.Version != CurrentVersion) throw new InvalidDataException("Unsupported command settings version.");
        result.Normalize();
        return result;
    }
}

public static class CitizenFieldCatalog
{
    public static readonly string[] MeIds = { "name", "ageGroup", "status", "householdSize", "home", "employment", "work", "school", "currentBuilding" };
    public static readonly string[] FindIds = { "locationType", "currentBuilding", "coordinates" };
    public static readonly string[] HistoryIds = { "totalLives", "currentLife", "status", "startDate", "endDate", "causeOfDeath", "previousLives" };
}

public sealed class CommandSettingsStore
{
    private readonly string _path;
    public CommandSettingsStore(string path) { _path = path; }
    public bool LoadFailed { get; private set; }
    public CommandSettings Load()
    {
        try { LoadFailed = false; return File.Exists(_path) ? CommandSettings.FromJson(File.ReadAllText(_path, Encoding.UTF8)) : CommandSettings.Defaults(); }
        catch { LoadFailed = true; return CommandSettings.Defaults(); }
    }
    public void Save(CommandSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try {
            File.WriteAllText(temporary, settings.ToJson(), new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        LoadFailed = false;
    }
}

public sealed class CommandSettingsController
{
    private readonly CommandSettingsStore _store;
    private CommandSettings _settings;
    public CommandSettingsController(CommandSettingsStore store) { _store = store; _settings = store.Load(); }
    public bool LoadFailed => _store.LoadFailed;
    public CommandSettings Current => System.Threading.Volatile.Read(ref _settings).Copy();
    public void Update(string json)
    {
        var next = CommandSettings.FromJson(json);
        _store.Save(next);
        System.Threading.Volatile.Write(ref _settings, next);
    }
}
