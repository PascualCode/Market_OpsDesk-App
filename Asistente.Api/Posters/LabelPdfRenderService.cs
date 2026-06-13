using Asistente.Api.Configuration;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Globalization;

namespace Asistente.Api.Posters;

/// <summary>
/// Servicio de generación PDF para etiquetas de precios.
///
/// Formato base:
/// - Hoja A4 vertical.
/// - 2 columnas.
/// - 7 filas.
/// - 14 etiquetas por página.
/// </summary>
public sealed class LabelPdfRenderService
{
    private readonly PosterRenderOptions _options;

    private static readonly CultureInfo SpanishCulture =
        new("es-ES");

    private const string LabelFontFamily = "Times New Roman";

    public LabelPdfRenderService(
        IOptions<PosterRenderOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Genera un PDF con las etiquetas seleccionadas.
    /// </summary>
    public byte[] RenderLabels(
        IReadOnlyList<PendingLabelDto> labels,
        LabelFormat format)
    {
        using var document = new PdfDocument();

        document.Info.Title =
            $"Etiquetas pendientes - {labels.Count} etiquetas";

        var labelsPerPage = 14;
        var labelIndex = 0;

        while (labelIndex < labels.Count)
        {
            var page = document.AddPage();

            page.Size = PdfSharp.PageSize.A4;
            page.Orientation = PdfSharp.PageOrientation.Portrait;

            using var gfx =
                XGraphics.FromPdfPage(page);

            var slots =
                BuildA4LabelSlots(
                    page,
                    format
                );

            foreach (var slot in slots)
            {
                if (labelIndex >= labels.Count)
                    break;

                DrawLabel(
                    gfx,
                    slot,
                    labels[labelIndex],
                    format
                );

                labelIndex++;
            }
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
    /// Genera las posiciones de las etiquetas en A4.
    ///
    /// Medida ajustada al formato real de etiqueta:
    /// - Ancho aproximado: 86 mm.
    /// - Alto aproximado: 35 mm.
    /// - 2 columnas.
    /// - 7 filas.
    /// </summary>
    /// <summary>
    /// Configuración visual de una plantilla de etiquetas.
    /// </summary>
    private sealed class LabelLayoutDefinition
    {
        public double LabelWidth { get; set; }

        public double LabelHeight { get; set; }

        public double MarginLeft { get; set; }

        public double MarginTop { get; set; }

        public double ColumnGap { get; set; }

        public double RowGap { get; set; }

        public int Columns { get; set; }

        public int Rows { get; set; }
    }

    /// <summary>
    /// Devuelve la configuración de tamaño y distribución
    /// según el formato de etiqueta.
    /// 
    /// IMPORTANTE:
    /// Si ya has ajustado el formato Normal a tu gusto,
    /// copia aquí exactamente tus valores actuales de Normal.
    /// </summary>
    private static LabelLayoutDefinition GetLabelLayout(
        LabelFormat format)
    {
        return format switch
        {
            LabelFormat.Reducido => new LabelLayoutDefinition
            {
                // Aproximado según referencia: 66 mm x 33 mm.
                LabelWidth = Mm(66),
                LabelHeight = Mm(33),

                MarginLeft = Mm(5),
                MarginTop = Mm(5),

                ColumnGap = Mm(2),
                RowGap = Mm(2),

                // 3 columnas caben en A4 vertical.
                Columns = 3,
                Rows = 8
            },

            LabelFormat.SuperReducido => new LabelLayoutDefinition
            {
                // Aproximado según referencia: 43 mm x 32 mm.
                LabelWidth = Mm(43),
                LabelHeight = Mm(32),

                MarginLeft = Mm(5),
                MarginTop = Mm(5),

                ColumnGap = Mm(2),
                RowGap = Mm(2),

                // 4 columnas caben cómodamente en A4 vertical.
                Columns = 4,
                Rows = 8
            },

            _ => new LabelLayoutDefinition
            {
                // FORMATO NORMAL.
                // Cambia estos valores por los que ya ajustaste manualmente.
                LabelWidth = Mm(83),
                LabelHeight = Mm(34),

                MarginLeft = Mm(5),
                MarginTop = Mm(5),

                ColumnGap = Mm(2),
                RowGap = Mm(2),

                Columns = 2,
                Rows = 7
            }
        };
    }

    /// <summary>
    /// Genera las posiciones de las etiquetas en A4
    /// según el formato seleccionado.
    /// </summary>
    private static List<XRect> BuildA4LabelSlots(
        PdfPage page,
        LabelFormat format)
    {
        var layout =
            GetLabelLayout(format);

        var slots =
            new List<XRect>();

        for (var row = 0; row < layout.Rows; row++)
        {
            for (var column = 0; column < layout.Columns; column++)
            {
                var x =
                    layout.MarginLeft +
                    (column * (layout.LabelWidth + layout.ColumnGap));

                var y =
                    layout.MarginTop +
                    (row * (layout.LabelHeight + layout.RowGap));

                if (x + layout.LabelWidth > page.Width.Point ||
                    y + layout.LabelHeight > page.Height.Point)
                {
                    continue;
                }

                slots.Add(
                    new XRect(
                        x,
                        y,
                        layout.LabelWidth,
                        layout.LabelHeight
                    )
                );
            }
        }

        return slots;
    }

    /// <summary>
    /// Dibuja una etiqueta individual.
    /// </summary>
    private void DrawLabel(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label,
        LabelFormat format)
    {
        var blackPen =
            new XPen(XColors.Black, 0.8);

        gfx.DrawRectangle(
            blackPen,
            rect
        );

        DrawDescription(
            gfx,
            rect,
            label,
            format
        );

        DrawMainPrice(
            gfx,
            rect,
            label,
            format
        );

        DrawVatText(
            gfx,
            rect,
            label,
            format
        );

        DrawBarcode(
            gfx,
            rect,
            label,
            format
        );

        DrawUnitPriceText(
            gfx,
            rect,
            label
        );

        DrawVatIncludedPrice(
            gfx,
            rect,
            label,
            format
        );
    }

    /// <summary>
    /// Dibuja la descripción superior.
    /// </summary>
    private static void DrawDescription(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label,
        LabelFormat format)
    {
        var lines =
            BuildDescriptionLines(
                label.ArticleDescription,
                format
            );

        var maxFontSize =
            format switch
            {
                LabelFormat.SuperReducido => 6.8,
                LabelFormat.Reducido => 8.2,
                _ => 9.8
            };

        var minFontSize =
            format switch
            {
                LabelFormat.SuperReducido => 4.8,
                LabelFormat.Reducido => 6.0,
                _ => 6.5
            };

        var availableHeight =
            format switch
            {
                LabelFormat.SuperReducido => Mm(10),
                LabelFormat.Reducido => Mm(10),
                _ => Mm(10)
            };

        var fontSize =
            GetFittingFontSizeForLines(
                gfx,
                lines,
                LabelFontFamily,
                XFontStyleEx.Bold,
                maxFontSize: maxFontSize,
                minFontSize: minFontSize,
                availableWidth: rect.Width - Mm(4),
                availableHeight: availableHeight
            );

        var font = new XFont(
            LabelFontFamily,
            fontSize,
            XFontStyleEx.Bold
        );

        var lineHeight =
            fontSize * 1.05;

        var startY =
            rect.Top + Mm(1.3);

        for (var i = 0; i < lines.Count; i++)
        {
            var lineRect = new XRect(
                rect.Left + Mm(2),
                startY + (i * lineHeight),
                rect.Width - Mm(4),
                lineHeight
            );

            gfx.DrawString(
                lines[i],
                font,
                XBrushes.Black,
                lineRect,
                XStringFormats.Center
            );
        }
    }

    /// <summary>
    /// Precio principal sin IVA.
    /// </summary>
    private static void DrawMainPrice(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label,
        LabelFormat format)
    {
        var priceText =
            $"{FormatPrice(label.Price)} €";

        var yOffset =
            format switch
            {
                LabelFormat.SuperReducido => GetSuperReducedYOffset(format) - Mm(1),
                LabelFormat.Reducido => -Mm(1),
                LabelFormat.Normal => -Mm(1),
                _ => 0
            };

        var maxFontSize =
            format switch
            {
                LabelFormat.SuperReducido => 24,
                LabelFormat.Reducido => 28,
                _ => 30
            };

        var minFontSize =
            format switch
            {
                LabelFormat.SuperReducido => 16,
                LabelFormat.Reducido => 18,
                _ => 18
            };

        var priceRect = new XRect(
            rect.Left + Mm(20),
            rect.Top + Mm(10) + yOffset,
            rect.Width - Mm(40),
            Mm(16)
        );

        var finalFontSize =
            GetFittingFontSizeForSingleLine(
                gfx,
                priceText,
                LabelFontFamily,
                XFontStyleEx.Bold,
                maxFontSize: maxFontSize,
                minFontSize: minFontSize,
                availableWidth: priceRect.Width,
                availableHeight: priceRect.Height
            );

        var font = new XFont(
            LabelFontFamily,
            finalFontSize,
            XFontStyleEx.Bold
        );

        gfx.DrawString(
            priceText,
            font,
            XBrushes.Black,
            priceRect,
            XStringFormats.Center
        );
    }

    /// <summary>
    /// Texto pequeño de IVA, por ejemplo + 10% IVA.
    /// </summary>
    private void DrawVatText(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label,
        LabelFormat format)
    {
        var vatRate =
            GetVatRate(label.VatCode);

        var text =
            $"+ {vatRate:0}% IVA";

        var font = new XFont(
            LabelFontFamily,
            7,
            XFontStyleEx.Bold
        );

        var yOffset =
            GetSuperReducedYOffset(format);

        var vatRect = new XRect(
            rect.Left + rect.Width - Mm(23),
            rect.Top + Mm(20) + yOffset,
            Mm(21),
            Mm(5)
        );

        var brush =
            IsReducedLabelFormat(format)
                ? GetReducedRedBrush()
                : XBrushes.Black;

        gfx.DrawString(
            text,
            font,
            brush,
            vatRect,
            XStringFormats.CenterRight
        );
    }

    /// <summary>
    /// Dibuja el código de barras usando el código EAN de la consulta.
    ///
    /// Se usa:
    /// - Return_CodBarraAux si tiene valor.
    /// - CodBarra si el auxiliar viene vacío.
    /// </summary>
    private static void DrawBarcode(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label,
        LabelFormat format)
    {
        var code =
            OnlyDigits(label.EffectiveBarcode);

        var yOffset = 1 +
            GetSuperReducedYOffset(format);

        var barcodeWidth =
            format switch
            {
                LabelFormat.Reducido => Mm(17),
                LabelFormat.SuperReducido => Mm(17),
                _ => Mm(30)
            };

        var barcodeHeight =
            format switch
            {
                LabelFormat.Reducido => Mm(6),
                LabelFormat.SuperReducido => Mm(4),
                _ => Mm(8)
            };

        var barcodeRect = new XRect(
            rect.Left + Mm(2),
            rect.Top + Mm(21) + yOffset,
            barcodeWidth,
            barcodeHeight
        );

        if (code.Length == 13 ||
            code.Length == 12)
        {
            DrawEan13Barcode(
                gfx,
                barcodeRect,
                code
            );

            return;
        }

        if (code.Length == 8 ||
            code.Length == 7)
        {
            DrawEan8Barcode(
                gfx,
                barcodeRect,
                code
            );

            return;
        }

        if (code.Length == 5)
        {
            DrawCode128Barcode(
                gfx,
                barcodeRect,
                code
            );

            return;
        }

        var font = new XFont(
            LabelFontFamily,
            6,
            XFontStyleEx.Regular
        );

        gfx.DrawString(
            string.IsNullOrWhiteSpace(code)
                ? "SIN CÓDIGO"
                : code,
            font,
            XBrushes.Black,
            barcodeRect,
            XStringFormats.Center
        );
    }

    /// <summary>
    /// Texto inferior izquierdo:
    /// "El Kilo le sale a X,XX € sin IVA"
    /// "La Unidad le sale a X,XX € sin IVA"
    /// </summary>
    private static void DrawUnitPriceText(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label)
    {
        var unitDescription =
            string.IsNullOrWhiteSpace(label.UnitDescription)
                ? "La Unidad"
                : label.UnitDescription.Trim();

        var unitPrice =
            CalculateUnitPriceWithoutVat(label);

        var text =
            $"{unitDescription} le sale a {FormatPrice(unitPrice)} € sin IVA";

        var font = new XFont(
            LabelFontFamily,
            6.8,
            XFontStyleEx.Bold
        );

        var unitRect = new XRect(
            rect.Left + Mm(2),
            rect.Bottom - Mm(5.2),
            rect.Width - Mm(30),
            Mm(4.5)
        );

        gfx.DrawString(
            text,
            font,
            XBrushes.Black,
            unitRect,
            XStringFormats.CenterLeft
        );
    }

    /// <summary>
    /// Precio con IVA incluido en la esquina inferior derecha.
    /// </summary>
    private void DrawVatIncludedPrice(
        XGraphics gfx,
        XRect rect,
        PendingLabelDto label,
        LabelFormat format)
    {
        var priceWithVat =
            CalculateVatIncludedPrice(
                label.Price,
                label.VatCode
            );

        var text =
            $"{FormatPrice(priceWithVat)} €";

        var yOffset =
            GetSuperReducedYOffset(format);

        var priceRect = new XRect(
            rect.Left + rect.Width - Mm(29),
            rect.Bottom - Mm(11) + yOffset,
            Mm(27),
            Mm(10)
        );

        var finalFontSize =
            GetFittingFontSizeForSingleLine(
                gfx,
                text,
                LabelFontFamily,
                XFontStyleEx.Bold,
                maxFontSize: 19,
                minFontSize: 12,
                availableWidth: priceRect.Width,
                availableHeight: priceRect.Height
            );

        var font = new XFont(
            LabelFontFamily,
            finalFontSize,
            XFontStyleEx.Bold
        );

        var brush =
            IsReducedLabelFormat(format)
                ? GetReducedRedBrush()
                : XBrushes.Black;

        gfx.DrawString(
            text,
            font,
            brush,
            priceRect,
            XStringFormats.CenterRight
        );
    }

    private decimal CalculateVatIncludedPrice(
        decimal basePrice,
        string? vatCode)
    {
        var vatRate =
            GetVatRate(vatCode);

        var finalPrice =
            basePrice * (1 + (vatRate / 100m));

        return Math.Round(
            finalPrice,
            2,
            MidpointRounding.AwayFromZero
        );
    }

    private decimal GetVatRate(
        string? vatCode)
    {
        if (string.IsNullOrWhiteSpace(vatCode))
            return 0m;

        if (_options.VatRatesByCode.TryGetValue(
                vatCode.Trim(),
                out var vatRate))
        {
            return vatRate;
        }

        return 0m;
    }

    private static decimal CalculateUnitPriceWithoutVat(
        PendingLabelDto label)
    {
        if (!label.Factor.HasValue ||
            label.Factor.Value <= 0)
        {
            return label.Price;
        }

        return Math.Round(
            label.Price / label.Factor.Value,
            2,
            MidpointRounding.AwayFromZero
        );
    }

    private static List<string> BuildDescriptionLines(
        string? description,
        LabelFormat format)
    {
        var text =
            string.IsNullOrWhiteSpace(description)
                ? "ARTÍCULO SIN DESCRIPCIÓN"
                : description.Trim();

        text = text.Replace("  ", " ");

        var words =
            text.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            );

        if (words.Length == 0)
            return ["ARTÍCULO SIN DESCRIPCIÓN"];

        var maxLines =
            format == LabelFormat.SuperReducido
                ? 3
                : 2;

        if (words.Length <= maxLines)
            return words.ToList();

        var lines =
            new List<string>();

        var wordsPerLine =
            (int)Math.Ceiling(words.Length / (double)maxLines);

        for (var i = 0; i < words.Length; i += wordsPerLine)
        {
            lines.Add(
                string.Join(
                    ' ',
                    words.Skip(i).Take(wordsPerLine)
                )
            );
        }

        while (lines.Count > maxLines)
        {
            var last =
                lines[^1];

            lines.RemoveAt(lines.Count - 1);

            lines[^1] =
                $"{lines[^1]} {last}";
        }

        return lines;
    }

    private static string FormatPrice(
        decimal price)
    {
        return price.ToString(
            "0.00",
            SpanishCulture
        );
    }

    private static string OnlyDigits(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(
            value.Where(char.IsDigit).ToArray()
        );
    }

    private static double Mm(
        double value)
    {
        return XUnit.FromMillimeter(value).Point;
    }

    /// <summary>
    /// Indica si el formato debe usar color rojo en IVA/precio final.
    /// </summary>
    private static bool IsReducedLabelFormat(
        LabelFormat format)
    {
        return format is
            LabelFormat.Reducido or
            LabelFormat.SuperReducido;
    }

    /// <summary>
    /// Desplazamiento vertical especial para formato SuperReducido.
    /// Valor negativo = sube.
    /// </summary>
    private static double GetSuperReducedYOffset(
        LabelFormat format)
    {
        return format == LabelFormat.SuperReducido
            ? -Mm(3)
            : 0;
    }

    /// <summary>
    /// Color rojo usado en etiquetas reducidas.
    /// </summary>
    private static XBrush GetReducedRedBrush()
    {
        return new XSolidBrush(
            XColor.FromArgb(230, 0, 0)
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
        for (var size = maxFontSize; size >= minFontSize; size -= 0.5)
        {
            var font = new XFont(
                fontFamily,
                size,
                fontStyle
            );

            var lineHeight =
                size * 1.05;

            var totalHeight =
                lines.Count * lineHeight;

            if (totalHeight > availableHeight)
                continue;

            var allFit =
                lines.All(line =>
                    gfx.MeasureString(line, font).Width <= availableWidth
                );

            if (allFit)
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
        for (var size = maxFontSize; size >= minFontSize; size -= 0.5)
        {
            var font = new XFont(
                fontFamily,
                size,
                fontStyle
            );

            var measured =
                gfx.MeasureString(text, font);

            if (measured.Width <= availableWidth &&
                measured.Height <= availableHeight * 1.3)
            {
                return size;
            }
        }

        return minFontSize;
    }

    // =========================================================
    // Generación básica de códigos EAN-13, EAN-8 y EAN-5
    // =========================================================

    private static void DrawEan13Barcode(
        XGraphics gfx,
        XRect rect,
        string digits)
    {
        if (digits.Length == 12)
        {
            digits += CalculateEan13CheckDigit(digits);
        }

        if (digits.Length != 13)
            return;

        var parityPatterns = new[]
        {
            "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
            "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"
        };

        var lCodes = new[]
        {
            "0001101", "0011001", "0010011", "0111101", "0100011",
            "0110001", "0101111", "0111011", "0110111", "0001011"
        };

        var gCodes = new[]
        {
            "0100111", "0110011", "0011011", "0100001", "0011101",
            "0111001", "0000101", "0010001", "0001001", "0010111"
        };

        var rCodes = new[]
        {
            "1110010", "1100110", "1101100", "1000010", "1011100",
            "1001110", "1010000", "1000100", "1001000", "1110100"
        };

        var firstDigit =
            digits[0] - '0';

        var parity =
            parityPatterns[firstDigit];

        var pattern = "101";

        for (var i = 1; i <= 6; i++)
        {
            var d = digits[i] - '0';

            pattern += parity[i - 1] == 'L'
                ? lCodes[d]
                : gCodes[d];
        }

        pattern += "01010";

        for (var i = 7; i <= 12; i++)
        {
            var d = digits[i] - '0';
            pattern += rCodes[d];
        }

        pattern += "101";

        DrawBinaryBarcodePattern(
            gfx,
            rect,
            pattern
        );
    }

    private static void DrawEan8Barcode(
        XGraphics gfx,
        XRect rect,
        string digits)
    {
        if (digits.Length == 7)
        {
            digits += CalculateEan8CheckDigit(digits);
        }

        if (digits.Length != 8)
            return;

        var lCodes = new[]
        {
            "0001101", "0011001", "0010011", "0111101", "0100011",
            "0110001", "0101111", "0111011", "0110111", "0001011"
        };

        var rCodes = new[]
        {
            "1110010", "1100110", "1101100", "1000010", "1011100",
            "1001110", "1010000", "1000100", "1001000", "1110100"
        };

        var pattern = "101";

        for (var i = 0; i < 4; i++)
        {
            var d = digits[i] - '0';
            pattern += lCodes[d];
        }

        pattern += "01010";

        for (var i = 4; i < 8; i++)
        {
            var d = digits[i] - '0';
            pattern += rCodes[d];
        }

        pattern += "101";

        DrawBinaryBarcodePattern(
            gfx,
            rect,
            pattern
        );
    }

    private static void DrawBinaryBarcodePattern(
        XGraphics gfx,
        XRect rect,
        string pattern)
    {
        var moduleWidth =
            rect.Width / pattern.Length;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '1')
                continue;

            gfx.DrawRectangle(
                XBrushes.Black,
                rect.Left + (i * moduleWidth),
                rect.Top,
                moduleWidth,
                rect.Height
            );
        }
    }

    private static int CalculateEan13CheckDigit(
        string twelveDigits)
    {
        var sum = 0;

        for (var i = 0; i < 12; i++)
        {
            var digit =
                twelveDigits[i] - '0';

            sum += i % 2 == 0
                ? digit
                : digit * 3;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static int CalculateEan8CheckDigit(
        string sevenDigits)
    {
        var sum = 0;

        for (var i = 0; i < 7; i++)
        {
            var digit =
                sevenDigits[i] - '0';

            sum += i % 2 == 0
                ? digit * 3
                : digit;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static void DrawCode128Barcode(
    XGraphics gfx,
    XRect rect,
    string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var patterns = Code128Patterns;

        var codes = new List<int>
    {
        104 // Start Code B
    };

        foreach (var character in value)
        {
            if (character < 32 || character > 126)
                continue;

            codes.Add(character - 32);
        }

        if (codes.Count == 1)
            return;

        var checksum = codes[0];

        for (var i = 1; i < codes.Count; i++)
        {
            checksum += codes[i] * i;
        }

        checksum %= 103;

        codes.Add(checksum);
        codes.Add(106); // Stop

        var totalModules =
            codes.Sum(code => patterns[code].Sum(c => c - '0'));

        var moduleWidth =
            rect.Width / totalModules;

        var currentX =
            rect.Left;

        foreach (var code in codes)
        {
            var pattern =
                patterns[code];

            var drawBar = true;

            foreach (var widthChar in pattern)
            {
                var width =
                    (widthChar - '0') * moduleWidth;

                if (drawBar)
                {
                    gfx.DrawRectangle(
                        XBrushes.Black,
                        currentX,
                        rect.Top,
                        width,
                        rect.Height
                    );
                }

                currentX += width;
                drawBar = !drawBar;
            }
        }
    }

    private static readonly string[] Code128Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222",
    "122213", "122312", "132212", "221213", "221312", "231212",
    "112232", "122132", "122231", "113222", "123122", "123221",
    "223211", "221132", "221231", "213212", "223112", "312131",
    "311222", "321122", "321221", "312212", "322112", "322211",
    "212123", "212321", "232121", "111323", "131123", "131321",
    "112313", "132113", "132311", "211313", "231113", "231311",
    "112133", "112331", "132131", "113123", "113321", "133121",
    "313121", "211331", "231131", "213113", "213311", "213131",
    "311123", "311321", "331121", "312113", "312311", "332111",
    "314111", "221411", "431111", "111224", "111422", "121124",
    "121421", "141122", "141221", "112214", "112412", "122114",
    "122411", "142112", "142211", "241211", "221114", "413111",
    "241112", "134111", "111242", "121142", "121241", "114212",
    "124112", "124211", "411212", "421112", "421211", "212141",
    "214121", "412121", "111143", "111341", "131141", "114113",
    "114311", "411113", "411311", "113141", "114131", "311141",
    "411131", "211412", "211214", "211232", "2331112"
    ];
}