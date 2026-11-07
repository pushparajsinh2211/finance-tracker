using System.Globalization;

namespace FamilyLedger.Api.Services.FinanceHub;

public static class TesseractTsvLayoutParser
{
    public static IReadOnlyList<string> ReconstructRows(string tsv)
    {
        var words = new List<OcrWord>();
        foreach (var line in tsv.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split('\t');
            if (columns.Length < 12 ||
                string.Equals(columns[0], "level", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(columns[1], out var page) ||
                !int.TryParse(columns[6], out var left) ||
                !int.TryParse(columns[7], out var top) ||
                !int.TryParse(columns[8], out var width) ||
                !int.TryParse(columns[9], out var height))
            {
                continue;
            }

            var text = string.Join("\t", columns.Skip(11)).Trim();
            if (string.IsNullOrWhiteSpace(text) || width <= 0 || height <= 0)
                continue;

            var confidence = float.TryParse(
                columns[10],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedConfidence)
                ? parsedConfidence
                : -1;

            words.Add(new OcrWord(
                page,
                left,
                top,
                width,
                height,
                confidence,
                text));
        }

        var result = new List<string>();
        foreach (var page in words
                     .GroupBy(word => word.Page)
                     .OrderBy(group => group.Key))
        {
            var pageWords = page.ToArray();
            var medianHeight = Median(pageWords.Select(word => word.Height));
            var centerTolerance = Math.Max(4d, medianHeight * 0.65d);
            var rows = new List<OcrRow>();

            foreach (var word in pageWords
                         .OrderBy(item => item.CenterY)
                         .ThenBy(item => item.Left))
            {
                var row = rows
                    .Where(candidate =>
                        Math.Abs(candidate.CenterY - word.CenterY) <=
                        centerTolerance)
                    .OrderBy(candidate =>
                        Math.Abs(candidate.CenterY - word.CenterY))
                    .FirstOrDefault();

                if (row == null)
                {
                    rows.Add(new OcrRow(word));
                }
                else
                {
                    row.Add(word);
                }
            }

            result.AddRange(rows
                .OrderBy(row => row.Top)
                .ThenBy(row => row.CenterY)
                .Select(row => string.Join(
                    " ",
                    row.Words
                        .OrderBy(word => word.Left)
                        .ThenBy(word => word.Top)
                        .Select(word => word.Text)))
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        return result;
    }

    private static double Median(IEnumerable<int> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private sealed record OcrWord(
        int Page,
        int Left,
        int Top,
        int Width,
        int Height,
        float Confidence,
        string Text)
    {
        public double CenterY => Top + Height / 2d;
    }

    private sealed class OcrRow
    {
        private double _centerTotal;

        public OcrRow(OcrWord word)
        {
            Words.Add(word);
            _centerTotal = word.CenterY;
            CenterY = word.CenterY;
            Top = word.Top;
        }

        public List<OcrWord> Words { get; } = [];
        public double CenterY { get; private set; }
        public int Top { get; private set; }

        public void Add(OcrWord word)
        {
            Words.Add(word);
            _centerTotal += word.CenterY;
            CenterY = _centerTotal / Words.Count;
            Top = Math.Min(Top, word.Top);
        }
    }
}
