internal static class ChinextTurnoverReport
{
    private const string FileName = "chinext_turnover_cycle";
    private const string AnchorFileName = "chinext_szse_anchors.csv";
    private static readonly DateOnly Start = new(2012, 12, 3);
    private const int PercentileWindow = 1260; // ~5 years

    // 14-year backtest-calibrated parameters for ChiNext (创业板)
    public const int PeakLookback = 120;
    public const int TroughLookback = 120;
    public const int BearWarnDays = 40;
    public const int BearConfirmDays = 95;
    public const int BottomConfirmDays = 75;
    public const double PeakMa5Min = 5.0;
    public const double PeakMa20Min = 4.0;
    public const double TroughMa5Max = 2.0;
    public const double TroughMinDrawdown = -20.0;

    private const double YuanPerYi = 100_000_000.0;

    // Built-in official SZSE ChiNext negotiable market cap (ltsz, 亿元) baseline anchors
    private static readonly (DateOnly Date, double FloatCapYi)[] BaselineAnchors =
    [
        (new DateOnly(2012, 12, 3), 2700.0),
        (new DateOnly(2015, 5, 28), 35600.0),
        (new DateOnly(2018, 9, 17), 25600.0),
        (new DateOnly(2018, 10, 18), 22900.0),
        (new DateOnly(2021, 7, 22), 90800.0),
        (new DateOnly(2024, 8, 13), 69000.0),
        (new DateOnly(2024, 9, 2), 67375.47),
        (new DateOnly(2024, 9, 26), 73470.21),
        (new DateOnly(2024, 10, 8), 106500.0),
        (new DateOnly(2026, 1, 12), 156100.0),
        (new DateOnly(2026, 11, 23), 109200.0)
    ];

    private static readonly string[] Header =
    [
        "日期", "创业板指", "创业板综", "创业板成交额_亿元", "创业板流通市值_亿元",
        "创业板换手率_百分比", "换手率MA5_百分比", "换手率MA20_百分比",
        "近5年历史分位_百分比", "量能比_相对MA20", "温度状态",
        "创业板MA60", "距天量峰天数", "距地量谷天数", "周期信号"
    ];

    private sealed record RawDay(DateOnly Date, double IndexClose, double CompClose, double AmountYi, double FloatCapYi);

    private sealed record Row(
        DateOnly Date, double IndexClose, double CompClose, double AmountYi, double FloatCapYi,
        double TurnoverPct, double Ma5, double Ma20, double Percentile5Y, double VolumeRatio,
        string State, double IndexMa60, int DaysSincePeak, int DaysSinceTrough, string Signal);

    public static async Task RunAsync(MarketData client, DateOnly today, string directory, bool fromCsv)
    {
        string csvPath = Path.Combine(directory, FileName + ".csv");
        var raw = fromCsv ? ReadRaw(csvPath) : await FetchAsync(client, today, directory);
        if (raw.Count < 20)
            throw new InvalidDataException("缺少足够的创业板换手率历史数据。");

        var data = ComputeRows(raw);
        var latest = data[^1];
        double floatCapWanYi = latest.FloatCapYi / 10_000.0;

        int peakIdx = latest.DaysSincePeak > 0 ? data.Length - 1 - latest.DaysSincePeak : -1;
        string topStatus = peakIdx >= 0
            ? $"牛转熊计时：距{Csv.Date(data[peakIdx].Date)}峰({data[peakIdx].Ma5:F2}%)已{latest.DaysSincePeak}日未超" +
              (latest.DaysSincePeak >= BearConfirmDays ? $"【已确认≥{BearConfirmDays}日】"
                : latest.DaysSincePeak >= BearWarnDays && latest.IndexClose < latest.IndexMa60 ? $"【已预警≥{BearWarnDays}日+破60线】"
                : $"（预警{BearWarnDays}日/确认{BearConfirmDays}日）")
            : "牛转熊计时：近1年无合格天量峰";

        int troughIdx = latest.DaysSinceTrough > 0 ? data.Length - 1 - latest.DaysSinceTrough : -1;
        string botStatus = troughIdx >= 0
            ? $"筑底计时：距{Csv.Date(data[troughIdx].Date)}地量({data[troughIdx].Ma5:F2}%)已{latest.DaysSinceTrough}日未破" +
              (latest.DaysSinceTrough >= BottomConfirmDays ? $"【已确认≥{BottomConfirmDays}日】" : $"（目标{BottomConfirmDays}日）")
            : $"筑底计时：未入地量区（当前MA5 {latest.Ma5:F2}%，门槛≤{TroughMa5Max:F1}%≈{latest.FloatCapYi * TroughMa5Max / 100.0:F0}亿）";

        var signalMarkers = new List<TurnoverReport.SignalMarker>();
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].Signal is "牛转熊确认" or "筑底确认")
            {
                string shortTag = data[i].Signal == "牛转熊确认" ? "转熊" : "筑底";
                signalMarkers.Add(new TurnoverReport.SignalMarker(i, data[i].Signal, $"{data[i].Date:yy-MM}{shortTag}"));
            }
        }

        using var outputs = new OutputBatch(directory);
        ReportCharts.ChinextTurnover(
            outputs.Stage(FileName + ".png"),
            data.Select(r => r.Date).ToArray(),
            data.Select(r => r.TurnoverPct).ToArray(),
            data.Select(r => r.Ma5).ToArray(),
            data.Select(r => r.Ma20).ToArray(),
            data.Select(r => r.IndexClose).ToArray(),
            $"{topStatus}  ｜  {botStatus}",
            [.. signalMarkers]);

        if (!fromCsv)
        {
            Csv.Write(outputs.Stage(FileName + ".csv"), Header, data.Select(r => new[]
            {
                Csv.Date(r.Date), r.IndexClose.ToString("F2"), r.CompClose.ToString("F2"),
                r.AmountYi.ToString("F2"), r.FloatCapYi.ToString("F2"),
                r.TurnoverPct.ToString("F4"), r.Ma5.ToString("F4"), r.Ma20.ToString("F4"),
                r.Percentile5Y.ToString("F2"), r.VolumeRatio.ToString("F3"), r.State,
                r.IndexMa60.ToString("F2"), r.DaysSincePeak.ToString(), r.DaysSinceTrough.ToString(), r.Signal
            }));
        }
        outputs.Commit();

        Console.WriteLine($"创业板换手率 {Csv.Date(latest.Date)}  创业板指 {latest.IndexClose:F2}（MA60 {latest.IndexMa60:F2}）  成交额 {latest.AmountYi:F1} 亿  流通市值 {floatCapWanYi:F2} 万亿");
        Console.WriteLine($"当日换手率 {latest.TurnoverPct:F2}%  MA5 {latest.Ma5:F2}%  MA20 {latest.Ma20:F2}%  近5年分位 {latest.Percentile5Y:F1}%  状态：{latest.State}  共 {data.Length} 个交易日");
        Console.WriteLine($"{topStatus}  ｜  {botStatus}");
        Console.WriteLine($"按最新创业板流通市值({floatCapWanYi:F2}万亿)动态折算：5.0%过热≈{latest.FloatCapYi * 0.05:F0}亿  6.5%天量≈{latest.FloatCapYi * 0.065:F0}亿  2.0%冰点≈{latest.FloatCapYi * 0.02:F0}亿  1.6%极冷≈{latest.FloatCapYi * 0.016:F0}亿");
    }

    private static List<RawDay> ReadRaw(string path)
    {
        var table = Csv.Read(path);
        int cDate = table.Column("日期");
        int cIdx = table.Column("创业板指");
        int cComp = table.Column("创业板综");
        int cAmt = table.Column("创业板成交额_亿元");
        int cFloat = table.Column("创业板流通市值_亿元");
        return table.Rows.Select(r => new RawDay(
            Csv.ParseDate(r[cDate]), Csv.Number(r[cIdx]), Csv.Number(r[cComp]),
            Csv.Number(r[cAmt]), Csv.Number(r[cFloat])))
            .OrderBy(r => r.Date).ToList();
    }

    private static async Task<List<RawDay>> FetchAsync(MarketData client, DateOnly today, string directory)
    {
        var idxTask = client.BaiduIndexDailyAsync("399006", Start);
        var compTask = client.BaiduIndexDailyAsync("399102", Start);
        await Task.WhenAll(idxTask, compTask);
        var idxMap = await idxTask;
        var compMap = await compTask;

        var commonDates = idxMap.Keys.Where(d => d >= Start && d <= today && compMap.ContainsKey(d))
            .Order().ToArray();
        if (commonDates.Length == 0)
            throw new InvalidDataException("未能获取创业板历史日线数据。");

        // Load official SZSE anchors (built-in baseline + persisted cache)
        var anchors = BaselineAnchors.ToDictionary(x => x.Date, x => x.FloatCapYi);
        string anchorPath = Path.Combine(directory, AnchorFileName);
        if (File.Exists(anchorPath))
        {
            var anchorTable = Csv.Read(anchorPath);
            int cD = anchorTable.Column("date");
            int cL = anchorTable.Column("float_cap_yi");
            foreach (var r in anchorTable.Rows)
                anchors[Csv.ParseDate(r[cD])] = Csv.Number(r[cL]);
        }

        // Gently fetch at most 1 official SZSE stats call for the latest trading day (never bursts SZSE)
        DateOnly latestDate = commonDates[^1];
        if (!anchors.ContainsKey(latestDate))
        {
            try
            {
                var stats = await client.SzseChinextStatsAsync(latestDate);
                if (stats is { } s && s.FloatCapYi > 0)
                {
                    anchors[latestDate] = s.FloatCapYi;
                    Directory.CreateDirectory(directory);
                    Csv.Write(anchorPath, ["date", "float_cap_yi"],
                        anchors.OrderBy(kv => kv.Key).Select(kv => new[] { Csv.Date(kv.Key), kv.Value.ToString("F2") }));
                }
            }
            catch (Exception)
            {
                // Fallback smoothly to latest official divisor * close_399102 if SZSE is temporarily unavailable.
            }
        }

        // Convert official SZSE ltsz anchors into smooth divisor = ltsz / close_399102
        var divisorAnchors = anchors
            .Where(kv => compMap.TryGetValue(kv.Key, out var c) && c.Close > 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => (DayNum: kv.Key.DayNumber, Divisor: kv.Value / compMap[kv.Key].Close))
            .ToArray();
        if (divisorAnchors.Length == 0)
            throw new InvalidDataException("缺少创业板流通市值校准锚点。");

        double InterpDivisor(DateOnly d)
        {
            int x = d.DayNumber;
            if (x <= divisorAnchors[0].DayNum) return divisorAnchors[0].Divisor;
            if (x >= divisorAnchors[^1].DayNum) return divisorAnchors[^1].Divisor;
            for (int k = 0; k < divisorAnchors.Length - 1; k++)
            {
                var (x0, y0) = divisorAnchors[k];
                var (x1, y1) = divisorAnchors[k + 1];
                if (x0 <= x && x <= x1)
                    return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
            }
            return divisorAnchors[^1].Divisor;
        }

        var raw = new List<RawDay>(commonDates.Length);
        foreach (var d in commonDates)
        {
            var idxDay = idxMap[d];
            var compDay = compMap[d];
            double amtYi = (idxDay.Amount > 0 ? idxDay.Amount : compDay.Amount) / YuanPerYi;
            if (idxDay.Close <= 0 || compDay.Close <= 0 || amtYi <= 0)
                continue;
            double floatCapYi = anchors.TryGetValue(d, out double exact)
                ? exact
                : InterpDivisor(d) * compDay.Close;
            if (floatCapYi <= 0)
                continue;
            raw.Add(new RawDay(d, idxDay.Close, compDay.Close, amtYi, floatCapYi));
        }

        // If today is still intraday (before close, e.g. amount < 15% of previous day's amount and no SZSE official close yet), drop partial bar.
        if (raw.Count >= 2 && raw[^1].Date == today && !anchors.ContainsKey(today))
        {
            DateTime nowShanghai = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Shanghai").DateTime;
            if (nowShanghai.TimeOfDay < new TimeSpan(15, 5, 0))
                raw.RemoveAt(raw.Count - 1);
        }

        return raw;
    }

    private static Row[] ComputeRows(List<RawDay> raw)
    {
        int n = raw.Count;
        var turnover = new double[n];
        var ma5 = new double[n];
        var ma20 = new double[n];
        var idxMa60 = new double[n];
        var idxDd252 = new double[n];
        var pct5Y = new double[n];

        double sum5 = 0, sum20 = 0, sumIdx60 = 0;
        for (int i = 0; i < n; i++)
        {
            turnover[i] = raw[i].AmountYi / raw[i].FloatCapYi * 100.0;
            sum5 += turnover[i];
            if (i >= 5) sum5 -= turnover[i - 5];
            ma5[i] = sum5 / Math.Min(i + 1, 5);

            sum20 += turnover[i];
            if (i >= 20) sum20 -= turnover[i - 20];
            ma20[i] = sum20 / Math.Min(i + 1, 20);

            sumIdx60 += raw[i].IndexClose;
            if (i >= 60) sumIdx60 -= raw[i - 60].IndexClose;
            idxMa60[i] = sumIdx60 / Math.Min(i + 1, 60);

            double hi = 0;
            for (int j = Math.Max(0, i - 251); j <= i; j++)
                hi = Math.Max(hi, raw[j].IndexClose);
            idxDd252[i] = hi > 0 ? (raw[i].IndexClose / hi - 1.0) * 100.0 : 0;

            int winStart = Math.Max(0, i - PercentileWindow + 1);
            int winLen = i - winStart + 1;
            int leCount = 0;
            for (int j = winStart; j <= i; j++)
                if (turnover[j] <= turnover[i]) leCount++;
            pct5Y[i] = leCount * 100.0 / winLen;
        }

        var daysSincePeak = new int[n];
        var daysSinceTrough = new int[n];
        var signal = new string[n];
        Array.Fill(signal, "");

        int activePeak = -1;
        bool peakWarned = false, peakConfirmed = false;
        int lastWarnIdx = -1000, lastConfirmIdx = -1000;

        int activeTrough = -1;
        bool troughConfirmed = false;
        int lastBottomIdx = -1000;

        for (int i = 0; i < n; i++)
        {
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
                else if (!peakWarned && d >= BearWarnDays && raw[i].IndexClose < idxMa60[i] && i - lastWarnIdx >= 60)
                {
                    signal[i] = "牛转熊预警";
                    peakWarned = true;
                    lastWarnIdx = i;
                }
            }

            if (i >= TroughLookback)
            {
                double winMin = double.MaxValue;
                for (int j = i - TroughLookback + 1; j <= i; j++)
                    winMin = Math.Min(winMin, ma5[j]);
                if (ma5[i] <= TroughMa5Max && idxDd252[i] <= TroughMinDrawdown && ma5[i] <= winMin)
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
                    activeTrough = ma5[i] <= TroughMa5Max && idxDd252[i] <= TroughMinDrawdown ? i : -1;
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
            double volRatio = ma20[i] > 0 ? turnover[i] / ma20[i] : 1.0;
            rows[i] = new Row(
                raw[i].Date, raw[i].IndexClose, raw[i].CompClose, raw[i].AmountYi, raw[i].FloatCapYi,
                turnover[i], ma5[i], ma20[i], pct5Y[i], volRatio, Classify(turnover[i]),
                idxMa60[i], daysSincePeak[i], daysSinceTrough[i], signal[i]);
        }
        return rows;
    }

    private static string Classify(double turnoverPct) => turnoverPct switch
    {
        >= 6.5 => "极端天量(勿追)",
        >= 5.0 => "过热警戒(勿追)",
        >= 3.8 => "偏热活跃",
        >= 2.0 => "常态震荡",
        >= 1.6 => "低谷冷清(勿割)",
        _ => "极端冰点(勿割)"
    };
}
