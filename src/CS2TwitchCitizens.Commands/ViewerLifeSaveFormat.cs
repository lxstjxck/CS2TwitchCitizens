using System.IO;

namespace CS2TwitchCitizens.Commands;

public static class ViewerLifeSaveFormat
{
    public const int Version = 1;

    public static void RequireSupported(int version)
    {
        if (version != Version)
            throw new InvalidDataException($"Unsupported viewer life save version {version}.");
    }
}
