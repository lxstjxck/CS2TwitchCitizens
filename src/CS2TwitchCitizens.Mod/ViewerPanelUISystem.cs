using System;
using Colossal.UI.Binding;
using CS2TwitchCitizens.Commands;
using Game;
using Game.SceneFlow;
using Game.UI;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Throttled, game-thread bridge for the official UI module.</summary>
    public sealed partial class ViewerPanelUISystem : UISystemBase
    {
        private const string Group = "cs2twitchcitizens";
        private ValueBinding<string> _snapshot = null!;
        private ValueBinding<string> _focusResult = null!;
        private ValueBinding<string> _auth = null!;
        private ValueBinding<string> _commandSettings = null!;
        private ValueBinding<string> _commandSettingsStatus = null!;
        private DateTime _nextRefresh;
        private int _focusSequence;
        private string? _pendingViewerId;

        protected override void OnCreate()
        {
            base.OnCreate();
            _snapshot = new ValueBinding<string>(Group, "snapshot",
                new ViewerPanelSnapshot().ToJson(), null, null);
            _focusResult = new ValueBinding<string>(Group, "focusResult", string.Empty, null, null);
            AddBinding(_snapshot);
            AddBinding(_focusResult);
            _auth = new ValueBinding<string>(Group, "auth", new CS2TwitchCitizens.Twitch.TwitchAuthView().ToJson(), null, null);
            AddBinding(_auth);
            _commandSettings = new ValueBinding<string>(Group, "commandSettings",
                (Mod.CommandSettings?.Current ?? CommandSettings.Defaults()).ToJson(), null, null);
            AddBinding(_commandSettings);
            _commandSettingsStatus = new ValueBinding<string>(Group, "commandSettingsStatus",
                Mod.CommandSettings?.LoadFailed == true ? "Recovered" : string.Empty, null, null);
            AddBinding(_commandSettingsStatus);
            AddBinding(new TriggerBinding<string>(Group, "updateCommandSettings", UpdateCommandSettings, null));
            AddBinding(new TriggerBinding<string>(Group, "connectTwitch", _ => Mod.Connection?.Connect(), null));
            AddBinding(new TriggerBinding<string>(Group, "cancelTwitch", _ => Mod.Connection?.Cancel(), null));
            AddBinding(new TriggerBinding<string>(Group, "reconnectTwitch", _ => Mod.Connection?.Reconnect(), null));
            AddBinding(new TriggerBinding<string>(Group, "reauthorizeTwitch", _ => Mod.Connection?.Reauthorize(), null));
            AddBinding(new TriggerBinding<string>(Group, "disconnectTwitch", _ => Mod.Connection?.Disconnect(), null));
            AddBinding(new TriggerBinding<string>(Group, "openTwitchVerification", _ => Mod.Connection?.OpenVerification(), null));
            AddBinding(new TriggerBinding<string>(Group, "focusCitizen", Focus, null));
            AddBinding(new TriggerBinding<string>(Group, "followCitizen", Follow, null));
            AddBinding(new TriggerBinding<string>(Group, "stopFollowing", StopFollowing, null));
            Mod.Log.Info("[CS2TwitchCitizens] ViewerPanelUISystem.OnCreate");
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (_pendingViewerId != null)
            {
                var system = World.GetExistingSystemManaged<CitizenBindingSystem>();
                if (system != null && system.TryPollFocus(out var status))
                {
                    PublishResult(_pendingViewerId, status);
                    _pendingViewerId = null;
                }
            }
            if (DateTime.UtcNow < _nextRefresh) return;
            _nextRefresh = DateTime.UtcNow.AddSeconds(1);
            var gameLoaded = GameManager.instance.gameMode == GameMode.Game;
            var bindingSystem = gameLoaded ? World.GetExistingSystemManaged<CitizenBindingSystem>() : null;
            var viewers = bindingSystem?.GetAllBindings() ?? Array.Empty<ViewerCitizenInfo>();
            var auth = Mod.Connection?.View ?? new CS2TwitchCitizens.Twitch.TwitchAuthView();
            var snapshot = ViewerPanelSnapshot.Create(auth.EventSubStatus,
                Mod.Connection?.UserId ?? string.Empty, gameLoaded, viewers);
            _snapshot.Update(snapshot.ToJson());
            _auth.Update(auth.ToJson());
            _commandSettings.Update((Mod.CommandSettings?.Current ?? CommandSettings.Defaults()).ToJson());
        }

        private void UpdateCommandSettings(string json)
        {
            try
            {
                Mod.CommandSettings?.Update(json);
                _commandSettings.Update((Mod.CommandSettings?.Current ?? CommandSettings.Defaults()).ToJson());
                _commandSettingsStatus.Update(string.Empty);
            }
            catch (Exception ex) { _commandSettingsStatus.Update("SaveFailed");
                Mod.Log.Info("[CS2TwitchCitizens] Command settings update failed: " + ex.GetType().Name); }
        }

        private void Focus(string viewerId)
        {
            var bindingSystem = GameManager.instance.gameMode == GameMode.Game
                ? World.GetExistingSystemManaged<CitizenBindingSystem>() : null;
            var result = bindingSystem == null
                ? CitizenFocusStatus.CameraUnavailable
                : ViewerPanelActions.Focus(viewerId, bindingSystem.FocusCitizen);
            _pendingViewerId = result == CitizenFocusStatus.FocusRequested ? viewerId : null;
            PublishResult(viewerId, result);
            Mod.Log.Info($"[CS2TwitchCitizens] UI FocusCitizen viewer={viewerId} result={result}");
            _nextRefresh = DateTime.MinValue;
        }

        private void Follow(string viewerId)
        {
            var system = GameManager.instance.gameMode == GameMode.Game
                ? World.GetExistingSystemManaged<CitizenBindingSystem>() : null;
            var result = system == null ? CitizenFocusStatus.CameraUnavailable
                : ViewerPanelActions.Focus(viewerId, system.FollowCitizen);
            _pendingViewerId = null;
            PublishResult(viewerId, result);
            Mod.Log.Info($"[CS2TwitchCitizens] UI FollowCitizen viewer={viewerId} result={result}");
        }

        private void StopFollowing(string viewerId)
        {
            var system = GameManager.instance.gameMode == GameMode.Game
                ? World.GetExistingSystemManaged<CitizenBindingSystem>() : null;
            var result = system?.StopFollowing() ?? CitizenFocusStatus.CameraUnavailable;
            _pendingViewerId = null;
            PublishResult(viewerId, result);
            Mod.Log.Info($"[CS2TwitchCitizens] UI StopFollowing viewer={viewerId} result={result}");
        }

        private void PublishResult(string viewerId, CitizenFocusStatus result) =>
            _focusResult.Update(new ViewerFocusFeedback { ViewerId = viewerId,
                Result = result.ToString(), Sequence = ++_focusSequence }.ToJson());
    }
}
