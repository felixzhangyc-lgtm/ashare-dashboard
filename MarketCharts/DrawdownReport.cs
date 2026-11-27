internal static class DrawdownReport
{
    private const string FileName = "margin_drawdown_speed";
    private static readonly DateOnly ChartStart = new(2016, 1, 1);
    private static readonly string[] Header =
    [
        "日期", "两融余额_亿元", "近一年最高余额_亿元", "相对近一年高点回撤_百分比",
        "回撤20交易日变化_百分点", "创业板指_收盘点位"
    ];

    private sealed record MarginDay(DateOnly Date, double Balance, double Peak, double Drawdown, double Speed, double Index);

    public static async Task RunAsync(MarketData client, DateOnly today, string directory, bool fromCsv)
    {
        var data = fromCsv ? ReadCached(Path.Combine(directory, FileName + ".csv"))
            : await FetchAsync(client, today);
        if (data.Length == 0)
            throw new InvalidDataException("缺少两融回撤历史数据。");

        using var outputs = new OutputBatch(directory);
        ReportCharts.Drawdown(outputs.Stage(FileName + ".png"), data.Select(row => row.Date).ToArray(),
            data.Select(row => row.Drawdown).ToArray(), data.Select(row => row.Speed).ToArray(),
            data.Select(row => row.Index).ToArray());
        if (!fromCsv)
            Csv.Write(outputs.Stage(FileName + ".csv"), Header, data.Select(row => new[]
            {
                Csv.Date(row.Date), Csv.Number(row.Balance), Csv.Number(row.Peak), Csv.Number(row.Drawdown),
                Csv.Number(row.Speed), Csv.Number(row.Index)
            }));
        outputs.Commit();
        var latest = data[^1];
        Console.WriteLine($"两融回撤 {Csv.Date(latest.Date)}  余额 {latest.Balance:F2} 亿元  回撤 {latest.Drawdown:F2}%  20日变化 {Csv.Signed(latest.Speed)} 个百分点  创业板指 {latest.Index:F2}  共 {data.Length} 个交易日");
    }

    private static MarginDay[] ReadCached(string path)
    {
        var table = Csv.Read(path);
        int[] columns = Header.Select(table.Column).ToArray();
        return table.Rows.Select(row => new MarginDay(Csv.ParseDate(row[columns[0]]), Csv.Number(row[columns[1]]),
            Csv.Number(row[columns[2]]), Csv.Number(row[columns[3]]), Csv.Number(row[columns[4]]),
            Csv.Number(row[columns[5]]))).OrderBy(row => row.Date).ToArray();
    }

    private static async Task<MarginDay[]> FetchAsync(MarketData client, DateOnly today)
    {
        var marginTask = client.DataCenterAsync("RPTA_RZRQ_LSHJ", "DIM_DATE,RZRQYE,RZYE,RQYE",
            "DIM_DATE", "(DIM_DATE>='2015-01-01')", 800);
        var indexTask = client.ChinextAsync(ChartStart.Year, today);
        await Task.WhenAll(marginTask, indexTask);
        var margin = (await marginTask).Select(row => (
            Date: Csv.ParseDate(row.GetProperty("DIM_DATE").GetString()!),
            Balance: MarketData.Number(row.GetProperty("RZRQYE")) / 100_000_000))
            .Where(row => row.Date <= today)
            .DistinctBy(row => row.Date).OrderBy(row => row.Date).ToArray();
        var index = await indexTask;

        var drawdowns = new double[margin.Length];
        var result = new List<MarginDay>();
        for (int i = 0; i < margin.Length; i++)
        {
            var (date, balance) = margin[i];
            if (balance <= 0)
                throw new InvalidDataException($"无效两融余额：{Csv.Date(date)}。");

            // Retain the 2015 warm-up rows for both trading-day windows.
            double peak = 0;
            for (int j = Math.Max(0, i - 251); j <= i; j++)
                peak = Math.Max(peak, margin[j].Balance);
            double drawdown = drawdowns[i] = (balance / peak - 1) * 100;
            if (date < ChartStart)
                continue;
            if (i < 20 || !index.TryGetValue(date, out double close))
                throw new InvalidDataException($"交易日 {Csv.Date(date)} 缺少回撤计算历史或创业板指点位。");
            result.Add(new MarginDay(date, balance, peak, drawdown, drawdown - drawdowns[i - 20], close));
        }
        return result.ToArray();
    }
}
