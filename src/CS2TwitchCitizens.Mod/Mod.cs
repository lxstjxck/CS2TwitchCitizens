using System;
using System.IO;
using Colossal.Logging;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;
using Game;
using Game.Modding;

namespace CS2TwitchCitizens.Mod
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger("CS2TwitchCitizens");
        internal static TwitchCommandQueue? CommandQueue { get; private set; }
        internal static bool TwitchEnabled { get; private set; }
        internal static string ChannelId { get; private set; } = string.Empty;
        private static TwitchConnectionStatus _fallbackStatus = TwitchConnectionStatus.Disabled;
        internal static TwitchConnectionStatus ConnectionStatus => _instance?._twitch?.Status ?? _fallbackStatus;
        private static Mod? _instance;
        private TwitchEventSubService? _twitch;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("[CS2TwitchCitizens] Mod.OnLoad ENTER");
            _instance = this;
            CommandQueue = new TwitchCommandQueue();
            Log.Info("[CS2TwitchCitizens] Registering CitizenDiscoverySystem");
            updateSystem.UpdateAt<CitizenDiscoverySystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("[CS2TwitchCitizens] Registering CitizenBindingSystem");
            updateSystem.UpdateAt<CitizenBindingSystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("[CS2TwitchCitizens] Registering ViewerPanelUISystem");
            updateSystem.UpdateAt<ViewerPanelUISystem>(SystemUpdatePhase.UIUpdate);

            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CS2TwitchCitizens", "twitch.config.json");
            if (!File.Exists(configPath))
            {
                Log.Info($"[CS2TwitchCitizens] Twitch disabled: config missing at {configPath}");
                return;
            }
            try
            {
                var config = TwitchConfig.Load(configPath);
                ChannelId = config.BroadcasterUserId;
                if (!config.Enabled)
                {
                    Log.Info("[CS2TwitchCitizens] Twitch disabled by config");
                    return;
                }
                if (!config.IsComplete)
                {
                    Log.Info("[CS2TwitchCitizens] Twitch config incomplete: clientId, accessToken, broadcasterUserId and userId required");
                    return;
                }
                _twitch = new TwitchEventSubService(config, CommandQueue, message => Log.Info(message));
                _fallbackStatus = TwitchConnectionStatus.Disconnected;
                TwitchEnabled = true;
                _twitch.Start();
                Log.Info("[CS2TwitchCitizens] Twitch service starting");
            }
            catch (Exception ex)
            {
                _twitch?.Dispose();
                _twitch = null;
                TwitchEnabled = false;
                _fallbackStatus = TwitchConnectionStatus.Disconnected;
                Log.Info($"[CS2TwitchCitizens] Twitch startup failed: {ex.GetType().Name}");
            }
        }

        public void OnDispose()
        {
            Log.Info("[CS2TwitchCitizens] Mod.OnDispose");
            _twitch?.Dispose();
            _twitch = null;
            TwitchEnabled = false;
            ChannelId = string.Empty;
            _fallbackStatus = TwitchConnectionStatus.Disabled;
            _instance = null;
            CommandQueue = null;
        }
    }
}
