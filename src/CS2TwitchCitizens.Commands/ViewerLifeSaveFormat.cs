using System.IO;

namespace CS2TwitchCitizens.Commands;

public static class ViewerLifeSaveFormat
{
    public const int Version = 3;

    public static void RequireSupported(int version)
    {
        if (version != 1 && version != 2 && version != Version)
            throw new InvalidDataException($"Unsupported viewer life save version {version}.");
    }
}
