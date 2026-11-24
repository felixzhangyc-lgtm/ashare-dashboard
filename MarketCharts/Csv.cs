using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

internal sealed record Csv(string[] Header, List<string[]> Rows)
{
    public static Csv Read(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8);
        parser.SetDelimiters(",");
        parser.TrimWhiteSpace = false;
        var header = parser.ReadFields() ?? throw new InvalidDataException($"CSV 缺少表头：{path}");
        var rows = new List<string[]>();
        while (parser.ReadFields() is { } row)
        {
            if (row.Length != header.Length)
                throw new InvalidDataException($"CSV 列数与表头不一致：{path}");
            rows.Add(row);
        }
        return new Csv(header, rows);
    }

    public int Column(string name)
    {
        int index = Array.IndexOf(Header, name);
        return index >= 0 ? index : throw new InvalidDataException($"CSV 缺少字段 {name}。");
    }

    public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true)) { NewLine = "\n" };
        writer.WriteLine(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
            writer.WriteLine(string.Join(',', row.Select(Escape)));
    }

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseDate(string date) => DateOnly.ParseExact(date.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static double Number(string value)
    {
        double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(number) ? number : throw new InvalidDataException($"无效数值 {value}。");
    }

    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    public static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("F2");

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\""
        : value;
}

internal sealed class OutputBatch : IDisposable
{
    private readonly string staging;
    private readonly string destination;
    private readonly List<string> files = [];

    public OutputBatch(string directory)
    {
        destination = directory;
        staging = Path.Combine(directory, $".update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
    }

    public string Stage(string name)
    {
        files.Add(name);
        return Path.Combine(staging, name);
    }

    public void Commit()
    {
        foreach (string name in files)
            File.Move(Path.Combine(staging, name), Path.Combine(destination, name), overwrite: true);
    }

    public void Dispose() => Directory.Delete(staging, recursive: true);
}
