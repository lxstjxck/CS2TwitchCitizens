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
            var snapshot = ViewerPanelSnapshot.Create(Mod.ConnectionStatus.ToString(),
                Mod.ChannelId, gameLoaded, viewers);
            _snapshot.Update(snapshot.ToJson());
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
