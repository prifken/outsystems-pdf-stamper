using PdfSharpCore.Fonts;

namespace PdfStamperLibrary;

/// <summary>
/// Provides fonts from embedded resources.
/// Required because AWS Lambda (where ODC External Libraries run)
/// has no system fonts installed.
/// </summary>
public class EmbeddedFontResolver : IFontResolver
{
    public static readonly EmbeddedFontResolver Instance = new();

    private static readonly Dictionary<string, byte[]> _cache = new();
    private static readonly object _lock = new();

    public string DefaultFontName => "DejaVuSans";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = isBold ? "DejaVuSans-Bold.ttf" : "DejaVuSans-Regular.ttf";
        return new FontResolverInfo(face);
    }

    public byte[] GetFont(string faceName)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(faceName, out var cached))
                return cached;

            var resourceName = $"PdfStamperLibrary.fonts.{faceName}";
            var assembly = typeof(EmbeddedFontResolver).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Font resource not found: {resourceName}. " +
                    $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            _cache[faceName] = bytes;
            return bytes;
        }
    }
}
