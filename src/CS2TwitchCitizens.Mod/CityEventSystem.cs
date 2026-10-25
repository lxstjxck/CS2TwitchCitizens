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
        private readonly InteractivePresentationQueue _presentation = new();
        private bool _wasLoaded;
        private bool _wasConnected;
        private bool _reportedCityNotReady;
        private string _lastError = "";
        public string ActiveId => _presentation.Active?.Id ?? "";
        public string SnapshotJson => new CityEventSnapshot {
            Events = _recent, Active = _presentation.Active,
            Queued = (Mod.Redemptions?.Count ?? 0) + _presentation.PendingCount,
            Dropped = Mod.Redemptions?.Rejected ?? 0,
            Error = _lastError, LedgerError = Mod.RedemptionLedger?.LoadFailed ?? false
        }.ToJson();

        protected override void OnUpdate()
        {
            base.OnUpdate();
            var inGame = GameManager.instance.gameMode == GameMode.Game;
            var cityReady = World.GetExistingSystemManaged<CitizenBindingSystem>()?.IsCityReady == true;
            if (!inGame && _wasLoaded) ResetCity();
            _wasLoaded = inGame;
            var connection = Mod.Connection?.View;
            var connected = connection?.State == "Connected" && connection.EventSubStatus == "Connected";
            if (!connected && _wasConnected) Mod.Redemptions?.Clear();
            _wasConnected = connected;
            if (!inGame) { Mod.Redemptions?.Clear(); return; }
            if (!connected) Mod.Redemptions?.Clear();
            if (_presentation.NeedsAdvance(DateTimeOffset.UtcNow)) ShowNext();
            if (!connected || Mod.Redemptions == null) return;
            if (!cityReady)
            {
                if (!_reportedCityNotReady && Mod.Redemptions.Count > 0)
                {
                    Mod.Log.Info("[Interactive] stage=Policy result=CityNotReady");
                    _reportedCityNotReady = true;
                }
                return;
            }
            _reportedCityNotReady = false;
            for (var i = 0; i < 4 && Mod.Redemptions.TryDequeue(out var redemption); i++)
                Process(redemption!);
        }

        public void ResetCity()
        {
            foreach (var record in _presentation.Reset())
                if (!string.IsNullOrEmpty(record.RedemptionId) && record.State != "Completed")
                    try { Mod.RedemptionLedger?.Finish(record.BroadcasterId, record.RedemptionId,
                        RedemptionLedger.ManualReview); }
                    catch { _lastError = "LedgerWriteFailed"; }
            Mod.Redemptions?.Clear();
            _recent.Clear(); _lastAction.Clear(); _lastError = "";
            _reportedCityNotReady = false;
        }

        private void Process(RedemptionEvent redemption)
        {
            var ledger = Mod.RedemptionLedger;
            if (ledger == null || ledger.LoadFailed)
            {
                Mod.Log.Info("[Interactive] stage=Policy result=LedgerUnavailable");
                return;
            }
            try
            {
                if (redemption.IsUpdate)
                {
                    var canceled = redemption.Status == "canceled";
                    ledger.MarkExternalStatus(redemption.BroadcasterId, redemption.RedemptionId,
                        canceled ? "Canceled" : "FulfilledInTwitch");
                    _presentation.RemoveRedemption(redemption.RedemptionId);
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
                Mod.Log.Info("[Interactive] stage=Policy result=" +
                    (result == "UnknownOrDisabledReward" ? "UnknownReward" : result));
                if (result == "Ready" && rule != null)
                {
                    ledger.Finish(redemption.BroadcasterId, redemption.RedemptionId, RedemptionLedger.Reserved);
                    var record = new CityEventRecord {
                        Id = Guid.NewGuid().ToString("N"), Type = rule.Action,
                        TwitchUserId = redemption.TwitchUserId, BroadcasterId = redemption.BroadcasterId,
                        DisplayName = InteractivePolicy.Clean(redemption.DisplayName, 50),
                        RewardIconUrl = redemption.RewardIconUrl,
                        RedemptionId = redemption.RedemptionId, RewardId = redemption.RewardId,
                        State = "Received",
                        ReceivedAt = redemption.ReceivedAt.ToString("O"), Text = text,
                        DisplaySeconds = rule.Action == InteractiveAction.CityAnnouncement ? 30 : 8
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
                    if (Execute(record)) { _lastAction[rule.RewardId] = DateTimeOffset.UtcNow;
                        result = RedemptionLedger.AwaitingExecution; }
                    else result = "QueueFull";
                }
                ledger.Finish(redemption.BroadcasterId, redemption.RedemptionId, result);
                if (result != RedemptionLedger.AwaitingExecution)
                {
                    _lastError = result;
                    Remember(new CityEventRecord { Id = Guid.NewGuid().ToString("N"),
                        Type = rule?.Action ?? "UnknownReward", TwitchUserId = redemption.TwitchUserId,
                        BroadcasterId = redemption.BroadcasterId, DisplayName = InteractivePolicy.Clean(redemption.DisplayName, 50),
                        RedemptionId = redemption.RedemptionId, RewardId = redemption.RewardId,
                        State = "Skipped", Result = result, Text = text,
                        ReceivedAt = redemption.ReceivedAt.ToString("O") });
                }
            }
            catch (Exception ex)
            {
                _lastError = "ProcessingError";
                try { ledger.Finish(redemption.BroadcasterId, redemption.RedemptionId, RedemptionLedger.ManualReview); }
                catch { _lastError = "LedgerWriteFailed"; }
                Mod.Log.Info("[CS2TwitchCitizens] Redemption processing error=" + ex.GetType().Name);
            }
        }

        public bool Execute(CityEventRecord record)
        {
            if (record.Type is not (InteractiveAction.ShowMessage or InteractiveAction.CityAnnouncement or "CitizenMessage"))
            { Mod.Log.Info("[Interactive] stage=Execute result=InvalidEvent"); return false; }
            if (!_presentation.TryEnqueue(record))
            { Mod.Log.Info("[Interactive] stage=Execute result=QueueFull"); return false; }
            Remember(record);
            Mod.Log.Info("[Interactive] stage=Execute result=queued");
            return true;
        }

        private void Remember(CityEventRecord record)
        {
            _recent.Insert(0, record);
            if (_recent.Count > 20) _recent.RemoveAt(_recent.Count - 1);
        }

        private void ShowNext()
        {
            var previous = _presentation.Advance(DateTimeOffset.UtcNow);
            if (previous != null && previous.State == "AwaitingDisplay")
            {
                previous.State = "Skipped";
                previous.Result = "UI did not acknowledge display";
                Mod.Log.Info("[Interactive] stage=ShowNext result=UIAcknowledgementMissing");
                if (!string.IsNullOrEmpty(previous.RedemptionId))
                    try { Mod.RedemptionLedger?.Finish(previous.BroadcasterId, previous.RedemptionId,
                        RedemptionLedger.ManualReview); }
                    catch { _lastError = "LedgerWriteFailed"; }
            }
            else if (previous != null && previous.State == "Completed")
            { previous.State = "Closed"; previous.Result = "Displayed and closed"; }
            if (_presentation.Active != null)
            {
                Mod.Log.Info("[Interactive] stage=ShowNext result=active");
            }
        }

        public void Acknowledge(string id)
        {
            if (!_presentation.Acknowledge(id, DateTimeOffset.UtcNow))
            { Mod.Log.Info("[Interactive] stage=Acknowledge result=NoActiveEvent"); return; }
            Mod.Log.Info("[Interactive] stage=Acknowledge result=received");
            var active = _presentation.Active!;
            if (string.IsNullOrEmpty(active.RedemptionId)) return;
            try { Mod.RedemptionLedger?.Finish(active.BroadcasterId, active.RedemptionId,
                RedemptionLedger.AwaitingTwitchConfirmation); }
            catch (Exception ex) { _lastError = "LedgerWriteFailed";
                Mod.Log.Info("[CS2TwitchCitizens] Redemption display record error=" + ex.GetType().Name); }
            // Manual Twitch confirmation remains required; DOM geometry cannot prove human visibility.
        }

        public void Test(string action)
        {
            if (GameManager.instance.gameMode != GameMode.Game)
            { Mod.Log.Info("[Interactive] stage=Test result=CityNotReady"); return; }
            if (action is not (InteractiveAction.ShowMessage or InteractiveAction.CityAnnouncement))
            { Mod.Log.Info("[Interactive] stage=Test result=InvalidEvent"); return; }
            var accepted = Execute(new CityEventRecord { Id = Guid.NewGuid().ToString("N"), Type = action,
                DisplayName = "Local test", Text = "Cities: Skylines II interactive preview",
                ReceivedAt = DateTimeOffset.UtcNow.ToString("O"), LocalTest = true,
                DisplaySeconds = action == InteractiveAction.CityAnnouncement ? 30 : 8 });
            Mod.Log.Info("[Interactive] stage=Test result=" + (accepted ? "accepted" : "QueueFull"));
        }

        public void Dismiss(string id) { if (_presentation.Active?.Id == id) ShowNext(); }

    }
}
