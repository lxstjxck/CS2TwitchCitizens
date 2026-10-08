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
        internal static TwitchConnectionController? Connection { get; private set; }
        internal static bool TwitchEnabled => Connection != null;

        public void OnLoad(UpdateSystem updateSystem)
        {
            CommandQueue = new TwitchCommandQueue();
            updateSystem.UpdateAt<CitizenDiscoverySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<CitizenBindingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ViewerPanelUISystem>(SystemUpdatePhase.UIUpdate);
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2TwitchCitizens");
            var path = Path.Combine(directory, "twitch.config.json");
            var clientId = TwitchClientId.Value;
            if (string.IsNullOrWhiteSpace(clientId))
                clientId = Environment.GetEnvironmentVariable("CS2TWITCHCITIZENS_CLIENT_ID") ?? "";
            var legacy = File.Exists(path);
            if (legacy)
            {
                try { if (string.IsNullOrWhiteSpace(clientId)) clientId = TwitchConfig.Load(path).ClientId; }
                catch (Exception ex) { Log.Info("[CS2TwitchCitizens] Legacy config unreadable: " + ex.GetType().Name); }
            }
            Connection = new TwitchConnectionController(clientId,
                new TwitchCredentialStore(Path.Combine(directory, "twitch.credentials.dpapi")),
                new TwitchOAuthClient(), CommandQueue, message => Log.Info(message), legacy);
            Connection.Start();
        }

        public void OnDispose()
        {
            Connection?.Dispose();
            Connection = null;
            CommandQueue = null;
        }
    }
}
