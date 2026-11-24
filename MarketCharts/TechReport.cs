internal static class TechReport
{
    private const string FileName = "tech_vs_dividend_2026";
    private static readonly DateOnly Baseline = new(2025, 12, 31);
    private static readonly string[] Header =
    [
        "日期", "科创50ETF华夏_前复权收盘价", "创业板ETF易方达_前复权收盘价", "红利低波ETF华泰柏瑞_前复权收盘价",
        "科创50与创业板各半_累计收益率", "红利低波_累计收益率", "成长组合减红利低波_差值百分点"
    ];

    private sealed record Prices(DateOnly Date, double Star, double Chinext, double Dividend);

    public static async Task RunAsync(MarketData client, DateOnly today, string directory, bool fromCsv)
    {
        var prices = fromCsv ? ReadPrices(Path.Combine(directory, FileName + ".csv"))
            : await FetchPricesAsync(client, today);
        if (prices.Length == 0 || prices[0].Date != Baseline)
            throw new InvalidDataException($"ETF 行情缺少基准交易日 {Csv.Date(Baseline)}。");
        if (prices[^1].Date > today || prices.Any(row => row.Star <= 0 || row.Chinext <= 0 || row.Dividend <= 0))
            throw new InvalidDataException("ETF 行情含未来日期或无效价格。");

        var baseline = prices[0];
        double[] growth = prices.Select(row => (row.Star / baseline.Star + row.Chinext / baseline.Chinext) / 2 - 1).ToArray();
        double[] dividend = prices.Select(row => row.Dividend / baseline.Dividend - 1).ToArray();
        double[] spread = growth.Zip(dividend, (a, b) => a - b).ToArray();
        int peak = Array.IndexOf(growth, growth.Max());
        DateOnly[] dates = prices.Select(row => row.Date).ToArray();

        using var outputs = new OutputBatch(directory);
        ReportCharts.Tech(outputs.Stage(FileName + ".png"), dates, growth, dividend, spread, peak);
        Csv.Write(outputs.Stage(FileName + ".csv"), Header, prices.Select((row, i) => new[]
        {
            Csv.Date(row.Date), Csv.Number(row.Star), Csv.Number(row.Chinext), Csv.Number(row.Dividend),
            Csv.Number(growth[i]), Csv.Number(dividend[i]), Csv.Number(spread[i] * 100)
        }));
        outputs.Commit();
        Console.WriteLine($"成长红利 {Csv.Date(dates[^1])}  成长 {Csv.Signed(growth[^1] * 100)}%  红利 {Csv.Signed(dividend[^1] * 100)}%  差值 {Csv.Signed(spread[^1] * 100)} 个百分点");
        Console.WriteLine($"成长峰值 {Csv.Date(dates[peak])}  {Csv.Signed(growth[peak] * 100)}%  共 {prices.Length} 个交易日");
    }

    private static Prices[] ReadPrices(string path)
    {
        var table = Csv.Read(path);
        int[] columns = Header.Take(4).Select(table.Column).ToArray();
        return table.Rows.Select(row => new Prices(Csv.ParseDate(row[columns[0]]),
            Csv.Number(row[columns[1]]), Csv.Number(row[columns[2]]), Csv.Number(row[columns[3]])))
            .OrderBy(row => row.Date).ToArray();
    }

    private static async Task<Prices[]> FetchPricesAsync(MarketData client, DateOnly today)
    {
        var prices = await Task.WhenAll(
            client.FundPricesAsync("1.588000", Baseline, today),
            client.FundPricesAsync("0.159915", Baseline, today),
            client.FundPricesAsync("1.512890", Baseline, today));
        return prices[0].Keys.Intersect(prices[1].Keys).Intersect(prices[2].Keys).Order()
            .Select(date => new Prices(date, prices[0][date], prices[1][date], prices[2][date])).ToArray();
    }
}
