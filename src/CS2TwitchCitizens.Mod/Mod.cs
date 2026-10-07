using Colossal.Logging;
using Game;
using Game.Modding;

namespace CS2TwitchCitizens.Mod
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger("CS2TwitchCitizens");

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("[CS2TwitchCitizens] Mod OnLoad");
            updateSystem.UpdateAt<CitizenDiscoverySystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            Log.Info("[CS2TwitchCitizens] Mod OnDispose");
        }
    }
}
