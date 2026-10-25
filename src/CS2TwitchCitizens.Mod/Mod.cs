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
        internal static readonly PersistentModLogger Log = new PersistentModLogger(LogManager.GetLogger("CS2TwitchCitizens"));
        internal static TwitchCommandQueue? CommandQueue { get; private set; }
        internal static TwitchConnectionController? Connection { get; private set; }
        internal static CommandSettingsController? CommandSettings { get; private set; }
        internal static RedemptionQueue? Redemptions { get; private set; }
        internal static RewardActionRegistry? InteractiveSettings { get; private set; }
        internal static RedemptionLedger? RedemptionLedger { get; private set; }
        internal static RewardManagementController? Rewards { get; private set; }
        internal static bool TwitchEnabled => Connection != null;

        public void OnLoad(UpdateSystem updateSystem)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2TwitchCitizens");
            Log.StartFileLogging(Path.Combine(directory, "logs"));
            Log.Info("[CS2TwitchCitizens] START version=0.9.7 persistentLog=enabled");
            CommandQueue = new TwitchCommandQueue();
            Redemptions = new RedemptionQueue();
            updateSystem.UpdateAt<CitizenDiscoverySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<CitizenBindingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ViewerPanelUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<CityEventSystem>(SystemUpdatePhase.UIUpdate);
            CommandSettings = new CommandSettingsController(new CommandSettingsStore(Path.Combine(directory, "commands.json")));
            InteractiveSettings = new RewardActionRegistry(Path.Combine(directory, "interactive.json"));
            RedemptionLedger = new RedemptionLedger(Path.Combine(directory, "redemptions.json"));
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
                new TwitchOAuthClient(), CommandQueue, message => Log.Info(message), legacy, CommandSettings, Redemptions);
            Rewards = new RewardManagementController(Connection, InteractiveSettings, RedemptionLedger,
                Path.Combine(directory, "reward-create"));
            Connection.Start();
        }

        public void OnDispose()
        {
            try
            {
                Rewards?.Dispose();
                Rewards = null;
                Connection?.Dispose();
                Connection = null;
                CommandSettings = null;
                CommandQueue = null;
                Redemptions = null;
                InteractiveSettings = null;
                RedemptionLedger = null;
            }
            finally
            {
                Log.Info("[CS2TwitchCitizens] STOP version=0.9.7");
                Log.StopFileLogging();
            }
        }
    }

    /// <summary>Retains normal Colossal logging and mirrors sanitized entries to a rotating file.</summary>
    internal sealed class PersistentModLogger
    {
        private readonly ILog _game;
        private PersistentDiagnosticLog? _file;
        private int _reportedFailure;

        public PersistentModLogger(ILog game) => _game = game;
        public long DroppedCount => _file?.Dropped ?? 0;
        public void StartFileLogging(string directory)
        {
            StopFileLogging();
            try
            {
                _file = new PersistentDiagnosticLog(directory);
                _reportedFailure = 0;
            }
            catch (Exception ex)
            {
                _game.Info("[CS2TwitchCitizens] Persistent file logger failed to start: " + ex.GetType().Name);
            }
        }

        public void Info(string message)
        {
            _game.Info(message);
            var file = _file;
            if (file == null) return;
            file.TryWrite(message);
            if (file.Failed && System.Threading.Interlocked.Exchange(ref _reportedFailure, 1) == 0)
                _game.Info("[CS2TwitchCitizens] Persistent file logger disabled after I/O failure");
        }

        public void StopFileLogging()
        {
            var current = _file;
            _file = null;
            current?.Dispose();
        }
    }
}
