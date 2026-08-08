using System.Runtime.InteropServices;
using System.IO;

namespace CoreRulesModern.Services;

public sealed class PackagedFontLoader : IDisposable
{
    private const uint PrivateFont = 0x10;
    private readonly List<string> _loadedPaths = [];

    public bool Load()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        if (!Directory.Exists(folder)) return false;

        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(path => path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            if (AddFontResourceEx(path, PrivateFont, IntPtr.Zero) != 0) _loadedPaths.Add(path);
        }

        return _loadedPaths.Count > 0;
    }

    public void Dispose()
    {
        foreach (var path in _loadedPaths)
        {
            RemoveFontResourceEx(path, PrivateFont, IntPtr.Zero);
        }

        _loadedPaths.Clear();
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);
}
