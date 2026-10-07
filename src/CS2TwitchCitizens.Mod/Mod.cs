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
        private TwitchEventSubService? _twitch;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("[CS2TwitchCitizens] Mod.OnLoad ENTER");
            CommandQueue = new TwitchCommandQueue();
            Log.Info("[CS2TwitchCitizens] Registering CitizenDiscoverySystem");
            updateSystem.UpdateAt<CitizenDiscoverySystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("[CS2TwitchCitizens] Registering CitizenBindingSystem");
            updateSystem.UpdateAt<CitizenBindingSystem>(SystemUpdatePhase.GameSimulation);

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
                TwitchEnabled = true;
                _twitch.Start();
                Log.Info("[CS2TwitchCitizens] Twitch service starting");
            }
            catch (Exception ex)
            {
                _twitch?.Dispose();
                _twitch = null;
                TwitchEnabled = false;
                Log.Info($"[CS2TwitchCitizens] Twitch startup failed: {ex.GetType().Name}");
            }
        }

        public void OnDispose()
        {
            Log.Info("[CS2TwitchCitizens] Mod.OnDispose");
            _twitch?.Dispose();
            _twitch = null;
            TwitchEnabled = false;
            CommandQueue = null;
        }
    }
}
