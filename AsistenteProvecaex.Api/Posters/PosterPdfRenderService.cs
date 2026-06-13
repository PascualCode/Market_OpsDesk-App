using Asistente.Api.Configuration;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Globalization;
using System.IO;

namespace Asistente.Api.Posters;

/// <summary>
/// Servicio de generación de carteles PDF.
/// 
/// Usa un diseño base A4 horizontal y lo escala en diferentes
/// disposiciones:
/// - A3: 1 cartel en hoja A3 horizontal.
/// - A4: 1 cartel en hoja A4 horizontal.
/// - A5: 2 carteles en hoja A4 vertical.
/// - A6: 4 carteles en hoja A4 horizontal.
/// </summary>
public sealed class PosterPdfRenderService
{
    private readonly PosterRenderOptions _options;

    private static readonly CultureInfo SpanishCulture =
        new("es-ES");

    private const string PosterFontFamily = "Times New Roman";

    private static readonly double BasePosterWidth =
        Mm(297);

    private static readonly double BasePosterHeight =
        Mm(210);

    private sealed class PosterPageLayout
    {
        public List<XRect> Slots { get; set; } = [];

        /// <summary>
        /// Dibuja un lienzo horizontal dentro de una página vertical.
        /// Se usa para evitar que Adobe/driver reduzca el PDF horizontal
        /// al imprimir silenciosamente.
        /// </summary>
        public bool RotateLandscapeCanvasOnPortraitPage { get; set; }
    }

    private sealed class MixedSlotAssignment
    {
        public PosterRenderItem Item { get; set; } = new();

        public XRect Slot { get; set; }

        public bool RotateInsideSlot { get; set; }
    }

    public PosterPdfRenderService(
        IOptions<PosterRenderOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Método mantenido para compatibilidad con el endpoint de prueba.
    /// </summary>
    public byte[] RenderBasicA4Poster(
        PosterProductDto product)
    {
        return RenderPoster(
            product,
            PosterSize.A4,
            PosterPriceType.Normal
        );
    }

    /// <summary>
    /// Renderiza el cartel según el tamaño solicitado
    /// y el tipo de precio/oferta.
    /// </summary>
    public byte[] RenderPoster(
        PosterProductDto product,
        PosterSize size,
        PosterPriceType priceType)
    {
        using var document = new PdfDocument();

        document.Info.Title =
            $"Cartel producto {product.Code} - {size} - {priceType}";

        var page = document.AddPage();

        var layout = ConfigurePageAndSlots(
            page,
            size
        );

        using var gfx = XGraphics.FromPdfPage(page);

        var state = gfx.Save();

        if (layout.RotateLandscapeCanvasOnPortraitPage)
        {
            gfx.TranslateTransform(
                0,
                page.Height.Point
            );

            gfx.RotateTransform(
                -90
            );
        }

        foreach (var slot in layout.Slots)
        {
            DrawPosterSlot(
                gfx,
                slot,
                product,
                priceType
            );
        }

        gfx.Restore(state);

        using var stream = new MemoryStream();

        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }

    /// <summary>
    /// Configura el tamaño real de página y devuelve las zonas
    /// donde se dibujará el cartel.
    /// </summary>
    private static PosterPageLayout ConfigurePageAndSlots(
        PdfPage page,
        PosterSize size)
    {
        switch (size)
        {
            case PosterSize.A3:
                page.Size = PdfSharp.PageSize.A3;
                page.Orientation = PdfSharp.PageOrientation.Landscape;

                return new PosterPageLayout
                {
                    Slots =
                    [
                        new XRect(
                            0,
                            0,
                            page.Width.Point,
                            page.Height.Point
                        )
                    ]
                };

            case PosterSize.A5:
                page.Size = PdfSharp.PageSize.A4;
                page.Orientation = PdfSharp.PageOrientation.Portrait;

                return new PosterPageLayout
                {
                    Slots =
                    [
                        new XRect(
                            0,
                            0,
                            page.Width.Point,
                            page.Height.Point / 2
                        ),
                        new XRect(
                            0,
                            page.Height.Point / 2,
                            page.Width.Point,
                            page.Height.Point / 2
                        )
                    ]
                };

            case PosterSize.A6:
                {
                    // IMPORTANTE:
                    // Aunque queremos resultado visual A4 horizontal,
                    // generamos una página A4 vertical y rotamos el contenido.
                    // Así evitamos que Adobe/driver lo imprima reducido en media hoja.
                    page.Size = PdfSharp.PageSize.A4;
                    page.Orientation = PdfSharp.PageOrientation.Portrait;

                    var virtualLandscapeWidth =
                        XUnit.FromMillimeter(297).Point;

                    var virtualLandscapeHeight =
                        XUnit.FromMillimeter(210).Point;

                    var halfWidth =
                        virtualLandscapeWidth / 2;

                    var halfHeight =
                        virtualLandscapeHeight / 2;

                    return new PosterPageLayout
                    {
                        RotateLandscapeCanvasOnPortraitPage = true,
                        Slots =
                        [
                            new XRect(
                                0,
                                0,
                                halfWidth,
                                halfHeight
                            ),
                            new XRect(
                                halfWidth,
                                0,
                                halfWidth,
                                halfHeight
                            ),
                            new XRect(
                                0,
                                halfHeight,
                                halfWidth,
                                halfHeight
                            ),
                            new XRect(
                                halfWidth,
                                halfHeight,
                                halfWidth,
                                halfHeight
                            )
                                        ]
                                    };
                }

            case PosterSize.A4:
            default:
                {
                    // IMPORTANTE:
                    // Aunque el cartel A4 debe ser visualmente horizontal,
                    // generamos una página A4 vertical y rotamos el contenido.
                    //
                    // Esto evita que Adobe/driver imprima el PDF reducido o en vertical
                    // cuando se lanza en modo silencioso desde la aplicación.
                    page.Size = PdfSharp.PageSize.A4;
                    page.Orientation = PdfSharp.PageOrientation.Portrait;

                    var virtualLandscapeWidth =
                        XUnit.FromMillimeter(297).Point;

                    var virtualLandscapeHeight =
                        XUnit.FromMillimeter(210).Point;

                    return new PosterPageLayout
                    {
                        RotateLandscapeCanvasOnPortraitPage = true,
                        Slots =
                        [
                            new XRect(
                                0,
                                0,
                                virtualLandscapeWidth,
                                virtualLandscapeHeight
                            )
                        ]
                    };
                }
        }
    }

    /// <summary>
    /// Dibuja el cartel base dentro de un slot concreto,
    /// escalándolo proporcionalmente.
    /// </summary>
    private void DrawPosterSlot(
        XGraphics gfx,
        XRect slot,
        PosterProductDto product,
        PosterPriceType priceType)
    {
        var scaleX =
            slot.Width / BasePosterWidth;

        var scaleY =
            slot.Height / BasePosterHeight;

        var scale =
            Math.Min(scaleX, scaleY);

        var scaledWidth =
            BasePosterWidth * scale;

        var scaledHeight =
            BasePosterHeight * scale;

        var offsetX =
            slot.Left + ((slot.Width - scaledWidth) / 2);

        var offsetY =
            slot.Top + ((slot.Height - scaledHeight) / 2);

        var state = gfx.Save();

        gfx.TranslateTransform(
            offsetX,
            offsetY
        );

        gfx.ScaleTransform(
            scale
        );

        DrawBusinessPosterBase(
            gfx,
            product,
            priceType
        );

        gfx.Restore(state);
    }

    /// <summary>
    /// Dibuja el cartel en coordenadas base A4 horizontal:
    /// 297mm x 210mm.
    /// </summary>
    private void DrawBusinessPosterBase(
        XGraphics gfx,
        PosterProductDto product,
        PosterPriceType priceType)
    {
        var black = XBrushes.Black;

        var red = new XSolidBrush(
            XColor.FromArgb(230, 0, 0)
        );

        if (IsBundleOffer(priceType))
        {
            DrawBundleOfferPoster(
                gfx,
                product,
                priceType,
                black,
                red
            );

            return;
        }

        DrawStandardPoster(
            gfx,
            product,
            priceType,
            black,
            red
        );
    }

    private static void DrawProductHeader(
        XGraphics gfx,
        XRect headerRect,
        PosterProductDto product)
    {
        var productLines =
            BuildProductLines(product.Name);

        var fontSize =
            GetFittingFontSizeForLines(
                gfx,
                productLines,
                PosterFontFamily,
                XFontStyleEx.Bold,
                maxFontSize: 48,
                minFontSize: 28,
                availableWidth: headerRect.Width - Mm(12),
                availableHeight: headerRect.Height
            );

        var font = new XFont(
            PosterFontFamily,
            fontSize,
            XFontStyleEx.Bold
        );

        var lineHeight =
            fontSize * 1.12;

        var totalTextHeight =
            productLines.Count * lineHeight;

        var startY =
            headerRect.Top +
            ((headerRect.Height - totalTextHeight) / 2);

        for (var i = 0; i < productLines.Count; i++)
        {
            var lineRect = new XRect(
                headerRect.Left + Mm(6),
                startY + (i * lineHeight),
                headerRect.Width - Mm(12),
                lineHeight
            );

            gfx.DrawString(
                productLines[i],
                font,
                XBrushes.Black,
                lineRect,
                XStringFormats.Center
            );
        }
    }

    private static void DrawMainPrice(
        XGraphics gfx,
        PosterProductDto product,
        XBrush brush,
        double y,
        double fontSize,
        double? rectHeight = null,
        double xOffsetMm = 0)
    {
        var priceText =
            product.Price.HasValue
                ? FormatPrice(product.Price.Value) + " €"
                : "SIN PRECIO";

        var rect = new XRect(
            Mm(8 + xOffsetMm),
            y,
            BasePosterWidth - Mm(16),
            rectHeight ?? Mm(95)
        );

        var finalFontSize =
            GetFittingFontSizeForSingleLine(
                gfx,
                priceText,
                PosterFontFamily,
                XFontStyleEx.Bold,
                maxFontSize: fontSize,
                minFontSize: 90,
                availableWidth: rect.Width,
                availableHeight: rect.Height
            );

        var priceFont = new XFont(
            PosterFontFamily,
            finalFontSize,
            XFontStyleEx.Bold
        );

        gfx.DrawString(
            priceText,
            priceFont,
            brush,
            rect,
            XStringFormats.Center
        );
    }

    private void DrawVatIncludedPrice(
        XGraphics gfx,
        PosterProductDto product,
        XBrush brush,
        decimal? basePrice,
        double y,
        double fontSize)
    {
        if (!basePrice.HasValue)
            return;

        var vatIncludedPrice =
            CalculateVatIncludedPrice(
                basePrice.Value,
                product.VatCode
            );

        var vatText =
            $"{FormatPrice(vatIncludedPrice)} € IVA inc.";

        var vatFont = new XFont(
            PosterFontFamily,
            fontSize,
            XFontStyleEx.Bold
        );

        var rect = new XRect(
            Mm(135),
            y,
            BasePosterWidth - Mm(150),
            Mm(26)
        );

        gfx.DrawString(
            vatText,
            vatFont,
            brush,
            rect,
            XStringFormats.CenterRight
        );
    }

    private void DrawLogo(
        XGraphics gfx)
    {
        var logoRect = new XRect(
            Mm(18),
            Mm(153),
            Mm(32),
            Mm(32)
        );

        try
        {
            if (!string.IsNullOrWhiteSpace(_options.LogoPath) &&
                File.Exists(_options.LogoPath))
            {
                using var logo =
                    XImage.FromFile(_options.LogoPath);

                gfx.DrawImage(
                    logo,
                    logoRect
                );

                return;
            }
        }
        catch
        {
            // Si falla el logo, dibujamos un marcador sencillo.
        }

        var placeholderFont = new XFont(
            PosterFontFamily,
            9,
            XFontStyleEx.Bold
        );

        gfx.DrawEllipse(
            XPens.DarkGreen,
            logoRect
        );

        gfx.DrawString(
            "SUPERMERCADO",
            placeholderFont,
            XBrushes.DarkGreen,
            logoRect,
            XStringFormats.Center
        );
    }

    private decimal CalculateVatIncludedPrice(
        decimal basePrice,
        string? vatCode)
    {
        if (string.IsNullOrWhiteSpace(vatCode))
            return Math.Round(basePrice, 2);

        if (!_options.VatRatesByCode.TryGetValue(
                vatCode,
                out var vatRate))
        {
            return Math.Round(basePrice, 2);
        }

        var finalPrice =
            basePrice * (1 + (vatRate / 100m));

        return Math.Round(
            finalPrice,
            2,
            MidpointRounding.AwayFromZero
        );
    }

    private static List<string> BuildProductLines(
        string? productName)
    {
        var text =
            string.IsNullOrWhiteSpace(productName)
                ? "PRODUCTO SIN DESCRIPCIÓN"
                : productName.Trim().ToUpperInvariant();

        text = text
            .Replace("  ", " ")
            .Replace(".", " ");

        var words = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (words.Count <= 3)
            return [string.Join(' ', words)];

        var lastWord =
            words[^1];

        var looksLikeFormat =
            lastWord.Contains('/') ||
            lastWord.Contains("GR") ||
            lastWord.Contains("ML") ||
            lastWord.Contains("KG") ||
            lastWord.Contains("L");

        if (looksLikeFormat)
        {
            var firstLine =
                string.Join(
                    ' ',
                    words.Take(words.Count - 1)
                );

            return
            [
                firstLine,
                lastWord
            ];
        }

        var middle =
            (int)Math.Ceiling(words.Count / 2d);

        return
        [
            string.Join(
                ' ',
                words.Take(middle)
            ),
            string.Join(
                ' ',
                words.Skip(middle)
            )
        ];
    }

    private static string FormatPrice(
        decimal price)
    {
        return price.ToString(
            "0.00",
            SpanishCulture
        );
    }

    private static double GetFittingFontSizeForLines(
    XGraphics gfx,
    List<string> lines,
    string fontFamily,
    XFontStyleEx fontStyle,
    double maxFontSize,
    double minFontSize,
    double availableWidth,
    double availableHeight)
    {
        for (var size = maxFontSize; size >= minFontSize; size -= 1)
        {
            var font = new XFont(
                fontFamily,
                size,
                fontStyle
            );

            var lineHeight =
                size * 1.12;

            var totalHeight =
                lines.Count * lineHeight;

            if (totalHeight > availableHeight)
                continue;

            var allLinesFit =
                lines.All(line =>
                    gfx.MeasureString(line, font).Width <= availableWidth
                );

            if (allLinesFit)
                return size;
        }

        return minFontSize;
    }

    private static double GetFittingFontSizeForSingleLine(
        XGraphics gfx,
        string text,
        string fontFamily,
        XFontStyleEx fontStyle,
        double maxFontSize,
        double minFontSize,
        double availableWidth,
        double availableHeight)
    {
        for (var size = maxFontSize; size >= minFontSize; size -= 2)
        {
            var font = new XFont(
                fontFamily,
                size,
                fontStyle
            );

            var measured =
                gfx.MeasureString(
                    text,
                    font
                );

            if (measured.Width <= availableWidth &&
                measured.Height <= availableHeight * 1.25)
            {
                return size;
            }
        }

        return minFontSize;
    }

    private static double Mm(
        double value)
    {
        return XUnit.FromMillimeter(value).Point;
    }

    //METODOS AUXILIARES DE OFERTAS
    private static bool IsBundleOffer(
    PosterPriceType priceType)
    {
        return priceType is
            PosterPriceType.Oferta1Mas1 or
            PosterPriceType.Oferta2Mas1 or
            PosterPriceType.Oferta3Mas1 or
            PosterPriceType.Oferta4Mas1 or
            PosterPriceType.Oferta5Mas1;
    }

    private static string? GetOfferTitle(
        PosterPriceType priceType)
    {
        return priceType switch
        {
            PosterPriceType.Novedad => "¡NOVEDAD!",
            PosterPriceType.SuperOferta => "SUPER OFERTA",
            PosterPriceType.Oferta1Mas1 => "OFERTA 1+1",
            PosterPriceType.Oferta2Mas1 => "OFERTA 2+1",
            PosterPriceType.Oferta3Mas1 => "OFERTA 3+1",
            PosterPriceType.Oferta4Mas1 => "OFERTA 4+1",
            PosterPriceType.Oferta5Mas1 => "OFERTA 5+1",
            _ => null
        };
    }

    private static int GetBundlePayQuantity(
        PosterPriceType priceType)
    {
        return priceType switch
        {
            PosterPriceType.Oferta1Mas1 => 1,
            PosterPriceType.Oferta2Mas1 => 2,
            PosterPriceType.Oferta3Mas1 => 3,
            PosterPriceType.Oferta4Mas1 => 4,
            PosterPriceType.Oferta5Mas1 => 5,
            _ => 1
        };
    }

    private static decimal CalculateBundleUnitPrice(
        decimal basePrice,
        PosterPriceType priceType)
    {
        var payQuantity =
            GetBundlePayQuantity(priceType);

        var totalQuantity =
            payQuantity + 1;

        return Math.Round(
            basePrice * payQuantity / totalQuantity,
            2,
            MidpointRounding.AwayFromZero
        );
    }

    //HACE LAS LLAMADAS DE PINTURA PARA POSTER NORMAL
    private void DrawStandardPoster(
    XGraphics gfx,
    PosterProductDto product,
    PosterPriceType priceType,
    XBrush black,
    XBrush red)
    {
        var hasOfferTitle =
            priceType != PosterPriceType.Normal;

        DrawOfferTitle(
            gfx,
            priceType,
            red
        );

        var headerRect = new XRect(
            Mm(10),
            hasOfferTitle ? Mm(24) : Mm(10),
            BasePosterWidth - Mm(20),
            hasOfferTitle ? Mm(55) : Mm(66)
        );

        DrawProductHeader(
            gfx,
            headerRect,
            product
        );

        // Y = 50 mm (Cuanto mas alto mas abajo)
        // Fuente = 112
        // Altura = 55 mm
        // Precio normal en negro.
        DrawMainPrice(
            gfx,
            product,
            black,
            hasOfferTitle ? Mm(78) : Mm(62),
            200
        );

        DrawLogo(
            gfx
        );

        DrawVatIncludedPrice(
            gfx,
            product,
            red,
            product.Price,
            Mm(165),
            36
        );
    }


    //HACE LAS LLAMADAS DE PINTURA DE LAS OFERTAS
    private void DrawBundleOfferPoster(
    XGraphics gfx,
    PosterProductDto product,
    PosterPriceType priceType,
    XBrush black,
    XBrush red)
    {
        DrawOfferTitle(
            gfx,
            priceType,
            red
        );

        var headerRect = new XRect(
            Mm(10),
            Mm(24),
            BasePosterWidth - Mm(20),
            Mm(44)
        );

        DrawProductHeader(
            gfx,
            headerRect,
            product
        );

        // Y = 50 mm (Cuanto mas alto mas abajo)
        // Fuente = 112
        // Altura = 55 mm
        // Precio normal en negro.
        // En ofertas X+1 lo subimos para dejar espacio al texto promocional.
        DrawMainPrice(
            gfx,
            product,
            black,
            Mm(55),
            112,
            Mm(55),
            xOffsetMm: 0
        );

        // IVA del precio normal a la derecha.
        // También lo subimos ligeramente para que acompañe al precio normal.
        DrawVatIncludedPrice(
            gfx,
            product,
            red,
            product.Price,
            Mm(100),
            28
        );

        DrawPromotionText(
            gfx,
            red
        );

        if (product.Price.HasValue)
        {
            var promoUnitPrice =
                CalculateBundleUnitPrice(
                    product.Price.Value,
                    priceType
                );

            DrawPromotionPrice(
                gfx,
                promoUnitPrice,
                red
            );

            DrawVatIncludedPrice(
                gfx,
                product,
                red,
                promoUnitPrice,
                Mm(177),
                28
            );
        }

        DrawLogo(
            gfx
        );
    }

    //AÑADE TITULO DE OFERTA
    private static void DrawOfferTitle(
    XGraphics gfx,
    PosterPriceType priceType,
    XBrush red)
    {
        var title =
            GetOfferTitle(priceType);

        if (string.IsNullOrWhiteSpace(title))
            return;

        var font = new XFont(
            PosterFontFamily,
            46,
            XFontStyleEx.Bold
        );

        var rect = new XRect(
            Mm(8),
            Mm(2),
            BasePosterWidth - Mm(16),
            Mm(22)
        );

        gfx.DrawString(
            title,
            font,
            red,
            rect,
            XStringFormats.TopCenter
        );

        var measured =
            gfx.MeasureString(
                title,
                font
            );

        var centerX =
            BasePosterWidth / 2;

        var underlineY =
            Mm(22);

        var pen = new XPen(
            XColor.FromArgb(230, 0, 0),
            1.6
        );

        gfx.DrawLine(
            pen,
            centerX - (measured.Width / 2),
            underlineY,
            centerX + (measured.Width / 2),
            underlineY
        );
    }

    //AÑADEN TEXTO PROMOCIALAR Y PRECIO PROMOCIONAL
    private static void DrawPromotionText(
    XGraphics gfx,
    XBrush red)
    {
        var font = new XFont(
            PosterFontFamily,
            28,
            XFontStyleEx.Bold
        );

        var rect = new XRect(
            Mm(8),
            Mm(128),
            BasePosterWidth - Mm(16),
            Mm(18)
        );

        gfx.DrawString(
            "CON LA PROMOCIÓN LA UNIDAD SALE A",
            font,
            red,
            rect,
            XStringFormats.Center
        );
    }

    
    private static void DrawPromotionPrice(
    XGraphics gfx,
    decimal promoUnitPrice,
    XBrush red)
    {
        var priceText =
            FormatPrice(promoUnitPrice) + "€";

        var rect = new XRect(
            Mm(8),
            Mm(145),
            BasePosterWidth - Mm(16),
            Mm(45)
        );

        var finalFontSize =
            GetFittingFontSizeForSingleLine(
                gfx,
                priceText,
                PosterFontFamily,
                XFontStyleEx.Bold,
                maxFontSize: 110,
                minFontSize: 70,
                availableWidth: rect.Width,
                availableHeight: rect.Height
            );

        var font = new XFont(
            PosterFontFamily,
            finalFontSize,
            XFontStyleEx.Bold
        );

        gfx.DrawString(
            priceText,
            font,
            red,
            rect,
            XStringFormats.Center
        );
    }

    /// <summary>
    /// Renderiza un PDF con varios productos.
    ///
    /// Comportamiento:
    /// - A3: 1 producto por hoja.
    /// - A4: 1 producto por hoja.
    /// - A5: 2 productos por hoja.
    /// - A6: 4 productos por hoja.
    ///
    /// Si una página queda incompleta, se dejan huecos vacíos.
    /// Ejemplo:
    /// - 3 productos en A6 => 3/4 de una hoja.
    /// - 6 productos en A6 => 1 hoja completa + media hoja.
    /// </summary>
    public byte[] RenderPosters(
        IReadOnlyList<PosterProductDto> products,
        PosterSize size,
        PosterPriceType priceType)
    {
        using var document = new PdfDocument();

        document.Info.Title =
            $"Carteles {size} - {priceType} - {products.Count} productos";

        var productIndex = 0;

        while (productIndex < products.Count)
        {
            var page = document.AddPage();

            var layout = ConfigurePageAndSlots(
                page,
                size
            );

            using var gfx =
                XGraphics.FromPdfPage(page);

            var state =
                gfx.Save();

            if (layout.RotateLandscapeCanvasOnPortraitPage)
            {
                gfx.TranslateTransform(
                    0,
                    page.Height.Point
                );

                gfx.RotateTransform(
                    -90
                );
            }

            foreach (var slot in layout.Slots)
            {
                if (productIndex >= products.Count)
                    break;

                DrawPosterSlot(
                    gfx,
                    slot,
                    products[productIndex],
                    priceType
                );

                productIndex++;
            }

            gfx.Restore(state);
        }

        using var stream =
            new MemoryStream();

        document.Save(
            stream,
            closeStream: false
        );

        return stream.ToArray();
    }

    /// <summary>
    /// Renderiza un PDF mixto.
    ///
    /// Reglas:
    /// - A3: una hoja A3 completa.
    /// - A4: una hoja A4 completa.
    /// - A5 y A6 se combinan en hojas A4 para optimizar papel.
    /// </summary>
    public byte[] RenderMixedPosters(
        IReadOnlyList<PosterRenderItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var document = new PdfDocument();

        document.Info.Title =
            $"Carteles mixtos - {items.Count} artículos";

        var mixedItems = new List<PosterRenderItem>();

        foreach (var item in items)
        {
            if (item is null)
                continue;

            if (item.Size == PosterSize.A3 ||
                item.Size == PosterSize.A4)
            {
                RenderSingleItemPage(
                    document,
                    item
                );

                continue;
            }

            mixedItems.Add(item);
        }

        RenderMixedA5A6Pages(
            document,
            mixedItems
        );

        using var stream =
            new MemoryStream();

        document.Save(
            stream,
            closeStream: false
        );

        return stream.ToArray();
    }

    private void RenderSingleItemPage(
        PdfDocument document,
        PosterRenderItem item)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(item);

        var page =
            document.AddPage();

        var layout =
            ConfigurePageAndSlots(
                page,
                item.Size
            );

        using var gfx =
            XGraphics.FromPdfPage(page);

        var state =
            gfx.Save();

        if (layout.RotateLandscapeCanvasOnPortraitPage)
        {
            gfx.TranslateTransform(
                0,
                page.Height.Point
            );

            gfx.RotateTransform(
                -90
            );
        }

        var slot =
            layout.Slots[0];

        DrawPosterSlot(
            gfx,
            slot,
            item.Product,
            item.PriceType
        );

        gfx.Restore(state);
    }

    private void RenderMixedA5A6Pages(
        PdfDocument document,
        IReadOnlyList<PosterRenderItem> mixedItems)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mixedItems);

        var pending =
            new Queue<PosterRenderItem>(
                mixedItems
                    .Where(item => item is not null)
            );

        while (pending.Count > 0)
        {
            var page =
                document.AddPage();

            page.Size = PdfSharp.PageSize.A4;
            page.Orientation = PdfSharp.PageOrientation.Portrait;

            using var gfx =
                XGraphics.FromPdfPage(page);

            var state =
                gfx.Save();

            gfx.TranslateTransform(
                0,
                page.Height.Point
            );

            gfx.RotateTransform(
                -90
            );

            var assignments =
                BuildMixedA5A6Assignments(
                    pending
                );

            foreach (var assignment in assignments)
            {
                if (assignment.RotateInsideSlot)
                {
                    DrawPosterSlotRotated(
                        gfx,
                        assignment.Slot,
                        assignment.Item.Product,
                        assignment.Item.PriceType
                    );
                }
                else
                {
                    DrawPosterSlot(
                        gfx,
                        assignment.Slot,
                        assignment.Item.Product,
                        assignment.Item.PriceType
                    );
                }
            }

            gfx.Restore(state);
        }
    }

    private static List<MixedSlotAssignment> BuildMixedA5A6Assignments(
        Queue<PosterRenderItem> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        var assignments =
            new List<MixedSlotAssignment>();

        var cellWidth =
            XUnit.FromMillimeter(148.5).Point;

        var cellHeight =
            XUnit.FromMillimeter(105).Point;

        var occupied =
            new bool[2, 2];

        while (pending.Count > 0)
        {
            var item =
                pending.Peek();

            if (item.Size == PosterSize.A5)
            {
                var placed = false;

                for (var column = 0; column < 2; column++)
                {
                    if (occupied[column, 0] ||
                        occupied[column, 1])
                    {
                        continue;
                    }

                    pending.Dequeue();

                    occupied[column, 0] = true;
                    occupied[column, 1] = true;

                    assignments.Add(new MixedSlotAssignment
                    {
                        Item = item,
                        Slot = new XRect(
                            column * cellWidth,
                            0,
                            cellWidth,
                            cellHeight * 2
                        ),
                        RotateInsideSlot = true
                    });

                    placed = true;
                    break;
                }

                if (!placed)
                    break;

                continue;
            }

            if (item.Size == PosterSize.A6)
            {
                var placed = false;

                for (var row = 0; row < 2; row++)
                {
                    for (var column = 0; column < 2; column++)
                    {
                        if (occupied[column, row])
                            continue;

                        pending.Dequeue();

                        occupied[column, row] = true;

                        assignments.Add(new MixedSlotAssignment
                        {
                            Item = item,
                            Slot = new XRect(
                                column * cellWidth,
                                row * cellHeight,
                                cellWidth,
                                cellHeight
                            ),
                            RotateInsideSlot = false
                        });

                        placed = true;
                        break;
                    }

                    if (placed)
                        break;
                }

                if (!placed)
                    break;

                continue;
            }

            pending.Dequeue();
        }

        return assignments;
    }

    private void DrawPosterSlotRotated(
    XGraphics gfx,
    XRect slot,
    PosterProductDto product,
    PosterPriceType priceType)
    {
        var scaleX =
            slot.Width / BasePosterHeight;

        var scaleY =
            slot.Height / BasePosterWidth;

        var scale =
            Math.Min(
                scaleX,
                scaleY
            );

        var scaledWidth =
            BasePosterHeight * scale;

        var scaledHeight =
            BasePosterWidth * scale;

        var offsetX =
            slot.Left + ((slot.Width - scaledWidth) / 2);

        var offsetY =
            slot.Top + ((slot.Height - scaledHeight) / 2);

        var state =
            gfx.Save();

        gfx.TranslateTransform(
            offsetX,
            offsetY + scaledHeight
        );

        gfx.RotateTransform(
            -90
        );

        gfx.ScaleTransform(
            scale
        );

        DrawBusinessPosterBase(
            gfx,
            product,
            priceType
        );

        gfx.Restore(state);
    }
}