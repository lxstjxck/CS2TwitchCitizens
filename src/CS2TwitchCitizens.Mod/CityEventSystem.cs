using System;
using System.Collections.Generic;
using System.Linq;
using CS2TwitchCitizens.Commands;
using Game;
using Game.SceneFlow;
using Game.UI;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Game-thread consumer for Twitch redemptions. The v0.9 executors only present UI.</summary>
    public sealed partial class CityEventSystem : UISystemBase, ICityEventExecutor
    {
        private readonly List<CityEventRecord> _recent = new();
        private readonly Dictionary<string, DateTimeOffset> _lastAction = new();
        private readonly Queue<CityEventRecord> _presentation = new();
        private DateTimeOffset _activeUntil;
        private CityEventRecord? _active;
        private bool _wasLoaded;
        private string _lastError = "";
        public string SnapshotJson => new CityEventSnapshot {
            Events = _recent, Active = _active, Queued = (Mod.Redemptions?.Count ?? 0) + _presentation.Count,
            Dropped = Mod.Redemptions?.Rejected ?? 0,
            Error = _lastError, LedgerError = Mod.RedemptionLedger?.LoadFailed ?? false
        }.ToJson();

        protected override void OnUpdate()
        {
            base.OnUpdate();
            var loaded = GameManager.instance.gameMode == GameMode.Game &&
                World.GetExistingSystemManaged<CitizenBindingSystem>()?.IsCityReady == true;
            if (!loaded && _wasLoaded) ResetCity();
            _wasLoaded = loaded;
            if (_active != null && DateTimeOffset.UtcNow >= _activeUntil) ShowNext();
            if (_active == null && _presentation.Count > 0) ShowNext();
            if (!loaded) { Mod.Redemptions?.Clear(); return; }
            if (Mod.Redemptions == null) return;
            for (var i = 0; i < 4 && Mod.Redemptions.TryDequeue(out var redemption); i++)
                Process(redemption!);
        }

        public void ResetCity()
        {
            Mod.Redemptions?.Clear();
            _recent.Clear(); _lastAction.Clear(); _presentation.Clear(); _active = null; _lastError = "";
        }

        private void Process(RedemptionEvent redemption)
        {
            var ledger = Mod.RedemptionLedger;
            if (ledger == null) return;
            try
            {
                if (redemption.IsUpdate)
                {
                    var canceled = redemption.Status == "canceled";
                    ledger.MarkExternalStatus(redemption.BroadcasterId, redemption.RedemptionId,
                        canceled ? "Canceled" : "FulfilledInTwitch");
                    if (canceled && _active?.RedemptionId == redemption.RedemptionId) _active = null;
                    if (_presentation.Count > 0)
                    {
                        var pending = _presentation.Where(e => e.RedemptionId != redemption.RedemptionId).ToArray();
                        _presentation.Clear();
                        foreach (var item in pending) _presentation.Enqueue(item);
                    }
                    var shown = _recent.FirstOrDefault(e => e.RedemptionId == redemption.RedemptionId);
                    if (shown != null && shown.State == "Queued")
                    {
                        shown.State = canceled ? "Canceled" : "Skipped";
                        shown.Result = canceled ? "Canceled before display" : "Fulfilled elsewhere before display";
                    }
                    else if (shown != null && canceled) { shown.State = "Canceled"; shown.Result = "Canceled in Twitch after display"; }
                    return;
                }
                if (!ledger.TryReserve(redemption.BroadcasterId, redemption.RedemptionId)) return;
                var config = Mod.InteractiveSettings?.Current ?? InteractiveSettings.Defaults();
                var rule = Mod.InteractiveSettings?.Find(redemption.RewardId);
                _lastAction.TryGetValue(redemption.RewardId, out var last);
                var result = InteractivePolicy.Check(config, rule, redemption,
                    last == default ? (DateTimeOffset?)null : last, DateTimeOffset.UtcNow, out var text);
                if (result == "Ready" && rule != null)
                {
                    var record = new CityEventRecord {
                        Id = Guid.NewGuid().ToString("N"), Type = rule.Action,
                        TwitchUserId = redemption.TwitchUserId, BroadcasterId = redemption.BroadcasterId,
                        DisplayName = InteractivePolicy.Clean(redemption.DisplayName, 50),
                        RewardIconUrl = redemption.RewardIconUrl,
                        RedemptionId = redemption.RedemptionId, RewardId = redemption.RewardId,
                        State = "Received",
                        ReceivedAt = redemption.ReceivedAt.ToString("O"), Text = text,
                        DisplaySeconds = rule.DisplaySeconds
                    };
                    if (rule.Action == InteractiveAction.ShowMessage)
                    {
                        var citizen = World.GetExistingSystemManaged<CitizenBindingSystem>()?.GetAllBindings()
                            .FirstOrDefault(v => v.TwitchUserId == redemption.TwitchUserId && v.IsValid &&
                                v.CurrentLifeStatus == "Active");
                        if (citizen != null)
                        {
                            record.Type = "CitizenMessage";
                            record.CitizenName = citizen.CitizenName;
                            record.CanFindCitizen = citizen.PositionAvailable;
                        }
                    }
                    if (Execute(record)) { _lastAction[rule.RewardId] = DateTimeOffset.UtcNow; result = "Queued"; }
                    else result = "ExecutorFailed";
                }
                ledger.Finish(redemption.BroadcasterId, redemption.RedemptionId, result);
                if (result != "Queued")
                {
                    _lastError = result;
                    Remember(new CityEventRecord { Id = Guid.NewGuid().ToString("N"),
                        Type = rule?.Action ?? "UnknownReward", TwitchUserId = redemption.TwitchUserId,
                        BroadcasterId = redemption.BroadcasterId, DisplayName = InteractivePolicy.Clean(redemption.DisplayName, 50),
                        RedemptionId = redemption.RedemptionId, RewardId = redemption.RewardId,
                        State = "Skipped", Result = result,
                        ReceivedAt = redemption.ReceivedAt.ToString("O") });
                }
            }
            catch (Exception ex)
            {
                _lastError = "ProcessingError";
                Mod.Log.Info("[CS2TwitchCitizens] Redemption processing error=" + ex.GetType().Name);
            }
        }

        public bool Execute(CityEventRecord record)
        {
            if (record.Type == InteractiveAction.GameDisaster) return false;
            if (_presentation.Count >= 16) return false;
            record.State = "Queued";
            record.Result = "Waiting for display";
            Remember(record);
            _presentation.Enqueue(record);
            return true;
        }

        private void Remember(CityEventRecord record)
        {
            _recent.Insert(0, record);
            if (_recent.Count > 20) _recent.RemoveAt(_recent.Count - 1);
        }

        private void ShowNext()
        {
            _active = _presentation.Count > 0 ? _presentation.Dequeue() : null;
            if (_active != null)
            {
                _active.State = "Completed";
                _active.Result = "UI displayed; no simulation change";
                _activeUntil = DateTimeOffset.UtcNow.AddSeconds(_active.DisplaySeconds);
                if (!string.IsNullOrEmpty(_active.RedemptionId))
                    try { Mod.RedemptionLedger?.Finish(_active.BroadcasterId, _active.RedemptionId, "Displayed"); }
                    catch (Exception ex) { _lastError = "LedgerWriteFailed";
                        Mod.Log.Info("[CS2TwitchCitizens] Redemption display record error=" + ex.GetType().Name); }
            }
        }

        public void Test(string action)
        {
            if (GameManager.instance.gameMode != GameMode.Game ||
                World.GetExistingSystemManaged<CitizenBindingSystem>()?.IsCityReady != true ||
                action is not (InteractiveAction.ShowMessage or InteractiveAction.CityAnnouncement or
                    InteractiveAction.CityCelebration or InteractiveAction.EmergencyAlert)) return;
            Execute(new CityEventRecord { Id = Guid.NewGuid().ToString("N"), Type = action,
                DisplayName = "Local test", Text = "Cities: Skylines II interactive preview",
                ReceivedAt = DateTimeOffset.UtcNow.ToString("O"), LocalTest = true });
        }

        public void Dismiss() => ShowNext();

    }
}
