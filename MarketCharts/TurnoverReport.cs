internal static class TurnoverReport
{
    private const string FileName = "ashare_turnover_cycle";
    private static readonly DateOnly Start = new(2010, 3, 31);
    private const int PercentileWindow = 1260; // ~5 years
    private const int PeakLookback = 120;
    private const int TroughLookback = 120;
    private const int BearWarnDays = 40;
    private const int BearConfirmDays = 95;
    private const int BottomConfirmDays = 75;
    private const double PeakMa5Min = 2.2;
    private const double PeakMa20Min = 1.8;
    private const double TroughMa5Max = 0.90;
    private const double TroughMinDrawdown = -10.0;
    private const double YuanPerYi = 100_000_000;

    private static readonly string[] Header =
    [
        "日期", "上证指数", "沪市成交额_亿元", "深市成交额_亿元", "全A成交额_亿元",
        "全A流通市值_亿元", "全A换手率_百分比", "换手率MA5_百分比", "换手率MA20_百分比",
        "近5年历史分位_百分比", "量能比_相对MA20", "温度状态",
        "上证MA60", "距天量峰天数", "距地量谷天数", "周期信号"
    ];

    public sealed record SignalMarker(int Index, string Kind, string Label);

    private sealed record Row(
        DateOnly Date, double ShIndex, double ShAmountYi, double SzAmountYi, double TotalAmountYi,
        double FloatCapYi, double TurnoverPct, double Ma5, double Ma20, double Percentile5Y,
        double VolumeRatio, string State, double ShMa60, int DaysSincePeak, int DaysSinceTrough, string Signal);

    public static async Task RunAsync(MarketData client, DateOnly today, string directory, bool fromCsv)
    {
        var data = fromCsv ? ReadCached(Path.Combine(directory, FileName + ".csv"))
            : await FetchAsync(client, today);
        if (data.Length < 20)
            throw new InvalidDataException("缺少足够的全A换手率历史数据。");

        var latest = data[^1];
        double floatCapWanYi = latest.FloatCapYi / 10_000.0;

        // Build live timer banner text for PNG & console
        int peakIdx = latest.DaysSincePeak > 0 ? data.Length - 1 - latest.DaysSincePeak : -1;
        string topStatus = peakIdx >= 0
            ? $"牛转熊计时：距{Csv.Date(data[peakIdx].Date)}峰({data[peakIdx].Ma5:F2}%)已{latest.DaysSincePeak}日未超" +
              (latest.DaysSincePeak >= BearConfirmDays ? "【已确认≥95日】"
                : latest.DaysSincePeak >= BearWarnDays && latest.ShIndex < latest.ShMa60 ? "【已预警≥40日+破60线】"
                : $"（预警40日/确认95日）")
            : "牛转熊计时：近1年无合格天量峰";

        int troughIdx = latest.DaysSinceTrough > 0 ? data.Length - 1 - latest.DaysSinceTrough : -1;
        string botStatus = troughIdx >= 0
            ? $"筑底计时：距{Csv.Date(data[troughIdx].Date)}地量({data[troughIdx].Ma5:F2}%)已{latest.DaysSinceTrough}日未破" +
              (latest.DaysSinceTrough >= BottomConfirmDays ? "【已确认≥75日】" : "（目标75日）")
            : $"筑底计时：未入地量区（当前MA5 {latest.Ma5:F2}%，门槛≤0.90%≈{floatCapWanYi * 0.009:F2}万亿）";

        var signalMarkers = new List<SignalMarker>();
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].Signal is "牛转熊确认" or "筑底确认")
            {
                string shortTag = data[i].Signal == "牛转熊确认" ? "转熊" : "筑底";
                signalMarkers.Add(new SignalMarker(i, data[i].Signal, $"{data[i].Date:yy-MM}{shortTag}"));
            }
        }

        using var outputs = new OutputBatch(directory);
        ReportCharts.Turnover(
            outputs.Stage(FileName + ".png"),
            data.Select(r => r.Date).ToArray(),
            data.Select(r => r.TurnoverPct).ToArray(),
            data.Select(r => r.Ma5).ToArray(),
            data.Select(r => r.Ma20).ToArray(),
            data.Select(r => r.ShIndex).ToArray(),
            $"{topStatus}  ｜  {botStatus}",
            [.. signalMarkers]);

        if (!fromCsv)
        {
            Csv.Write(outputs.Stage(FileName + ".csv"), Header, data.Select(r => new[]
            {
                Csv.Date(r.Date), r.ShIndex.ToString("F2"), r.ShAmountYi.ToString("F2"),
                r.SzAmountYi.ToString("F2"), r.TotalAmountYi.ToString("F2"), r.FloatCapYi.ToString("F2"),
                r.TurnoverPct.ToString("F4"), r.Ma5.ToString("F4"), r.Ma20.ToString("F4"),
                r.Percentile5Y.ToString("F2"), r.VolumeRatio.ToString("F3"), r.State,
                r.ShMa60.ToString("F2"), r.DaysSincePeak.ToString(), r.DaysSinceTrough.ToString(), r.Signal
            }));
        }
        outputs.Commit();

        Console.WriteLine($"全A换手率 {Csv.Date(latest.Date)}  上证 {latest.ShIndex:F2}（MA60 {latest.ShMa60:F2}）  成交额 {latest.TotalAmountYi / 10_000:F2} 万亿  流通市值 {floatCapWanYi:F2} 万亿");
        Console.WriteLine($"当日换手率 {latest.TurnoverPct:F2}%  MA5 {latest.Ma5:F2}%  MA20 {latest.Ma20:F2}%  近5年分位 {latest.Percentile5Y:F1}%  状态：{latest.State}  共 {data.Length} 个交易日");
        Console.WriteLine($"{topStatus}  ｜  {botStatus}");
        Console.WriteLine($"按最新流通市值({floatCapWanYi:F2}万亿)动态折算：3.0%过热≈{floatCapWanYi * 0.03:F2}万亿  4.0%天量≈{floatCapWanYi * 0.04:F2}万亿  1.0%冰点≈{floatCapWanYi * 0.01:F2}万亿  0.6%极冷≈{floatCapWanYi * 0.006:F2}万亿");
    }

    private static Row[] ReadCached(string path)
    {
        var table = Csv.Read(path);
        int[] baseCols = Header.Take(12).Select(table.Column).ToArray();
        var raw = table.Rows.Select(r => (
            Date: Csv.ParseDate(r[baseCols[0]]),
            ShClose: Csv.Number(r[baseCols[1]]),
            ShAmtYi: Csv.Number(r[baseCols[2]]),
            SzAmtYi: Csv.Number(r[baseCols[3]]),
            FloatCapYi: Csv.Number(r[baseCols[5]]),
            Turnover: Csv.Number(r[baseCols[6]])
        )).OrderBy(r => r.Date).ToList();
        return ComputeRows(raw);
    }

    private static async Task<Row[]> FetchAsync(MarketData client, DateOnly today)
    {
        var capTask = client.DataCenterAsync("RPTA_RZRQ_LSHJ", "DIM_DATE,LTSZ",
            "DIM_DATE", $"(DIM_DATE>='{Csv.Date(Start)}')", 800);
        var shTask = client.BaiduIndexDailyAsync("000001", Start);
        var szTask = client.BaiduIndexDailyAsync("399106", Start);
        await Task.WhenAll(capTask, shTask, szTask);

        var capByDate = new Dictionary<DateOnly, double>();
        foreach (var row in await capTask)
        {
            if (!row.TryGetProperty("DIM_DATE", out var dProp) || dProp.ValueKind != System.Text.Json.JsonValueKind.String)
                continue;
            if (!row.TryGetProperty("LTSZ", out var capProp) || capProp.ValueKind == System.Text.Json.JsonValueKind.Null)
                continue;
            double cap = MarketData.Number(capProp);
            if (cap <= 0)
                continue;
            var date = Csv.ParseDate(dProp.GetString()!);
            if (date <= today)
                capByDate[date] = cap;
        }

        var sh = await shTask;
        var sz = await szTask;

        // Supplement any post-close trading days newer than T+1 RPTA_RZRQ_LSHJ using official SSE+SZSE negotiable cap,
        // falling back smoothly to composite-index-scaled float cap when exchanges haven't published today's cap yet.
        if (capByDate.Count > 0)
        {
            var prevDate = capByDate.Keys.Max();
            var newerDates = sh.Keys.Where(d => d > prevDate && d <= today && sz.ContainsKey(d))
                .Order().TakeLast(3).ToArray();
            foreach (var d in newerDates)
            {
                double? exCap = await client.ExchangeNegotiableCapYuanAsync(d);
                if (exCap is > 0)
                {
                    capByDate[d] = exCap.Value;
                    prevDate = d;
                }
                else if (sh.TryGetValue(prevDate, out var prevSh) && sz.TryGetValue(prevDate, out var prevSz)
                    && prevSh.Close > 0 && prevSz.Close > 0 && sh[d].Close > 0 && sz[d].Close > 0)
                {
                    double weightedFactor = 0.55 * (sh[d].Close / prevSh.Close) + 0.45 * (sz[d].Close / prevSz.Close);
                    capByDate[d] = capByDate[prevDate] * weightedFactor;
                    prevDate = d;
                }
            }
        }

        var commonDates = capByDate.Keys.Where(d => sh.ContainsKey(d) && sz.ContainsKey(d))
            .Order().ToArray();

        var raw = new List<(DateOnly Date, double ShClose, double ShAmtYi, double SzAmtYi, double FloatCapYi, double Turnover)>();
        foreach (var date in commonDates)
        {
            var shDay = sh[date];
            var szDay = sz[date];
            double floatCap = capByDate[date];
            if (shDay.Close <= 0 || shDay.Amount <= 0 || szDay.Amount <= 0 || floatCap <= 0)
                continue;
            double totalAmt = shDay.Amount + szDay.Amount;
            double turnoverPct = totalAmt / floatCap * 100.0;
            if (turnoverPct <= 0 || turnoverPct > 20)
                continue;
            raw.Add((date, shDay.Close, shDay.Amount / YuanPerYi, szDay.Amount / YuanPerYi, floatCap / YuanPerYi, turnoverPct));
        }

        return ComputeRows(raw);
    }

    private static Row[] ComputeRows(List<(DateOnly Date, double ShClose, double ShAmtYi, double SzAmtYi, double FloatCapYi, double Turnover)> raw)
    {
        int n = raw.Count;
        var ma5 = new double[n];
        var ma20 = new double[n];
        var shMa60 = new double[n];
        var shDd252 = new double[n];
        var pct5Y = new double[n];

        double sum5 = 0, sum20 = 0, sumSh60 = 0;
        for (int i = 0; i < n; i++)
        {
            sum5 += raw[i].Turnover;
            if (i >= 5) sum5 -= raw[i - 5].Turnover;
            ma5[i] = sum5 / Math.Min(i + 1, 5);

            sum20 += raw[i].Turnover;
            if (i >= 20) sum20 -= raw[i - 20].Turnover;
            ma20[i] = sum20 / Math.Min(i + 1, 20);

            sumSh60 += raw[i].ShClose;
            if (i >= 60) sumSh60 -= raw[i - 60].ShClose;
            shMa60[i] = sumSh60 / Math.Min(i + 1, 60);

            double shHi = 0;
            for (int j = Math.Max(0, i - 251); j <= i; j++)
                shHi = Math.Max(shHi, raw[j].ShClose);
            shDd252[i] = shHi > 0 ? (raw[i].ShClose / shHi - 1.0) * 100.0 : 0;

            int winStart = Math.Max(0, i - PercentileWindow + 1);
            int winLen = i - winStart + 1;
            int leCount = 0;
            for (int j = winStart; j <= i; j++)
            {
                if (raw[j].Turnover <= raw[i].Turnover)
                    leCount++;
            }
            pct5Y[i] = leCount * 100.0 / winLen;
        }

        // Strictly causal cycle timers & signals (no lookahead)
        var daysSincePeak = new int[n];
        var daysSinceTrough = new int[n];
        var signal = new string[n];
        Array.Fill(signal, "");

        int activePeak = -1;
        bool peakWarned = false;
        bool peakConfirmed = false;
        int lastWarnIdx = -1000;
        int lastConfirmIdx = -1000;

        int activeTrough = -1;
        bool troughConfirmed = false;
        int lastBottomIdx = -1000;

        for (int i = 0; i < n; i++)
        {
            // 1. Update active qualified turnover peak (MA5 >= 2.2%, MA20 >= 1.8%, 120d high)
            if (i >= PeakLookback)
            {
                double winMax = 0;
                for (int j = i - PeakLookback + 1; j <= i; j++)
                    winMax = Math.Max(winMax, ma5[j]);
                if (ma5[i] >= PeakMa5Min && ma20[i] >= PeakMa20Min && ma5[i] >= winMax)
                {
                    if (activePeak < 0 || ma5[i] >= ma5[activePeak] || i - activePeak > 252)
                    {
                        activePeak = i;
                        peakWarned = false;
                        peakConfirmed = false;
                    }
                }
            }

            if (activePeak >= 0)
            {
                if (ma5[i] > ma5[activePeak])
                {
                    activePeak = ma5[i] >= PeakMa5Min && ma20[i] >= PeakMa20Min ? i : -1;
                    peakWarned = false;
                    peakConfirmed = false;
                }
                else if (i - activePeak > 252)
                {
                    // Peak older than 1 trading year expires from active counter
                    activePeak = -1;
                    peakWarned = false;
                    peakConfirmed = false;
                }
            }

            if (activePeak >= 0 && i > activePeak)
            {
                int d = i - activePeak;
                daysSincePeak[i] = d;
                if (!peakConfirmed && d >= BearConfirmDays && i - lastConfirmIdx >= 60)
                {
                    signal[i] = "牛转熊确认";
                    peakConfirmed = true;
                    lastConfirmIdx = i;
                }
                else if (!peakWarned && d >= BearWarnDays && raw[i].ShClose < shMa60[i] && i - lastWarnIdx >= 60)
                {
                    signal[i] = "牛转熊预警";
                    peakWarned = true;
                    lastWarnIdx = i;
                }
            }

            // 2. Update active qualified turnover trough (MA5 <= 0.90%, 120d low, SH drawdown <= -10%)
            if (i >= TroughLookback)
            {
                double winMin = double.MaxValue;
                for (int j = i - TroughLookback + 1; j <= i; j++)
                    winMin = Math.Min(winMin, ma5[j]);
                if (ma5[i] <= TroughMa5Max && shDd252[i] <= TroughMinDrawdown && ma5[i] <= winMin)
                {
                    if (activeTrough < 0 || ma5[i] <= ma5[activeTrough] || i - activeTrough > 252)
                    {
                        activeTrough = i;
                        troughConfirmed = false;
                    }
                }
            }

            if (activeTrough >= 0)
            {
                if (ma5[i] < ma5[activeTrough])
                {
                    activeTrough = ma5[i] <= TroughMa5Max && shDd252[i] <= TroughMinDrawdown ? i : -1;
                    troughConfirmed = false;
                }
                else if (i - activeTrough > 252)
                {
                    activeTrough = -1;
                    troughConfirmed = false;
                }
            }

            if (activeTrough >= 0 && i > activeTrough)
            {
                int d = i - activeTrough;
                daysSinceTrough[i] = d;
                if (!troughConfirmed && d >= BottomConfirmDays && i - lastBottomIdx >= 60)
                {
                    signal[i] = "筑底确认";
                    troughConfirmed = true;
                    lastBottomIdx = i;
                }
            }
        }

        var rows = new Row[n];
        for (int i = 0; i < n; i++)
        {
            var item = raw[i];
            double volRatio = ma20[i] > 0 ? item.Turnover / ma20[i] : 1.0;
            rows[i] = new Row(
                item.Date, item.ShClose, item.ShAmtYi, item.SzAmtYi,
                item.ShAmtYi + item.SzAmtYi, item.FloatCapYi,
                item.Turnover, ma5[i], ma20[i], pct5Y[i], volRatio, Classify(item.Turnover),
                shMa60[i], daysSincePeak[i], daysSinceTrough[i], signal[i]);
        }
        return rows;
    }

    private static string Classify(double turnoverPct) => turnoverPct switch
    {
        >= 3.5 => "极端天量(勿追)",
        >= 3.0 => "过热警戒(勿追)",
        >= 2.2 => "偏热活跃",
        >= 1.0 => "常态震荡",
        >= 0.65 => "低谷冷清(勿割)",
        _ => "极端冰点(勿割)"
    };
}
