using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CS2TwitchCitizens.Twitch;

[DataContract]
public sealed class TwitchCredentials
{
    [DataMember(Name = "access_token")] public string AccessToken { get; set; } = "";
    [DataMember(Name = "refresh_token")] public string RefreshToken { get; set; } = "";
    [DataMember(Name = "user_id")] public string UserId { get; set; } = "";
    [DataMember(Name = "login")] public string Login { get; set; } = "";
    [DataMember(Name = "display_name")] public string DisplayName { get; set; } = "";
    [DataMember(Name = "expires_at")] public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Windows DPAPI CurrentUser; the encrypted file is separate from city saves and public settings.</summary>
public sealed class TwitchCredentialStore
{
    private readonly string _path;
    public TwitchCredentialStore(string path) => _path = path;
    public bool Exists => File.Exists(_path);

    public TwitchCredentials? Load()
    {
        if (!File.Exists(_path)) return null;
        var bytes = Unprotect(File.ReadAllBytes(_path));
        using var stream = new MemoryStream(bytes);
        return (TwitchCredentials?)new DataContractJsonSerializer(typeof(TwitchCredentials)).ReadObject(stream);
    }

    public void Save(TwitchCredentials credentials)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(TwitchCredentials)).WriteObject(stream, credentials);
        var encrypted = Protect(stream.ToArray());
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Delete() { if (File.Exists(_path)) File.Delete(_path); }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Protect(byte[] value) => Transform(value, true);
    private static byte[] Unprotect(byte[] value) => Transform(value, false);
    private static byte[] Transform(byte[] value, bool encrypt)
    {
        var input = new Blob { Size = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var ok = encrypt
                ? CryptProtectData(ref input, "CS2TwitchCitizens", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!ok) throw new System.Security.Cryptography.CryptographicException("Windows credential protection failed.");
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
