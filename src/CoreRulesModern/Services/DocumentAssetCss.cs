using System.IO;
using System.Text;

namespace CoreRulesModern.Services;

/// <summary>
/// Builds immutable CSS for packaged fonts and surfaces once per process.
/// </summary>
public static class DocumentAssetCss
{
    private static readonly Lazy<string> PackagedFonts = new(CreatePackagedFontCss);
    private static readonly Lazy<string> LegacyFonts = new(CreateLegacyFontCss);
    private static readonly Lazy<string> Parchment = new(CreateParchmentBackgroundImage);

    public static string PackagedFontCss => PackagedFonts.Value;
    public static string LegacyFontCss => LegacyFonts.Value;
    public static string ParchmentBackgroundImage => Parchment.Value;

    public const string ExplicitFontCss =
        ".core-rules-friz-bold,.core-rules-friz-bold *{" +
        "font-family:'Core Rules Friz Quadrata Bold','Friz Quadrata Bold',serif!important;" +
        "font-weight:bold!important;}" +
        ".core-rules-friz-regular,.core-rules-friz-regular *{" +
        "font-family:'Core Rules Friz Quadrata','Friz Quadrata',serif!important;" +
        "font-weight:normal!important;}" +
        ".core-rules-quadrat-xbold,.core-rules-quadrat-xbold *{" +
        "font-family:'Core Rules Quadrat Serial XBold','quadrat-serial-xbold',serif!important;" +
        "font-weight:bold!important;}";

    public const string ExplicitFontScript =
        "for(const font of document.querySelectorAll('font[face]')){" +
        "const faces=(font.getAttribute('face')||'').split(',').map(value=>value.trim().replace(/^['\\\"]|['\\\"]$/g,''));" +
        "if(faces.some(value=>value.toLowerCase()==='friz quadrata bold'))font.classList.add('core-rules-friz-bold');" +
        "else if(faces.some(value=>value.toLowerCase()==='friz quadrata'))font.classList.add('core-rules-friz-regular');" +
        "else if(faces.some(value=>value.toLowerCase()==='quadrat-serial-xbold'))font.classList.add('core-rules-quadrat-xbold');}";

    public const string ThemedScrollbarCss =
        "html,body{scrollbar-face-color:#765531;scrollbar-track-color:#1d130e;" +
        "scrollbar-arrow-color:#fff2d4;scrollbar-highlight-color:#c7a568;" +
        "scrollbar-shadow-color:#120b08;scrollbar-3dlight-color:#9b7842;" +
        "scrollbar-darkshadow-color:#120b08;}" +
        "::-webkit-scrollbar{width:14px;height:14px;background:#1d130e;}" +
        "::-webkit-scrollbar-track{background:#1d130e;border:1px solid #5d432a;}" +
        "::-webkit-scrollbar-thumb{background:#765531;border:1px solid #9b7842;border-radius:4px;}" +
        "::-webkit-scrollbar-thumb:hover{background:#957040;}" +
        "::-webkit-scrollbar-corner{background:#1d130e;}";

    private static string CreatePackagedFontCss() => CreateFontCss(embed: true);

    private static string CreateLegacyFontCss() => CreateFontCss(embed: false);

    private static string CreateFontCss(bool embed)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        if (!Directory.Exists(folder)) return string.Empty;

        var css = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(IsFontFile))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var family = GetFamily(name);
            if (family is null) continue;

            var isOpenType = path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);
            var format = isOpenType ? "opentype" : "truetype";
            var source = embed
                ? $"data:{(isOpenType ? "font/otf" : "font/ttf")};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}"
                : new Uri(path).AbsoluteUri;
            var weight = name.Contains("bold", StringComparison.OrdinalIgnoreCase) ? "bold" : "normal";
            var style = name.Contains("italic", StringComparison.OrdinalIgnoreCase) ? "italic" : "normal";
            css.Append("@font-face{font-family:'").Append(family).Append("';src:url('")
                .Append(source).Append("') format('").Append(format)
                .Append("');font-style:").Append(style).Append(";font-weight:").Append(weight).Append(";}");
        }

        return css.ToString();
    }

    private static bool IsFontFile(string path) =>
        path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase);

    private static string? GetFamily(string name) =>
        name.Contains("quadrat-serial-xbold", StringComparison.OrdinalIgnoreCase) ? "Core Rules Quadrat Serial XBold" :
        name.Contains("korinna", StringComparison.OrdinalIgnoreCase) ? "Core Rules Korinna" :
        name.Contains("honda", StringComparison.OrdinalIgnoreCase) ? "Core Rules Honda" :
        name.Contains("friz", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("bold", StringComparison.OrdinalIgnoreCase) ? "Core Rules Friz Quadrata Bold" :
        name.Contains("friz", StringComparison.OrdinalIgnoreCase) ? "Core Rules Friz Quadrata" :
        name.Contains("university", StringComparison.OrdinalIgnoreCase) ? "Core Rules University Roman" :
        name.Contains("antiqua", StringComparison.OrdinalIgnoreCase) ? "Core Rules Book Antiqua" : null;

    private static string CreateParchmentBackgroundImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ParchmentTexture.jpg");
        if (!File.Exists(path)) return "none";

        try
        {
            return $"url('data:image/jpeg;base64,{Convert.ToBase64String(File.ReadAllBytes(path))}')";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "none";
        }
    }
}
