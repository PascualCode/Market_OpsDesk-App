using PdfSharp.Fonts;

namespace Asistente.Api.Posters;

/// <summary>
/// Resolver de fuentes para PDFsharp en Linux.
///
/// Para los carteles usamos una fuente tipo Times New Roman:
/// Liberation Serif.
/// </summary>
public sealed class DejaVuFontResolver : IFontResolver
{
    private const string RegularFaceName = "LiberationSerif#Regular";
    private const string BoldFaceName = "LiberationSerif#Bold";

    private static readonly string[] RegularCandidates =
    [
        "/usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf",
        "/usr/share/fonts/truetype/liberation2/LiberationSerif-Regular.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf"
    ];

    private static readonly string[] BoldCandidates =
    [
        "/usr/share/fonts/truetype/liberation/LiberationSerif-Bold.ttf",
        "/usr/share/fonts/truetype/liberation2/LiberationSerif-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
    ];

    public FontResolverInfo ResolveTypeface(
        string familyName,
        bool isBold,
        bool isItalic)
    {
        return isBold
            ? new FontResolverInfo(BoldFaceName)
            : new FontResolverInfo(RegularFaceName);
    }

    public byte[] GetFont(string faceName)
    {
        var candidates =
            faceName == BoldFaceName
                ? BoldCandidates
                : RegularCandidates;

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        throw new FileNotFoundException(
            $"No se ha encontrado ninguna fuente válida para {faceName}."
        );
    }
}