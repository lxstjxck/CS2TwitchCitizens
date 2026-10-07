using Colossal.Logging;
using CS2TwitchCitizens.Commands;
using Game;
using Game.Modding;

namespace CS2TwitchCitizens.Mod
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger("CS2TwitchCitizens");
        internal static TwitchCommandQueue? CommandQueue { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("[CS2TwitchCitizens] Mod.OnLoad ENTER");
            CommandQueue = new TwitchCommandQueue();
            Log.Info("[CS2TwitchCitizens] Registering CitizenDiscoverySystem");
            updateSystem.UpdateAt<CitizenDiscoverySystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("[CS2TwitchCitizens] Registering CitizenBindingSystem");
            updateSystem.UpdateAt<CitizenBindingSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            Log.Info("[CS2TwitchCitizens] Mod.OnDispose");
            CommandQueue = null;
        }
    }
}
