using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

namespace CS2TwitchCitizens.Mod
{
    [DataContract]
    public sealed class RewardManagerCommand
    {
        [DataMember(Name = "operation")] public string Operation { get; set; } = "";
        [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
        [DataMember(Name = "broadcasterId")] public string BroadcasterId { get; set; } = "";
        [DataMember(Name = "redemptionId")] public string RedemptionId { get; set; } = "";
        [DataMember(Name = "action")] public string Action { get; set; } = "";
        [DataMember(Name = "cost")] public long Cost { get; set; }
        [DataMember(Name = "edit")] public TwitchRewardEdit? Edit { get; set; }
        [DataMember(Name = "expected")] public TwitchRewardEdit? Expected { get; set; }
    }

    [DataContract]
    public sealed class RewardManagerSnapshot
    {
        [DataMember(Name = "broadcasterId")] public string BroadcasterId { get; set; } = "";
        [DataMember(Name = "items")] public TwitchReward[] Items { get; set; } = Array.Empty<TwitchReward>();
        [DataMember(Name = "manageableIds")] public string[] ManageableIds { get; set; } = Array.Empty<string>();
        [DataMember(Name = "redemptions")] public TwitchRedemption[] Redemptions { get; set; } = Array.Empty<TwitchRedemption>();
        [DataMember(Name = "selectedRewardId")] public string SelectedRewardId { get; set; } = "";
        [DataMember(Name = "busy")] public bool Busy { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; } = "";
        [DataMember(Name = "fresh")] public bool Fresh { get; set; }
        [DataMember(Name = "fixedRewards")] public FixedRewardView[] FixedRewards { get; set; } = Array.Empty<FixedRewardView>();
        [DataMember(Name = "legacyActive")] public string[] LegacyActive { get; set; } = Array.Empty<string>();
        public string ToJson() => JsonData.Write(this);
    }

    [DataContract]
    public sealed class FixedRewardView
    {
        [DataMember(Name = "templateId")] public string TemplateId { get; set; } = "";
        [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
        [DataMember(Name = "cost")] public long Cost { get; set; }
        [DataMember(Name = "enabled")] public bool Enabled { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; } = "Missing";
        [DataMember(Name = "iconUrl")] public string IconUrl { get; set; } = "";
    }

    [DataContract]
    internal sealed class PendingRewardCreate
    {
        [DataMember(Name = "title")] public string Title { get; set; } = "";
        [DataMember(Name = "action")] public string Action { get; set; } = "";
        [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
    }

    /// <summary>UI-facing state. Helix work runs asynchronously and never accesses ECS.</summary>
    public sealed class RewardManagementController : IDisposable
    {
        private readonly object _gate = new object();
        private readonly TwitchConnectionController _connection;
        private readonly RewardActionRegistry _registry;
        private readonly RedemptionLedger _ledger;
        private readonly TwitchRewardManager _api;
        private readonly string _recoveryDirectory;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<RewardManagerCommand> _autoQueue = new ConcurrentQueue<RewardManagerCommand>();
        private readonly HashSet<string> _autoIds = new HashSet<string>(StringComparer.Ordinal);
        private Task? _activeWork;
        private Task? _autoWorker;
        private int _autoRunning;
        private RewardManagerSnapshot _state = new RewardManagerSnapshot();
        private bool _disposed;
        private string _pendingCreateTitle = "";
        private string _pendingCreateAction = "";
        private string _pendingCreatedId = "";
        private string _pendingBroadcasterId = "";

        public RewardManagementController(TwitchConnectionController connection, RewardActionRegistry registry,
            RedemptionLedger ledger, string recoveryDirectory, TwitchRewardManager? api = null)
        { _connection = connection; _registry = registry; _ledger = ledger; _recoveryDirectory = recoveryDirectory;
            _api = api ?? new TwitchRewardManager(); }

        public string SnapshotJson
        {
            get { lock (_gate) return _state.BroadcasterId == _connection.UserId ? _state.ToJson()
                : new RewardManagerSnapshot().ToJson(); }
        }

        public void Submit(string json)
        {
            RewardManagerCommand command;
            try
            {
                if (json.Length > 4096) throw new InvalidDataException();
                command = JsonData.Read<RewardManagerCommand>(json);
            }
            catch { SetStatus("InvalidRequest"); return; }
            if (command.Operation is "Bind" or "Unbind" or "Create" or "Update" or "Delete")
            { SetStatus("FixedRewardsOnly"); return; }
            lock (_gate)
            {
                if (_disposed || _state.Busy) return;
                _state.Busy = true; _state.Status = "";
                if (command.Operation is "Sync" or "FixedEnable" or "FixedDisable" or "FixedCost")
                    _state.Fresh = false;
                var cancellation = _stop.Token;
                _activeWork = Task.Run(() => RunAsync(command, cancellation));
            }
        }

        public void EnqueueAutoFulfill(string broadcasterId, string rewardId, string redemptionId)
        {
            if (string.IsNullOrWhiteSpace(rewardId) || string.IsNullOrWhiteSpace(redemptionId) ||
                broadcasterId != _connection.UserId) return;
            var key = broadcasterId + ":" + redemptionId;
            lock (_gate)
            {
                if (_disposed || !_autoIds.Add(key)) return;
                _autoQueue.Enqueue(new RewardManagerCommand { Operation = "Fulfill", RewardId = rewardId,
                    RedemptionId = redemptionId, BroadcasterId = broadcasterId });
            }
            StartAutoWorker();
        }

        private void StartAutoWorker()
        {
            if (_stop.IsCancellationRequested || Interlocked.CompareExchange(ref _autoRunning, 1, 0) != 0) return;
            lock (_gate) _autoWorker = Task.Run(ProcessAutoQueueAsync);
        }

        private async Task ProcessAutoQueueAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested && _autoQueue.TryDequeue(out var command))
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        bool admitted;
                        lock (_gate)
                        {
                            admitted = !_disposed && !_state.Busy;
                            if (admitted) { _state.Busy = true; _state.Status = ""; }
                        }
                        if (admitted) break;
                        await Task.Delay(200, _stop.Token).ConfigureAwait(false);
                    }
                    if (_stop.IsCancellationRequested) break;
                    await RunAsync(command, _stop.Token).ConfigureAwait(false);
                    lock (_gate) _autoIds.Remove(command.BroadcasterId + ":" + command.RedemptionId);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                Interlocked.Exchange(ref _autoRunning, 0);
                if (!_autoQueue.IsEmpty && !_stop.IsCancellationRequested) StartAutoWorker();
            }
        }

        private void Bind(RewardManagerCommand command)
        {
            lock (_gate) if (_disposed || _state.Busy) return;
            try
            {
                if (string.IsNullOrEmpty(command.RewardId) || _state.BroadcasterId != _connection.UserId ||
                    string.IsNullOrEmpty(_state.BroadcasterId)) throw new InvalidDataException();
                var settings = _registry.Current;
                if (command.Operation == "Unbind")
                    settings.Rewards.RemoveAll(r => r.RewardId == command.RewardId);
                else
                {
                    if (!_state.Items.Any(r => r.Id == command.RewardId) ||
                        !InteractiveAction.IsSupported(command.Action) ||
                        command.Action == InteractiveAction.GameDisaster) throw new InvalidDataException();
                    var existing = settings.Rewards.FirstOrDefault(r => r.RewardId == command.RewardId);
                    if (existing == null) settings.Rewards.Add(new RewardRule { RewardId = command.RewardId,
                        Action = command.Action });
                    else existing.Action = command.Action;
                }
                _registry.Update(settings.ToJson());
                SetStatus("Saved");
            }
            catch { SetStatus("SaveFailed"); }
        }

        private async Task RunAsync(RewardManagerCommand command, CancellationToken cancellation)
        {
            try
            {
                var access = _connection.RewardAuthorization ?? throw new UnauthorizedAccessException();
                var client = access.ClientId; var token = access.AccessToken; var broadcaster = access.BroadcasterId;
                if (command.BroadcasterId.Length > 0 && command.BroadcasterId != broadcaster)
                    throw new InvalidDataException("AccountChanged");
                LoadPendingCreate(broadcaster);
                if (command.Operation == "Sync")
                { await SyncAsync(access, cancellation).ConfigureAwait(false); SetStatus("Synced"); return; }
                if (!access.CanManage) throw new UnauthorizedAccessException();
                var all = await _api.ListAsync(client, token, broadcaster, false, cancellation).ConfigureAwait(false);
                var owned = await _api.ListAsync(client, token, broadcaster, true, cancellation).ConfigureAwait(false);
                var ids = new HashSet<string>(owned.Select(r => r.Id), StringComparer.Ordinal);
                if (command.Operation is "FixedEnable" or "FixedDisable" or "FixedCost")
                {
                    await ChangeFixedAsync(command, access, all, owned, cancellation).ConfigureAwait(false);
                    return;
                }
                if (command.Operation == "Create")
                {
                    if (!string.IsNullOrEmpty(_pendingCreateTitle))
                        throw new InvalidDataException("CreateNeedsReconciliation");
                    if (all.Length >= 50) throw new InvalidDataException("RewardLimit");
                    if (command.Edit == null || !InteractiveAction.IsSupported(command.Action) ||
                        command.Action == InteractiveAction.GameDisaster) throw new InvalidDataException("InvalidRequest");
                    command.Edit.Validate(true);
                    if (all.Any(r => r.Title == command.Edit.Title)) throw new InvalidDataException("DuplicateTitle");
                    // POST is never retried after an uncertain network outcome.
                    _pendingCreateTitle = command.Edit.Title;
                    _pendingCreateAction = command.Action;
                    SavePendingCreate();
                    TwitchReward created;
                    try { created = await _api.CreateAsync(client, token, broadcaster, command.Edit, cancellation)
                        .ConfigureAwait(false); }
                    catch (TwitchRewardException ex) when (ex.StatusCode < 500)
                    { ClearPendingCreate(); throw; }
                    _pendingCreatedId = created.Id;
                    SavePendingCreate();
                    if (_connection.UserId != broadcaster) throw new InvalidDataException("AccountChanged");
                    try { SaveRule(created.Id, command.Action); }
                    catch { SetStatus("NeedsSync:" + created.Id); return; }
                    await SyncAsync(access, cancellation).ConfigureAwait(false);
                    SetStatus("Created"); return;
                }
                if (!ids.Contains(command.RewardId)) throw new UnauthorizedAccessException("ExternalReward");
                if (command.Operation == "Update")
                {
                    if (command.Edit == null) throw new InvalidDataException("InvalidRequest");
                    var current = all.FirstOrDefault(r => r.Id == command.RewardId);
                    if (current == null || command.Expected == null || !Matches(current, command.Expected))
                        throw new InvalidDataException("ServerChangedSyncRequired");
                    var updated = await _api.UpdateAsync(client, token, broadcaster, command.RewardId,
                        command.Edit, cancellation).ConfigureAwait(false);
                    if (updated.Id != command.RewardId) throw new InvalidDataException("RewardMismatch");
                    if (_connection.UserId != broadcaster) throw new InvalidDataException("AccountChanged");
                    if (!string.IsNullOrEmpty(command.Action))
                        try { SaveRule(updated.Id, command.Action); }
                        catch { SetStatus("NeedsSync:" + updated.Id); return; }
                    await SyncAsync(access, cancellation).ConfigureAwait(false);
                    SetStatus("Updated"); return;
                }
                if (command.Operation == "Delete")
                {
                    await _api.DeleteAsync(client, token, broadcaster, command.RewardId, cancellation).ConfigureAwait(false);
                    if (_connection.UserId != broadcaster) throw new InvalidDataException("AccountChanged");
                    try
                    {
                        var settings = _registry.Current;
                        settings.Rewards.RemoveAll(r => r.RewardId == command.RewardId);
                        _registry.Update(settings.ToJson());
                    }
                    catch { SetStatus("NeedsSync:" + command.RewardId); return; }
                    await SyncAsync(access, cancellation).ConfigureAwait(false);
                    SetStatus("Deleted"); return;
                }
                if (command.Operation == "Redemptions")
                {
                    var pending = await _api.ListRedemptionsAsync(client, token, broadcaster,
                        command.RewardId, "UNFULFILLED", cancellation).ConfigureAwait(false);
                    lock (_gate) { _state.SelectedRewardId = command.RewardId; _state.Redemptions = pending; }
                    SetStatus("RedemptionsLoaded"); return;
                }
                if (command.Operation == "Fulfill" || command.Operation == "Cancel")
                {
                    if (_ledger.LoadFailed) throw new InvalidDataException("JournalUnavailable");
                    var existing = await _api.GetRedemptionAsync(client, token, broadcaster, command.RewardId,
                        command.RedemptionId, cancellation).ConfigureAwait(false);
                    if (existing.Id != command.RedemptionId || existing.Reward?.Id != command.RewardId ||
                        existing.BroadcasterId != broadcaster || existing.Status != "UNFULFILLED")
                        throw new InvalidDataException("RedemptionUnavailable");
                    if (command.Operation == "Fulfill" &&
                        _ledger.Get(broadcaster, command.RedemptionId) is not (RedemptionLedger.AwaitingTwitchConfirmation or "Displayed"))
                        throw new InvalidDataException("NotDisplayed");
                    var status = command.Operation == "Fulfill" ? "FULFILLED" : "CANCELED";
                    var result = await _api.SetRedemptionStatusAsync(client, token, broadcaster, command.RewardId,
                        command.RedemptionId, status, cancellation).ConfigureAwait(false);
                    if (result.Id != command.RedemptionId || result.Status != status)
                        throw new InvalidDataException("StatusUnconfirmed");
                    _ledger.MarkExternalStatus(broadcaster, command.RedemptionId, status);
                    if (_ledger.Get(broadcaster, command.RedemptionId) != status)
                        throw new InvalidDataException("JournalWriteFailed");
                    var pending = await _api.ListRedemptionsAsync(client, token, broadcaster,
                        command.RewardId, "UNFULFILLED", cancellation).ConfigureAwait(false);
                    lock (_gate) { _state.SelectedRewardId = command.RewardId; _state.Redemptions = pending; }
                    SetStatus(status); return;
                }
                throw new InvalidDataException("InvalidRequest");
            }
            catch (OperationCanceledException) { SetStatus("Canceled"); }
            catch (TwitchRewardException ex) { SetStatus("HTTP" + ex.StatusCode); }
            catch (UnauthorizedAccessException) { SetStatus("AuthorizationRequired"); }
            catch (InvalidDataException ex) { SetStatus(ex.Message); }
            catch (Exception ex) { SetStatus("NetworkOrStorageError:" + ex.GetType().Name); }
            finally { lock (_gate) _state.Busy = false; }
        }

        private async Task SyncAsync(TwitchRewardAuthorization access, CancellationToken cancellation)
        {
            var all = await _api.ListAsync(access.ClientId, access.AccessToken, access.BroadcasterId,
                false, cancellation).ConfigureAwait(false);
            var owned = access.CanManage ? await _api.ListAsync(access.ClientId, access.AccessToken,
                access.BroadcasterId, true, cancellation).ConfigureAwait(false) : Array.Empty<TwitchReward>();
            if (_connection.UserId != access.BroadcasterId) throw new InvalidDataException("AccountChanged");
            if (!string.IsNullOrEmpty(_pendingCreateTitle))
            {
                var template = FixedRewardCatalog.Find(_pendingCreateAction);
                var candidates = template == null ? Array.Empty<TwitchReward>() : owned.Where(r =>
                    r.Title == template.Title && r.Prompt == template.Prompt && r.IsUserInputRequired).ToArray();
                var match = owned.FirstOrDefault(r => _pendingCreatedId.Length > 0 && r.Id == _pendingCreatedId) ??
                    (candidates.Length == 1 ? candidates[0] : null);
                if (match == null) throw new InvalidDataException("CreateOutcomeUnknownCheckTwitch");
                try
                {
                    if (template == null) SaveRule(match.Id, _pendingCreateAction);
                    else SaveFixedRule(match.Id, _pendingCreateAction, true);
                }
                catch { throw new InvalidDataException("NeedsSync:" + match.Id); }
                ClearPendingCreate();
            }
            var settings = _registry.Current;
            var legacy = settings.Rewards.Where(r => r.Enabled && FixedRewardCatalog.Find(r.Action) == null).ToArray();
            if (legacy.Length > 0)
            {
                foreach (var rule in legacy) rule.Enabled = false;
                _registry.Update(settings.ToJson());
            }
            var ownedIds = new HashSet<string>(owned.Select(r => r.Id), StringComparer.Ordinal);
            var fixedViews = FixedRewardCatalog.All.Select(template =>
            {
                var links = settings.Rewards.Where(r => r.Action == template.Id).ToArray();
                var link = links.Length == 1 ? links[0] : null;
                var reward = link == null ? null : all.FirstOrDefault(r => r.Id == link.RewardId);
                var conflict = links.Length > 1 || link != null && (reward == null || !ownedIds.Contains(link.RewardId)) ||
                    link == null && all.Any(r => r.Title == template.Title);
                return new FixedRewardView {
                    TemplateId = template.Id, RewardId = reward?.Id ?? "",
                    Cost = reward?.Cost ?? template.DefaultCost,
                    Enabled = reward?.IsEnabled == true && link?.Enabled == true,
                    Status = conflict ? "Conflict" : reward == null ? "Missing" :
                        reward.IsEnabled && link?.Enabled == true ? "Synced" : "Disabled",
                    IconUrl = TwitchRewardCatalog.SafeIcon(reward?.Image?.Url1x) ??
                        TwitchRewardCatalog.SafeIcon(reward?.DefaultImage?.Url1x) ?? ""
                };
            }).ToArray();
            lock (_gate)
            {
                _state.BroadcasterId = access.BroadcasterId;
                _state.Fresh = true;
                _state.Items = all;
                _state.ManageableIds = owned.Select(r => r.Id).ToArray();
                _state.FixedRewards = fixedViews;
                _state.LegacyActive = settings.Rewards.Where(r => FixedRewardCatalog.Find(r.Action) == null &&
                    all.Any(item => item.Id == r.RewardId && item.IsEnabled)).Select(r => r.RewardId).ToArray();
                _state.Redemptions = Array.Empty<TwitchRedemption>();
                _state.SelectedRewardId = "";
            }
        }

        private async Task ChangeFixedAsync(RewardManagerCommand command, TwitchRewardAuthorization access,
            TwitchReward[] all, TwitchReward[] owned, CancellationToken cancellation)
        {
            var template = FixedRewardCatalog.Find(command.Action) ?? throw new InvalidDataException("InvalidTemplate");
            if ((command.Operation == "FixedCost" || command.Operation == "FixedEnable" && command.Cost != 0) &&
                command.Cost < 1)
                throw new InvalidDataException("InvalidCost");
            var settings = _registry.Current;
            var links = settings.Rewards.Where(r => r.Action == template.Id).ToArray();
            if (links.Length > 1) throw new InvalidDataException("RewardConflict");
            var link = links.FirstOrDefault();
            var reward = link == null ? null : owned.FirstOrDefault(r => r.Id == link.RewardId);
            if (link != null && reward == null) throw new InvalidDataException("RewardConflict");
            if (reward == null && all.Any(r => r.Title == template.Title))
                throw new InvalidDataException("RewardConflict");
            if (command.Operation != "FixedEnable" && reward == null)
                throw new InvalidDataException("RewardMissing");
            if (command.Operation == "FixedEnable" && reward == null)
            {
                if (_pendingCreateTitle.Length > 0) throw new InvalidDataException("CreateNeedsReconciliation");
                if (all.Length >= 50) throw new InvalidDataException("RewardLimit");
                _pendingCreateTitle = template.Title;
                _pendingCreateAction = template.Id;
                SavePendingCreate();
                TwitchReward created;
                try { created = await _api.CreateAsync(access.ClientId, access.AccessToken,
                    access.BroadcasterId, template.CreateEdit(command.Cost > 0 ? command.Cost : template.DefaultCost),
                    cancellation).ConfigureAwait(false); }
                catch (TwitchRewardException ex) when (ex.StatusCode < 500)
                { ClearPendingCreate(); throw; }
                _pendingCreatedId = created.Id;
                SavePendingCreate();
                if (_connection.UserId != access.BroadcasterId) throw new InvalidDataException("AccountChanged");
                SaveFixedRule(created.Id, template.Id, true);
                ClearPendingCreate();
            }
            else if (reward != null)
            {
                var patch = command.Operation switch {
                    "FixedCost" => new TwitchRewardPatch { Cost = command.Cost },
                    "FixedEnable" => new TwitchRewardPatch { IsEnabled = true },
                    _ => new TwitchRewardPatch { IsEnabled = false }
                };
                var updated = await _api.PatchAsync(access.ClientId, access.AccessToken,
                    access.BroadcasterId, reward.Id, patch, cancellation).ConfigureAwait(false);
                if (updated.Id != reward.Id || patch.Cost.HasValue && updated.Cost != patch.Cost.Value ||
                    patch.IsEnabled.HasValue && updated.IsEnabled != patch.IsEnabled.Value)
                    throw new InvalidDataException("ServerChangedSyncRequired");
                if (_connection.UserId != access.BroadcasterId) throw new InvalidDataException("AccountChanged");
                if (command.Operation != "FixedCost")
                    SaveFixedRule(reward.Id, template.Id, command.Operation == "FixedEnable");
            }
            await SyncAsync(access, cancellation).ConfigureAwait(false);
            SetStatus(command.Operation == "FixedCost" ? "Updated" :
                command.Operation == "FixedEnable" ? "Enabled" : "Disabled");
        }

        private void SaveFixedRule(string rewardId, string action, bool enabled)
        {
            var template = FixedRewardCatalog.Find(action) ?? throw new InvalidDataException("InvalidTemplate");
            var settings = _registry.Current;
            var existing = settings.Rewards.FirstOrDefault(r => r.RewardId == rewardId);
            if (existing == null) settings.Rewards.Add(new RewardRule { RewardId = rewardId,
                Action = action, Enabled = enabled, DisplaySeconds = template.DisplaySeconds });
            else { existing.Action = action; existing.Enabled = enabled;
                existing.DisplaySeconds = template.DisplaySeconds; }
            if (enabled) settings.Enabled = true;
            _registry.Update(settings.ToJson());
        }

        private void SaveRule(string rewardId, string action)
        {
            if (!InteractiveAction.IsSupported(action) || action == InteractiveAction.GameDisaster)
                throw new InvalidDataException("InvalidAction");
            var settings = _registry.Current;
            var existing = settings.Rewards.FirstOrDefault(r => r.RewardId == rewardId);
            if (existing == null) settings.Rewards.Add(new RewardRule { RewardId = rewardId, Action = action });
            else existing.Action = action;
            _registry.Update(settings.ToJson());
        }
        private static bool Matches(TwitchReward actual, TwitchRewardEdit expected)
        {
            var stream = actual.MaxPerStream;
            var viewer = actual.MaxPerUserPerStream;
            var cooldown = actual.GlobalCooldown;
            return actual.Title == expected.Title && actual.Prompt == expected.Prompt &&
                actual.Cost == expected.Cost &&
                (string.IsNullOrEmpty(actual.BackgroundColor) ? "#00E5CB" : actual.BackgroundColor) == expected.BackgroundColor &&
                actual.IsEnabled == expected.IsEnabled && actual.IsPaused == expected.IsPaused &&
                actual.IsUserInputRequired == expected.IsUserInputRequired &&
                (stream?.IsEnabled ?? false) == expected.IsMaxPerStreamEnabled &&
                (!expected.IsMaxPerStreamEnabled || stream?.MaxPerStream == expected.MaxPerStream) &&
                (viewer?.IsEnabled ?? false) == expected.IsMaxPerUserPerStreamEnabled &&
                (!expected.IsMaxPerUserPerStreamEnabled || viewer?.MaxPerUserPerStream == expected.MaxPerUserPerStream) &&
                (cooldown?.IsEnabled ?? false) == expected.IsGlobalCooldownEnabled &&
                (!expected.IsGlobalCooldownEnabled || cooldown?.Seconds == expected.GlobalCooldownSeconds) &&
                actual.SkipQueue == expected.SkipQueue;
        }
        private void ClearPendingCreate()
        {
            var path = PendingPath(_pendingBroadcasterId);
            if (File.Exists(path)) File.Delete(path);
            _pendingCreateTitle = ""; _pendingCreateAction = ""; _pendingCreatedId = "";
        }
        private void LoadPendingCreate(string broadcasterId)
        {
            if (broadcasterId.Any(c => !char.IsDigit(c))) throw new InvalidDataException("InvalidBroadcasterId");
            _pendingBroadcasterId = broadcasterId;
            var path = PendingPath(broadcasterId);
            var record = File.Exists(path) ? JsonData.Read<PendingRewardCreate>(File.ReadAllText(path)) : null;
            if (record != null && (string.IsNullOrWhiteSpace(record.Title) ||
                !InteractiveAction.IsSupported(record.Action)))
                throw new InvalidDataException("CreateRecoveryCorrupt");
            _pendingCreateTitle = record?.Title ?? "";
            _pendingCreateAction = record?.Action ?? "";
            _pendingCreatedId = record?.RewardId ?? "";
        }
        private void SavePendingCreate() => AtomicJson.Write(PendingPath(_pendingBroadcasterId),
            new PendingRewardCreate { Title = _pendingCreateTitle, Action = _pendingCreateAction,
                RewardId = _pendingCreatedId });
        private string PendingPath(string broadcasterId) => Path.Combine(_recoveryDirectory,
            broadcasterId + ".json");
        private void SetStatus(string status) { lock (_gate) _state.Status = status; }
        public void Dispose()
        {
            Task[] tasks;
            lock (_gate)
            {
                _disposed = true; _stop.Cancel();
                tasks = new[] { _activeWork, _autoWorker }.Where(task => task != null).Cast<Task>().ToArray();
            }
            if (tasks.Length == 0) { _api.Dispose(); _stop.Dispose(); }
            else Task.WhenAll(tasks).ContinueWith(_ => { _api.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
        }
    }
}
