using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Globalization;

namespace CS2TwitchCitizens.Commands;

[DataContract]
public sealed class RedemptionEvent
{
    [DataMember(Name = "redemptionId")] public string RedemptionId { get; set; } = "";
    [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
    [DataMember(Name = "broadcasterId")] public string BroadcasterId { get; set; } = "";
    [DataMember(Name = "twitchUserId")] public string TwitchUserId { get; set; } = "";
    [DataMember(Name = "displayName")] public string DisplayName { get; set; } = "";
    [DataMember(Name = "rewardIconUrl")] public string RewardIconUrl { get; set; } = "";
    [DataMember(Name = "userInput")] public string UserInput { get; set; } = "";
    [DataMember(Name = "status")] public string Status { get; set; } = "";
    [DataMember(Name = "isUpdate")] public bool IsUpdate { get; set; }
    [DataMember(Name = "receivedAt")] public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class RedemptionQueue
{
    private readonly ConcurrentQueue<RedemptionEvent> _items = new();
    private readonly int _capacity;
    private int _count;
    private int _rejected;
    public int Count => Volatile.Read(ref _count);
    public int Rejected => Volatile.Read(ref _rejected);
    public RedemptionQueue(int capacity = 64) => _capacity = Math.Max(1, Math.Min(512, capacity));
    public bool TryEnqueue(RedemptionEvent value)
    {
        if (Interlocked.Increment(ref _count) > _capacity)
        {
            Interlocked.Decrement(ref _count);
            Interlocked.Increment(ref _rejected);
            return false;
        }
        _items.Enqueue(value);
        return true;
    }
    public bool TryDequeue(out RedemptionEvent? value)
    {
        if (!_items.TryDequeue(out value)) return false;
        Interlocked.Decrement(ref _count);
        return true;
    }
    public void Clear() { while (TryDequeue(out _)) { } }
}

public static class InteractiveAction
{
    public const string ShowMessage = "ShowMessage";
    public const string CityAnnouncement = "CityAnnouncement";
    public const string CityCelebration = "CityCelebration";
    public const string EmergencyAlert = "EmergencyAlert";
    public const string GameDisaster = "GameDisaster";
    public static bool IsSupported(string? value) => value is ShowMessage or CityAnnouncement or CityCelebration or EmergencyAlert or GameDisaster;
}

public static class InteractivePolicy
{
    public static string Check(InteractiveSettings settings, RewardRule? rule, RedemptionEvent redemption,
        DateTimeOffset? lastShown, DateTimeOffset now, out string text)
    {
        text = rule == null ? "" : Clean(redemption.UserInput, rule.MaxTextLength);
        if (!settings.Enabled) return "InteractiveDisabled";
        if (rule == null || !rule.Enabled) return "UnknownOrDisabledReward";
        if (rule.Action == InteractiveAction.GameDisaster) return "DisasterUnavailable";
        if (redemption.Status != "unfulfilled" && redemption.Status != "fulfilled") return "CanceledOrCompleted";
        if (text.Length == 0) return "EmptyMessage";
        if (lastShown.HasValue && now - lastShown.Value < TimeSpan.FromSeconds(rule.CooldownSeconds)) return "Cooldown";
        return "Ready";
    }
    public static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var chars = value.Where(c => (!char.IsControl(c) || c == '\n') &&
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray();
        var clean = new string(chars).Trim();
        if (clean.Length <= max) return clean;
        var length = max;
        if (length > 0 && char.IsHighSurrogate(clean[length - 1])) length--;
        return clean.Substring(0, length).TrimEnd();
    }
}

[DataContract]
public sealed class RewardRule
{
    [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
    [DataMember(Name = "action")] public string Action { get; set; } = InteractiveAction.ShowMessage;
    [DataMember(Name = "enabled")] public bool Enabled { get; set; } = true;
    [DataMember(Name = "maxTextLength")] public int MaxTextLength { get; set; } = 180;
    [DataMember(Name = "cooldownSeconds")] public int CooldownSeconds { get; set; } = 15;
    [DataMember(Name = "displaySeconds")] public int DisplaySeconds { get; set; } = 8;
}

[DataContract]
public sealed class InteractiveSettings
{
    [DataMember(Name = "version")] public int Version { get; set; } = 1;
    [DataMember(Name = "enabled")] public bool Enabled { get; set; } = false;
    [DataMember(Name = "destructiveEnabled")] public bool DestructiveEnabled { get; set; } = false;
    [DataMember(Name = "rewards")] public List<RewardRule> Rewards { get; set; } = new();
    public static InteractiveSettings Defaults() => new();
    public string ToJson() => JsonData.Write(this);
    public static InteractiveSettings FromJson(string json) => JsonData.Read<InteractiveSettings>(json);
    public void Validate()
    {
        if (Version != 1 || Rewards == null || Rewards.Count > 32) throw new InvalidDataException("Invalid interactive settings.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Rewards)
        {
            if (rule == null || rule.RewardId.Length > 128 || string.IsNullOrWhiteSpace(rule.RewardId) ||
                !ids.Add(rule.RewardId) || !InteractiveAction.IsSupported(rule.Action) ||
                rule.MaxTextLength is < 1 or > 300 || rule.CooldownSeconds is < 0 or > 3600 ||
                rule.DisplaySeconds is < 2 or > 60)
                throw new InvalidDataException("Invalid reward rule.");
        }
    }
}

public sealed class RewardActionRegistry
{
    private readonly string _path;
    private readonly object _gate = new();
    private InteractiveSettings _current;
    public bool LoadFailed { get; private set; }
    public RewardActionRegistry(string path)
    {
        _path = path;
        try
        {
            _current = File.Exists(path) ? InteractiveSettings.FromJson(File.ReadAllText(path)) : InteractiveSettings.Defaults();
            _current.Validate();
        }
        catch { _current = InteractiveSettings.Defaults(); LoadFailed = true; }
    }
    public InteractiveSettings Current { get { lock (_gate) return Clone(_current); } }
    public RewardRule? Find(string rewardId)
    {
        lock (_gate) return _current.Rewards.FirstOrDefault(r => r.Enabled && r.RewardId == rewardId) is { } rule
            ? Clone(rule) : null;
    }
    public void Update(string json)
    {
        if (json.Length > 32768) throw new InvalidDataException("Settings too large.");
        var settings = InteractiveSettings.FromJson(json);
        settings.Validate();
        lock (_gate)
        {
            AtomicJson.Write(_path, settings);
            _current = settings;
            LoadFailed = false;
        }
    }
    private static T Clone<T>(T value) => JsonData.Read<T>(JsonData.Write(value));
}

public static class AtomicJson
{
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonData.Write(value));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class JsonData
{
    public static string Write<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static T Read<T>(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T?)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)
            ?? throw new InvalidDataException("Empty JSON object.");
    }
}

public sealed class RedemptionLedger
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _states;
    public bool LoadFailed { get; }
    public RedemptionLedger(string path)
    {
        _path = path;
        try { _states = File.Exists(path)
            ? JsonData.Read<Dictionary<string, string>>(File.ReadAllText(path))
            : new(); }
        catch { _states = new(); LoadFailed = true; }
    }
    public bool TryReserve(string broadcasterId, string redemptionId)
    {
        if (LoadFailed || string.IsNullOrWhiteSpace(broadcasterId) || string.IsNullOrWhiteSpace(redemptionId)) return false;
        lock (_gate)
        {
            var key = broadcasterId + ":" + redemptionId;
            if (_states.ContainsKey(key)) return false;
            _states.Add(key, "Received");
            try { AtomicJson.Write(_path, _states); }
            catch { _states.Remove(key); throw; }
            return true;
        }
    }
    public void Finish(string broadcasterId, string redemptionId, string result)
    {
        lock (_gate)
        {
            var key = broadcasterId + ":" + redemptionId;
            if (!_states.ContainsKey(key)) return;
            _states[key] = result;
            AtomicJson.Write(_path, _states);
        }
    }
    public void MarkExternalStatus(string broadcasterId, string redemptionId, string status)
    {
        if (LoadFailed || string.IsNullOrWhiteSpace(broadcasterId) || string.IsNullOrWhiteSpace(redemptionId)) return;
        lock (_gate)
        {
            _states[broadcasterId + ":" + redemptionId] = status;
            AtomicJson.Write(_path, _states);
        }
    }
    public string? Get(string broadcasterId, string redemptionId)
    {
        lock (_gate) return _states.TryGetValue(broadcasterId + ":" + redemptionId, out var result) ? result : null;
    }
}

[DataContract]
public sealed class CityEventRecord
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "type")] public string Type { get; set; } = "";
    [DataMember(Name = "twitchUserId")] public string TwitchUserId { get; set; } = "";
    [DataMember(Name = "broadcasterId")] public string BroadcasterId { get; set; } = "";
    [DataMember(Name = "displayName")] public string DisplayName { get; set; } = "";
    [DataMember(Name = "rewardIconUrl")] public string RewardIconUrl { get; set; } = "";
    [DataMember(Name = "redemptionId")] public string RedemptionId { get; set; } = "";
    [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
    [DataMember(Name = "state")] public string State { get; set; } = "";
    [DataMember(Name = "receivedAt")] public string ReceivedAt { get; set; } = "";
    [DataMember(Name = "text")] public string Text { get; set; } = "";
    [DataMember(Name = "citizenName")] public string CitizenName { get; set; } = "";
    [DataMember(Name = "canFindCitizen")] public bool CanFindCitizen { get; set; }
    [DataMember(Name = "result")] public string Result { get; set; } = "";
    [DataMember(Name = "localTest")] public bool LocalTest { get; set; }
    [DataMember(Name = "displaySeconds")] public int DisplaySeconds { get; set; } = 8;
}

[DataContract]
public sealed class CityEventSnapshot
{
    [DataMember(Name = "events")] public List<CityEventRecord> Events { get; set; } = new();
    [DataMember(Name = "active")] public CityEventRecord? Active { get; set; }
    [DataMember(Name = "queued")] public int Queued { get; set; }
    [DataMember(Name = "dropped")] public int Dropped { get; set; }
    [DataMember(Name = "error")] public string Error { get; set; } = "";
    [DataMember(Name = "ledgerError")] public bool LedgerError { get; set; }
    public string ToJson() => JsonData.Write(this);
}

public interface ICityEventExecutor
{
    bool Execute(CityEventRecord record);
}
