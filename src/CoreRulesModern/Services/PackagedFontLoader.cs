using System.Runtime.InteropServices;
using System.IO;

namespace CoreRulesModern.Services;

public sealed class PackagedFontLoader : IDisposable
{
    private const uint PrivateFont = 0x10;
    private string? _loadedPath;

    public bool Load()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Fonts",
            "ITC Korinna Regular.otf");

        if (!File.Exists(path)) return false;
        if (AddFontResourceEx(path, PrivateFont, IntPtr.Zero) == 0) return false;

        _loadedPath = path;
        return true;
    }

    public void Dispose()
    {
        if (_loadedPath is null) return;
        RemoveFontResourceEx(_loadedPath, PrivateFont, IntPtr.Zero);
        _loadedPath = null;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);
}
