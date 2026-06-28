using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace FakeNewsDetector.Evaluation;

public record DatasetItem(string Title, string Text, string TrueLabel);

public static class DatasetReader
{
    /// <summary>
    /// Loads a balanced sample from the ISOT dataset (True.csv + Fake.csv).
    /// Each CSV has columns: title, text, subject, date
    /// </summary>
    public static List<DatasetItem> LoadIsot(string trueFile, string fakeFile,
        int maxPerClass = 50, int seed = 42)
    {
        Console.WriteLine($"  Loading true articles from  : {trueFile}");
        var trueItems = ReadFile(trueFile, "true");
        Console.WriteLine($"  Loaded {trueItems.Count} usable true articles.");

        Console.WriteLine($"  Loading fake articles from  : {fakeFile}");
        var fakeItems = ReadFile(fakeFile, "fake");
        Console.WriteLine($"  Loaded {fakeItems.Count} usable fake articles.");

        var rng = new Random(seed);
        var sampled = trueItems.OrderBy(_ => rng.Next()).Take(maxPerClass)
            .Concat(fakeItems.OrderBy(_ => rng.Next()).Take(maxPerClass))
            .OrderBy(_ => rng.Next())
            .ToList();

        Console.WriteLine($"  Sampled {sampled.Count} articles ({maxPerClass} per class, seed={seed}).");
        return sampled;
    }

    private static List<DatasetItem> ReadFile(string path, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Dataset file not found: {path}\nSee Evaluation/data/DOWNLOAD.md for instructions.", path);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
        };

        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        return csv.GetRecords<ISOTRecord>()
            .Where(r => !string.IsNullOrWhiteSpace(r.text) && r.text!.Length > 80)
            .Select(r => new DatasetItem(
                (r.title ?? "").Trim(),
                (r.text ?? "").Trim(),
                label))
            .ToList();
    }

    private sealed class ISOTRecord
    {
        public string? title { get; set; }
        public string? text { get; set; }
        public string? subject { get; set; }
        public string? date { get; set; }
    }
}
