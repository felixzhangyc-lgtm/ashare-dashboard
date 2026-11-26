using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

internal static class DashboardReport
{
    private static readonly string[] IndustryColors =
    [
        "#1f77b4", "#aec7e8", "#ff7f0e", "#ffbb78", "#2ca02c", "#98df8a", "#d62728", "#ff9896",
        "#9467bd", "#c5b0d5", "#8c564b", "#c49c94", "#e377c2", "#f7b6d2", "#7f7f7f", "#c7c7c7",
        "#bcbd22", "#dbdb8d", "#17becf", "#9edae5", "#393b79", "#5254a3", "#6b6ecf", "#9c9ede",
        "#637939", "#8ca252", "#b5cf6b", "#cedb9c", "#8c6d31", "#bd9e39", "#e7ba52"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Run(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DateTime nowShanghai = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTimeOffset.UtcNow, "Asia/Shanghai").DateTime;

        var payload = new Dictionary<string, object?>
        {
            ["generatedAt"] = nowShanghai.ToString("yyyy-MM-dd HH:mm:ss"),
            ["regime"] = LoadRegimeScore(outputDir),
            ["industry"] = LoadIndustry(outputDir),
            ["tech"] = LoadTech(outputDir),
            ["drawdown"] = LoadDrawdown(outputDir),
            ["turnover"] = LoadTurnover(outputDir),
            ["chinextTurnover"] = LoadChinextTurnover(outputDir),
            ["runHistory"] = RunLog.LoadHistory(outputDir)
        };

        string template = ReadTemplate();
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        string html = template.Replace("__DASHBOARD_DATA__", json, StringComparison.Ordinal);

        using var outputs = new OutputBatch(outputDir);
        File.WriteAllText(outputs.Stage("dashboard.html"), html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        outputs.Commit();
        Console.WriteLine($"交互式看板已生成：{Path.Combine(outputDir, "dashboard.html")}");
    }

    private static string ReadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MarketCharts.dashboard.template.html")
            ?? throw new InvalidOperationException("找不到内嵌的 dashboard.template.html 模板资源。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static double R(string value, int digits = 2) => Math.Round(Csv.Number(value), digits);
    private static double R(double value, int digits = 2) => Math.Round(value, digits);

    private static object? LoadIndustry(string outputDir)
    {
        string folder = Path.Combine(outputDir, "margin-industry-report-project");
        string widePath = Path.Combine(folder, "sw_l1_margin_netbuy_cumulative_since_2025.csv");
        string rawPath = Path.Combine(folder, "eastmoney_margin_l2_raw_since_2025.csv");
        if (!File.Exists(widePath))
            return null;

        var table = Csv.Read(widePath);
        if (table.Rows.Count < 2)
            return null;

        string[] names = table.Header[1..];
        string[] dates = table.Rows.Select(r => r[0]).ToArray();
        var cols = Enumerable.Range(0, names.Length)
            .Select(i => table.Rows.Select(r => R(r[i + 1], 2)).ToArray())
            .ToArray();

        int[] order = Enumerable.Range(0, names.Length)
            .OrderByDescending(i => cols[i][^1])
            .ToArray();

        var orderedSeries = new List<Dictionary<string, object>>(order.Length);
        var dailyRanking = new List<(string Name, double Delta, double Cumulative)>(order.Length);
        double totalDailyNetBuy = 0;
        double prevDailyNetBuy = 0;

        for (int rank = 0; rank < order.Length; rank++)
        {
            int idx = order[rank];
            double[] vals = cols[idx];
            var deltas = new double[vals.Length];
            for (int k = 1; k < vals.Length; k++)
                deltas[k] = R(vals[k] - vals[k - 1], 2);

            double latest = vals[^1];
            double latestDelta = deltas[^1];
            totalDailyNetBuy += latestDelta;
            if (vals.Length >= 3)
                prevDailyNetBuy += deltas[^2];

            orderedSeries.Add(new Dictionary<string, object>
            {
                ["name"] = names[idx],
                ["color"] = IndustryColors[rank % IndustryColors.Length],
                ["values"] = vals,
                ["deltas"] = deltas,
                ["latest"] = latest,
                ["latestDelta"] = latestDelta
            });
            dailyRanking.Add((names[idx], latestDelta, latest));
        }

        string latestDate = dates[^1];
        string prevDate = dates[^2];
        double? totalBalance = null;
        if (File.Exists(rawPath))
        {
            var raw = Csv.Read(rawPath);
            int dIdx = Array.IndexOf(raw.Header, "date");
            int bIdx = Array.IndexOf(raw.Header, "fin_balance");
            int l1Idx = Array.IndexOf(raw.Header, "l1_industry");
            if (dIdx >= 0 && bIdx >= 0)
            {
                double balSum = 0;
                foreach (var r in raw.Rows)
                {
                    if (r[dIdx] == latestDate && (l1Idx < 0 || !string.IsNullOrWhiteSpace(r[l1Idx])) && !string.IsNullOrWhiteSpace(r[bIdx]))
                        balSum += Csv.Number(r[bIdx]);
                }
                if (balSum > 0)
                    totalBalance = R(balSum / 1e8, 2);
            }
        }

        return new Dictionary<string, object?>
        {
            ["dates"] = dates,
            ["latestDate"] = latestDate,
            ["prevDate"] = prevDate,
            ["totalBalance"] = totalBalance,
            ["totalDailyNetBuy"] = R(totalDailyNetBuy, 2),
            ["dailyChangeVsPrev"] = dates.Length >= 3 ? R(totalDailyNetBuy - prevDailyNetBuy, 2) : null,
            ["series"] = orderedSeries,
            ["dailyRanking"] = dailyRanking.OrderByDescending(x => x.Delta)
                .Select(x => new { name = x.Name, delta = x.Delta, cumulative = x.Cumulative })
                .ToArray()
        };
    }

    private static object? LoadTech(string outputDir)
    {
        string path = Path.Combine(outputDir, "tech-vs-dividend-2026", "tech_vs_dividend_2026.csv");
        if (!File.Exists(path))
            return null;
        var table = Csv.Read(path);
        if (table.Rows.Count == 0)
            return null;

        int cDate = table.Column("日期");
        int cStar = table.Column("科创50ETF华夏_前复权收盘价");
        int cChi = table.Column("创业板ETF易方达_前复权收盘价");
        int cDivPx = table.Column("红利低波ETF华泰柏瑞_前复权收盘价");
        int cGrow = table.Column("科创50与创业板各半_累计收益率");
        int cDiv = table.Column("红利低波_累计收益率");
        int cSpread = table.Column("成长组合减红利低波_差值百分点");

        string[] dates = table.Rows.Select(r => r[cDate]).ToArray();
        double[] growth = table.Rows.Select(r => R(Csv.Number(r[cGrow]) * 100.0, 2)).ToArray();
        int peakIdx = Array.IndexOf(growth, growth.Max());

        return new
        {
            dates,
            latestDate = dates[^1],
            star = table.Rows.Select(r => R(r[cStar], 4)).ToArray(),
            chinext = table.Rows.Select(r => R(r[cChi], 4)).ToArray(),
            dividendPrice = table.Rows.Select(r => R(r[cDivPx], 4)).ToArray(),
            growth,
            dividend = table.Rows.Select(r => R(Csv.Number(r[cDiv]) * 100.0, 2)).ToArray(),
            spread = table.Rows.Select(r => R(r[cSpread], 2)).ToArray(),
            peakDate = dates[peakIdx],
            peakGrowth = growth[peakIdx]
        };
    }

    private static object? LoadDrawdown(string outputDir)
    {
        string path = Path.Combine(outputDir, "margin-drawdown-speed", "margin_drawdown_speed.csv");
        if (!File.Exists(path))
            return null;
        var table = Csv.Read(path);
        if (table.Rows.Count == 0)
            return null;

        int cDate = table.Column("日期");
        int cBal = table.Column("两融余额_亿元");
        int cPeak = table.Column("近一年最高余额_亿元");
        int cDd = table.Column("相对近一年高点回撤_百分比");
        int cSpd = table.Column("回撤20交易日变化_百分点");
        int cChi = table.Column("创业板指_收盘点位");

        string[] dates = table.Rows.Select(r => r[cDate]).ToArray();
        return new
        {
            dates,
            latestDate = dates[^1],
            balance = table.Rows.Select(r => R(r[cBal], 2)).ToArray(),
            peak = table.Rows.Select(r => R(r[cPeak], 2)).ToArray(),
            drawdown = table.Rows.Select(r => R(r[cDd], 2)).ToArray(),
            speed = table.Rows.Select(r => R(r[cSpd], 2)).ToArray(),
            chinext = table.Rows.Select(r => R(r[cChi], 2)).ToArray()
        };
    }

    private static object? LoadTurnover(string outputDir)
    {
        string path = Path.Combine(outputDir, "market-turnover-cycle", "ashare_turnover_cycle.csv");
        if (!File.Exists(path))
            return null;
        var table = Csv.Read(path);
        if (table.Rows.Count == 0)
            return null;

        int n = table.Rows.Count;
        int cDate = table.Column("日期");
        int cSh = table.Column("上证指数");
        int cShAmt = table.Column("沪市成交额_亿元");
        int cSzAmt = table.Column("深市成交额_亿元");
        int cTotAmt = table.Column("全A成交额_亿元");
        int cCap = table.Column("全A流通市值_亿元");
        int cTr = table.Column("全A换手率_百分比");
        int cMa5 = table.Column("换手率MA5_百分比");
        int cMa20 = table.Column("换手率MA20_百分比");
        int cPct = table.Column("近5年历史分位_百分比");
        int cRatio = table.Column("量能比_相对MA20");
        int cState = table.Column("温度状态");
        int cMa60 = Array.IndexOf(table.Header, "上证MA60");
        int cDPeak = Array.IndexOf(table.Header, "距天量峰天数");
        int cDTrough = Array.IndexOf(table.Header, "距地量谷天数");
        int cSig = Array.IndexOf(table.Header, "周期信号");

        string[] dates = table.Rows.Select(r => r[cDate]).ToArray();
        double[] shIndex = table.Rows.Select(r => R(r[cSh], 2)).ToArray();
        double[] ma5 = table.Rows.Select(r => R(r[cMa5], 2)).ToArray();
        double[] ma20 = table.Rows.Select(r => R(r[cMa20], 2)).ToArray();
        double[] shMa60 = cMa60 >= 0 ? table.Rows.Select(r => R(r[cMa60], 2)).ToArray() : new double[n];
        int[] daysPeak = cDPeak >= 0 ? table.Rows.Select(r => int.Parse(r[cDPeak])).ToArray() : new int[n];
        int[] daysTrough = cDTrough >= 0 ? table.Rows.Select(r => int.Parse(r[cDTrough])).ToArray() : new int[n];
        string[] signals = cSig >= 0 ? table.Rows.Select(r => r[cSig].Trim()).ToArray() : Enumerable.Repeat("", n).ToArray();

        int last = n - 1;
        double winHi = shIndex[Math.Max(0, last - 251)..(last + 1)].Max();
        double latestDd252 = winHi > 0 ? R((shIndex[last] / winHi - 1.0) * 100.0, 2) : 0.0;

        object? peakInfo = null;
        if (daysPeak[last] > 0 && last - daysPeak[last] >= 0)
        {
            int pi = last - daysPeak[last];
            peakInfo = new { date = dates[pi], ma5 = ma5[pi], ma20 = ma20[pi], sh = shIndex[pi], days = daysPeak[last] };
        }

        object? troughInfo = null;
        if (daysTrough[last] > 0 && last - daysTrough[last] >= 0)
        {
            int ti = last - daysTrough[last];
            troughInfo = new
            {
                date = dates[ti],
                ma5 = ma5[ti],
                sh = shIndex[ti],
                days = daysTrough[last],
                upSince = shIndex[ti] > 0 ? R((shIndex[last] / shIndex[ti] - 1.0) * 100.0, 2) : 0.0
            };
        }

        var signalEvents = new List<object>();
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrEmpty(signals[i]))
                continue;
            int refDays = signals[i].StartsWith("牛转熊", StringComparison.Ordinal) ? daysPeak[i] : daysTrough[i];
            int refIdx = refDays > 0 && i - refDays >= 0 ? i - refDays : i;
            signalEvents.Add(new
            {
                index = i,
                date = dates[i],
                kind = signals[i],
                sh = shIndex[i],
                ma5 = ma5[i],
                refDate = dates[refIdx],
                refMa5 = ma5[refIdx],
                refSh = shIndex[refIdx],
                days = refDays
            });
        }

        return new
        {
            dates,
            firstDate = dates[0],
            latestDate = dates[last],
            shIndex,
            shMa60,
            shDd252Latest = latestDd252,
            shAmountYi = table.Rows.Select(r => R(r[cShAmt], 1)).ToArray(),
            szAmountYi = table.Rows.Select(r => R(r[cSzAmt], 1)).ToArray(),
            totalAmountYi = table.Rows.Select(r => R(r[cTotAmt], 1)).ToArray(),
            floatCapWanYi = table.Rows.Select(r => R(Csv.Number(r[cCap]) / 10_000.0, 2)).ToArray(),
            turnover = table.Rows.Select(r => R(r[cTr], 2)).ToArray(),
            ma5,
            ma20,
            pct5y = table.Rows.Select(r => R(r[cPct], 1)).ToArray(),
            volRatio = table.Rows.Select(r => R(r[cRatio], 2)).ToArray(),
            daysSincePeak = daysPeak,
            daysSinceTrough = daysTrough,
            signals,
            signalEvents,
            peakInfo,
            troughInfo,
            latestState = table.Rows[last][cState]
        };
    }

    private static object? LoadChinextTurnover(string outputDir)
    {
        string path = Path.Combine(outputDir, "chinext-turnover-cycle", "chinext_turnover_cycle.csv");
        if (!File.Exists(path))
            return null;
        var table = Csv.Read(path);
        if (table.Rows.Count == 0)
            return null;

        int n = table.Rows.Count;
        int cDate = table.Column("日期");
        int cIdx = table.Column("创业板指");
        int cAmt = table.Column("创业板成交额_亿元");
        int cCap = table.Column("创业板流通市值_亿元");
        int cTr = table.Column("创业板换手率_百分比");
        int cMa5 = table.Column("换手率MA5_百分比");
        int cMa20 = table.Column("换手率MA20_百分比");
        int cPct = table.Column("近5年历史分位_百分比");
        int cRatio = table.Column("量能比_相对MA20");
        int cState = table.Column("温度状态");
        int cMa60 = table.Column("创业板MA60");
        int cDPeak = table.Column("距天量峰天数");
        int cDTrough = table.Column("距地量谷天数");
        int cSig = table.Column("周期信号");

        string[] dates = table.Rows.Select(r => r[cDate]).ToArray();
        double[] idxVal = table.Rows.Select(r => R(r[cIdx], 2)).ToArray();
        double[] ma5 = table.Rows.Select(r => R(r[cMa5], 2)).ToArray();
        double[] ma20 = table.Rows.Select(r => R(r[cMa20], 2)).ToArray();
        double[] idxMa60 = table.Rows.Select(r => R(r[cMa60], 2)).ToArray();
        int[] daysPeak = table.Rows.Select(r => int.Parse(r[cDPeak])).ToArray();
        int[] daysTrough = table.Rows.Select(r => int.Parse(r[cDTrough])).ToArray();
        string[] signals = table.Rows.Select(r => r[cSig].Trim()).ToArray();

        int last = n - 1;
        double winHi = idxVal[Math.Max(0, last - 251)..(last + 1)].Max();
        double latestDd252 = winHi > 0 ? R((idxVal[last] / winHi - 1.0) * 100.0, 2) : 0.0;

        object? peakInfo = null;
        if (daysPeak[last] > 0 && last - daysPeak[last] >= 0)
        {
            int pi = last - daysPeak[last];
            peakInfo = new { date = dates[pi], ma5 = ma5[pi], ma20 = ma20[pi], idx = idxVal[pi], days = daysPeak[last] };
        }

        object? troughInfo = null;
        if (daysTrough[last] > 0 && last - daysTrough[last] >= 0)
        {
            int ti = last - daysTrough[last];
            troughInfo = new
            {
                date = dates[ti],
                ma5 = ma5[ti],
                idx = idxVal[ti],
                days = daysTrough[last],
                upSince = idxVal[ti] > 0 ? R((idxVal[last] / idxVal[ti] - 1.0) * 100.0, 2) : 0.0
            };
        }

        var signalEvents = new List<object>();
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrEmpty(signals[i]))
                continue;
            int refDays = signals[i].StartsWith("牛转熊", StringComparison.Ordinal) ? daysPeak[i] : daysTrough[i];
            int refIdx = refDays > 0 && i - refDays >= 0 ? i - refDays : i;
            signalEvents.Add(new
            {
                index = i,
                date = dates[i],
                kind = signals[i],
                idx = idxVal[i],
                ma5 = ma5[i],
                refDate = dates[refIdx],
                refMa5 = ma5[refIdx],
                refIdx = idxVal[refIdx],
                days = refDays
            });
        }

        return new
        {
            dates,
            firstDate = dates[0],
            latestDate = dates[last],
            indexVal = idxVal,
            indexMa60 = idxMa60,
            indexDd252Latest = latestDd252,
            amountYi = table.Rows.Select(r => R(r[cAmt], 1)).ToArray(),
            floatCapYi = table.Rows.Select(r => R(r[cCap], 1)).ToArray(),
            turnover = table.Rows.Select(r => R(r[cTr], 2)).ToArray(),
            ma5,
            ma20,
            pct5y = table.Rows.Select(r => R(r[cPct], 1)).ToArray(),
            volRatio = table.Rows.Select(r => R(r[cRatio], 2)).ToArray(),
            daysSincePeak = daysPeak,
            daysSinceTrough = daysTrough,
            signals,
            signalEvents,
            peakInfo,
            troughInfo,
            latestState = table.Rows[last][cState]
        };
    }

    private static object? LoadRegimeScore(string outputDir)
    {
        RegimeScoreReport.Row[] rows;
        try
        {
            rows = RegimeScoreReport.ComputeFromOutputs(outputDir);
        }
        catch
        {
            return null;
        }
        if (rows.Length < 20)
            return null;

        int n = rows.Length;
        int last = n - 1;
        string[] dates = rows.Select(r => Csv.Date(r.Date)).ToArray();

        // Build contiguous regime segments for ECharts markArea background bands
        var segments = new List<object>();
        int segStart = 0;
        for (int i = 1; i <= n; i++)
        {
            if (i == n || rows[i].Regime != rows[segStart].Regime)
            {
                segments.Add(new
                {
                    regime = rows[segStart].Regime,
                    start = dates[segStart],
                    end = dates[i - 1]
                });
                segStart = i;
            }
        }

        // Historical causal signal events
        var events = new List<object>();
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrEmpty(rows[i].Signal))
                continue;
            events.Add(new
            {
                index = i,
                date = dates[i],
                kind = rows[i].Signal,
                idx = R(rows[i].Index, 2),
                fastScore = R(rows[i].FastScore, 1),
                slowScore = R(rows[i].SlowScore, 1),
                marginDd = R(rows[i].MarginDd, 2),
                marginChg20 = R(rows[i].MarginChg20, 2),
                turnMa5 = R(rows[i].TurnMa5, 2)
            });
        }

        // Count consecutive days in current regime
        int daysInRegime = 1;
        for (int i = last - 1; i >= 0 && rows[i].Regime == rows[last].Regime; i--)
            daysInRegime++;
        string regimeStartDate = dates[last - daysInRegime + 1];

        var cur = rows[last];
        return new
        {
            dates,
            firstDate = dates[0],
            latestDate = dates[last],
            indexVal = rows.Select(r => R(r.Index, 2)).ToArray(),
            ma60 = rows.Select(r => R(r.Ma60, 2)).ToArray(),
            ma120 = rows.Select(r => R(r.Ma120, 2)).ToArray(),
            indexDd252 = rows.Select(r => R(r.IndexDd252, 1)).ToArray(),
            marginDd = rows.Select(r => R(r.MarginDd, 2)).ToArray(),
            marginChg20 = rows.Select(r => R(r.MarginChg20, 2)).ToArray(),
            marginChg60 = rows.Select(r => R(r.MarginChg60, 2)).ToArray(),
            turnMa5 = rows.Select(r => R(r.TurnMa5, 2)).ToArray(),
            turnMa20 = rows.Select(r => R(r.TurnMa20, 2)).ToArray(),
            daysSincePeak = rows.Select(r => r.DaysSincePeak).ToArray(),
            daysSinceTrough = rows.Select(r => r.DaysSinceTrough).ToArray(),
            trendScore = rows.Select(r => R(r.TrendScore, 1)).ToArray(),
            marginDdScore = rows.Select(r => R(r.MarginDdScore, 1)).ToArray(),
            marginGrowthScore = rows.Select(r => R(r.MarginGrowthScore, 1)).ToArray(),
            turnoverScore = rows.Select(r => R(r.TurnoverScore, 1)).ToArray(),
            rawScore = rows.Select(r => R(r.RawScore, 1)).ToArray(),
            fastScore = rows.Select(r => R(r.FastScore, 1)).ToArray(),
            slowScore = rows.Select(r => R(r.SlowScore, 1)).ToArray(),
            regimes = rows.Select(r => r.Regime).ToArray(),
            signals = rows.Select(r => r.Signal).ToArray(),
            segments,
            events,
            latest = new
            {
                date = dates[last],
                regime = cur.Regime,
                regimeLabel = RegimeScoreReport.RegimeLabel(cur.Regime),
                daysInRegime,
                regimeStartDate,
                rawScore = R(cur.RawScore, 1),
                fastScore = R(cur.FastScore, 1),
                slowScore = R(cur.SlowScore, 1),
                trendScore = R(cur.TrendScore, 1),
                marginDdScore = R(cur.MarginDdScore, 1),
                marginGrowthScore = R(cur.MarginGrowthScore, 1),
                turnoverScore = R(cur.TurnoverScore, 1),
                indexVal = R(cur.Index, 2),
                ma60 = R(cur.Ma60, 2),
                ma120 = R(cur.Ma120, 2),
                indexDd252 = R(cur.IndexDd252, 1),
                marginBalance = R(cur.MarginBalance, 2),
                marginDd = R(cur.MarginDd, 2),
                marginChg20 = R(cur.MarginChg20, 2),
                marginChg60 = R(cur.MarginChg60, 2),
                turnMa5 = R(cur.TurnMa5, 2),
                turnMa20 = R(cur.TurnMa20, 2),
                daysSincePeak = cur.DaysSincePeak,
                daysSinceTrough = cur.DaysSinceTrough
            }
        };
    }
}
