using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
//using System.Reflection.Metadata;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
//using static System.Net.Mime.MediaTypeNames;

if (args.Length < 2 || args.Length > 3)
{
    Console.Error.WriteLine("Usage: HexDumpWordHighlighter <hexDumpPath> <tlcOffsetsPath> [outputDocxPath]");
    return 1;
}

string hexDumpPath = args[0];
string tlcOffsetsPath = args[1];
string outputDocxPath = args.Length >= 3
    ? args[2]
    : Path.ChangeExtension(hexDumpPath, ".highlighted.docx");

if (!File.Exists(hexDumpPath))
{
    Console.Error.WriteLine($"Hex dump file not found: {hexDumpPath}");
    return 2;
}

if (!File.Exists(tlcOffsetsPath))
{
    Console.Error.WriteLine($"TLC offsets file not found: {tlcOffsetsPath}");
    return 3;
}

try
{
    var level0Items = TlcOffsetParser.ParseLevelZeroItems(tlcOffsetsPath)
        .OrderBy(x => x.Offset)
        .ToList();

    if (level0Items.Count == 0)
    {
        Console.Error.WriteLine("No level 0 TLC items were found in the TLC offsets file.");
        return 4;
    }

    var highlightRanges = level0Items
        .Select(x => new HighlightRange(x.Offset, x.Offset + x.Length, x.Code))
        .ToList();

    var hexLines = HexDumpParser.ParseFile(hexDumpPath).ToList();

    if (hexLines.Count == 0)
    {
        Console.Error.WriteLine("No hex dump lines were parsed from the input file.");
        return 5;
    }

    WordWriter.CreateDocument(outputDocxPath, hexDumpPath, tlcOffsetsPath, hexLines, level0Items, highlightRanges);

    Console.WriteLine($"Created: {outputDocxPath}");
    Console.WriteLine($"Level 0 items highlighted: {level0Items.Count}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 10;
}

internal sealed record TlcLevel0Item(string Code, long Offset, int Length);
internal sealed record HighlightRange(long StartInclusive, long EndExclusive, string Label);
internal sealed record HexDumpLine(long Offset, IReadOnlyList<byte> Bytes, string AsciiText)
{
    public long EndOffsetExclusive => Offset + Bytes.Count;
}

internal static class TlcOffsetParser
{
    public static IEnumerable<TlcLevel0Item> ParseLevelZeroItems(string path)
    {
        foreach (string rawLine in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            string line = rawLine.Trim();
            string[] parts = line.Split(['\t'], StringSplitOptions.None);

            if (parts.Length < 4)
            {
                parts = Regex.Split(line, @"\s+");
            }

            if (parts.Length < 4)
            {
                continue;
            }

            string code = parts[0].Trim();
            string offsetText = parts[1].Trim();
            string lengthText = parts[2].Trim();
            string levelText = parts[3].Trim();

            if (!int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
            {
                continue;
            }

            if (level != 0)
            {
                continue;
            }

            if (!TryParseOffset(offsetText, out long offset))
            {
                continue;
            }

            if (!int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int length) || length < 0)
            {
                continue;
            }

            yield return new TlcLevel0Item(code, offset, length);
        }
    }

    private static bool TryParseOffset(string text, out long value)
    {
        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (Regex.IsMatch(text, "^[0-9A-Fa-f]+$"))
        {
            return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

internal static partial class HexDumpParser
{
    // Expected shape:
    // 00000000  45 42 53 05 ...  EBS.....
    [GeneratedRegex(@"^(?<offset>[0-9A-Fa-f]{8,16})\s{2}(?<hex>(?:[0-9A-Fa-f]{2}\s+){1,16})(?<ascii>.{0,16})$")]
    private static partial Regex HexLineRegex();

    public static IEnumerable<HexDumpLine> ParseFile(string path)
    {
        foreach (string raw in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string line = raw.TrimEnd();
            Match match = HexLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            long offset = long.Parse(match.Groups["offset"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            string hexGroup = match.Groups["hex"].Value;
            string ascii = match.Groups["ascii"].Value;

            var bytes = new List<byte>(16);
            foreach (Match byteMatch in Regex.Matches(hexGroup, @"[0-9A-Fa-f]{2}"))
            {
                bytes.Add(byte.Parse(byteMatch.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            yield return new HexDumpLine(offset, bytes, ascii);
        }
    }
}

internal static class WordWriter
{
    private const int PreferredLinesPerPage = 36 + 18 + 10;
    private const string HeadingFontSize = "22";  // 11pt
    private const string TitleFontSize = "20";  // 10pt
    private const string NormalFontSize = "18"; // 9pt


    public static void CreateDocument(
        string outputPath,
        string hexDumpPath,
        string tlcOffsetsPath,
        IReadOnlyList<HexDumpLine> hexLines,
        IReadOnlyList<TlcLevel0Item> level0Items,
        IReadOnlyList<HighlightRange> ranges)
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using WordprocessingDocument document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        MainDocumentPart mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();

        Body body = new();
        mainPart.Document.Append(body);

        AddStylesPart(mainPart);
        body.Append(CreatePageSettings());
        body.Append(CreateTitleParagraph(hexDumpPath, tlcOffsetsPath, level0Items.Count));
        body.Append(CreateLegendParagraph());

        int linesOnPage = 0;
        int nextItemIndex = 0;
        long? nextLevel0Start = level0Items.Count > 0 ? level0Items[0].Offset : null;

        foreach (HexDumpLine line in hexLines)
        {
            if (ShouldInsertPageBreak(line.Offset, linesOnPage, nextLevel0Start, level0Items, nextItemIndex))
            {
                body.Append(CreatePageBreakParagraph());
                body.Append(CreateSectionHeaderParagraph(level0Items[nextItemIndex]));
                linesOnPage = 1;
            }

            body.Append(CreateHexParagraph(line, ranges));
            linesOnPage++;

            while (nextItemIndex < level0Items.Count && line.EndOffsetExclusive > level0Items[nextItemIndex].Offset)
            {
                nextItemIndex++;
            }

            nextLevel0Start = nextItemIndex < level0Items.Count ? level0Items[nextItemIndex].Offset : null;
        }

        mainPart.Document.Save();
    }

    private static bool ShouldInsertPageBreak(
        long currentOffset,
        int linesOnPage,
        long? nextLevel0Start,
        IReadOnlyList<TlcLevel0Item> items,
        int nextItemIndex)
    {
        if (linesOnPage >= PreferredLinesPerPage)
            return true;

        if (nextLevel0Start is null)
        {
            return false;
        }

        if (nextItemIndex == 0)
        {
            return false;
        }

        if (currentOffset != nextLevel0Start.Value)
        {
            return false;
        }

        return false;
    }

    private static Paragraph CreateTitleParagraph(string hexDumpPath, string tlcOffsetsPath, int level0Count)
    {
        return new Paragraph(
            CreateParagraphProperties(justification: JustificationValues.Left),
            CreateRun(
                $"Hex dump highlight report\nHex dump: {Path.GetFileName(hexDumpPath)}\nTLC offsets: {Path.GetFileName(tlcOffsetsPath)}\nLevel 0 items: {level0Count}",
                fontName: "Calibri",
                fontSizeHalfPoints: HeadingFontSize,
                bold: true));
    }

    private static Paragraph CreateLegendParagraph()
    {
        return new Paragraph(
            CreateParagraphProperties(spacingAfterTwips: 180),
            CreateRun("Yellow highlight = TLC level 0 byte range in both the hex columns and the character column.", fontName: "Calibri", fontSizeHalfPoints: TitleFontSize));
    }

    private static Paragraph CreateSectionHeaderParagraph(TlcLevel0Item item)
    {
        return new Paragraph(
            CreateParagraphProperties(spacingBeforeTwips: 120, spacingAfterTwips: 80),
            CreateRun($"Section start: {item.Code} @ 0x{item.Offset:X8} ({item.Length} bytes)", fontName: "Calibri", fontSizeHalfPoints: TitleFontSize, bold: true));
    }

    private static Paragraph CreatePageBreakParagraph()
    {
        return new Paragraph(
            new Run(new Break { Type = BreakValues.Page }));
    }

    private static Paragraph CreateHexParagraph(HexDumpLine line, IReadOnlyList<HighlightRange> ranges)
    {
        Paragraph paragraph = new(CreateParagraphProperties(styleId: "HexDump"));

        paragraph.Append(CreateRun($"{line.Offset:X8}  ", fontName: "Consolas", fontSizeHalfPoints: NormalFontSize));

        for (int i = 0; i < 16; i++)
        {
            if (i < line.Bytes.Count)
            {
                long byteOffset = line.Offset + i;
                HighlightColorValues? highlightColor = GetHighlightColor(byteOffset, ranges);
                paragraph.Append(CreateRun($"{line.Bytes[i]:X2}", fontName: "Consolas", fontSizeHalfPoints: NormalFontSize, highlightColor: highlightColor));
            }
            else
            {
                paragraph.Append(CreateRun("  ", fontName: "Consolas", fontSizeHalfPoints: NormalFontSize));
            }

            paragraph.Append(CreateRun(i == 15 ? "  " : " ", fontName: "Consolas", fontSizeHalfPoints: NormalFontSize));
        }

        string ascii = line.AsciiText.PadRight(Math.Min(16, Math.Max(16, line.Bytes.Count)));
        if (ascii.Length < 16)
        {
            ascii = ascii.PadRight(16);
        }
        else if (ascii.Length > 16)
        {
            ascii = ascii[..16];
        }

        for (int i = 0; i < 16; i++)
        {
            bool hasByte = i < line.Bytes.Count;
            long byteOffset = line.Offset + i;
            //???bool highlighted = hasByte && IsHighlighted(byteOffset, ranges);
            char ch = ascii[i];
            HighlightColorValues? highlightColor = hasByte ? GetHighlightColor(byteOffset, ranges) : null;
            paragraph.Append(CreateRun(ch.ToString(), fontName: "Consolas", fontSizeHalfPoints: NormalFontSize, highlightColor: highlightColor));
        }

        return paragraph;
    }

    //private static bool IsHighlighted(long byteOffset, IReadOnlyList<HighlightRange> ranges)
    //{
    //    foreach (HighlightRange range in ranges)
    //    {
    //        if (byteOffset >= range.StartInclusive && byteOffset < range.EndExclusive)
    //        {
    //            return true;
    //        }
    //    }

    //    return false;
    //}

    private static HighlightColorValues? GetHighlightColor(long byteOffset, IReadOnlyList<HighlightRange> ranges)
    {
        for (int i = 0; i < ranges.Count; i++)
        {
            HighlightRange range = ranges[i];
            if (byteOffset >= range.StartInclusive && byteOffset < range.EndExclusive)
            {
                return (i % 2 == 0) ? HighlightColorValues.Yellow : HighlightColorValues.Cyan;
            }
        }

        return null;
    }

    private static void AddStylesPart(MainDocumentPart mainPart)
    {
        StyleDefinitionsPart stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(
                new StyleName { Val = "HexDump" },
                new BasedOn { Val = "Normal" },
                new UIPriority { Val = 99 },
                new Rsid { Val = "00000000" },
                new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                    new FontSize { Val = NormalFontSize }))
            {
                Type = StyleValues.Paragraph,
                StyleId = "HexDump",
                CustomStyle = true
            });

        stylesPart.Styles.Save();
    }

    private static SectionProperties CreatePageSettings()
    {
        return new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720, Header = 360, Footer = 360, Gutter = 0 },
            new Columns { Space = "720" },
            new DocGrid { LinePitch = 360 });
    }

    private static ParagraphProperties CreateParagraphProperties(
        string? styleId = null,
        JustificationValues? justification = null,
        int? spacingBeforeTwips = null,
        int? spacingAfterTwips = null)
    {
        ParagraphProperties props = new();

        if (!string.IsNullOrWhiteSpace(styleId))
        {
            props.Append(new ParagraphStyleId { Val = styleId });
        }

        if (justification is not null)
        {
            props.Append(new Justification { Val = justification.Value });
        }

        if (spacingBeforeTwips is not null || spacingAfterTwips is not null)
        {
            props.Append(new SpacingBetweenLines
            {
                Before = (spacingBeforeTwips ?? 0).ToString(CultureInfo.InvariantCulture),
                After = (spacingAfterTwips ?? 0).ToString(CultureInfo.InvariantCulture)
            });
        }

        return props;
    }

    // Update CreateRun signature and highlight block:
    private static Run CreateRun(
        string text,
        string fontName,
        string fontSizeHalfPoints,
        bool bold = false,
        HighlightColorValues? highlightColor = null)
    {
        RunProperties runProps = new(
            new RunFonts { Ascii = fontName, HighAnsi = fontName, ComplexScript = fontName },
            new FontSize { Val = fontSizeHalfPoints },
            new FontSizeComplexScript { Val = fontSizeHalfPoints });

        if (bold)
        {
            runProps.Append(new Bold());
        }

        if (highlightColor is not null)
        {
            string fill = highlightColor == HighlightColorValues.Cyan ? "00FFFF" : "FFFF00";
            runProps.Append(new Highlight { Val = highlightColor.Value });
            runProps.Append(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = fill
            });
        }

        return new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
