import { ModRegistrar } from "cs2/modding";
import { TwitchCitizensButton, TwitchCitizensGameRoot } from "mods/twitch-citizens";

const register: ModRegistrar = (moduleRegistry) => {

    moduleRegistry.append('GameTopRight', TwitchCitizensButton);
    moduleRegistry.append('Game', TwitchCitizensGameRoot);
}

export default register;
