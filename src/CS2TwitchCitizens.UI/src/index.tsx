import { ModRegistrar } from "cs2/modding";
import { TwitchCitizensButton, TwitchCitizensPanel } from "mods/twitch-citizens";

const register: ModRegistrar = (moduleRegistry) => {

    moduleRegistry.append('GameTopRight', TwitchCitizensButton);
    moduleRegistry.append('Game', TwitchCitizensPanel);
}

export default register;
