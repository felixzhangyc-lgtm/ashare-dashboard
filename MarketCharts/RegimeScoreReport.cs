using System.Text;

internal static class RegimeScoreReport
{
    public const string SubDir = "regime-score";
    public const string FileName = "chinext_regime_score";

    public const double WeightTrend = 30.0;
    public const double WeightMarginDd = 25.0;
    public const double WeightMarginGrowth = 25.0;
    public const double WeightTurnover = 20.0;

    public const double BullThreshold = 65.0;
    public const double RecoverThreshold = 60.0;
    public const double WarnThreshold = 50.0;
    public const double BearThreshold = 36.0;
    public const double ClimaxFastBearThreshold = 55.0;
    public const int ClimaxEarlyWarnDays = 95;
    public const int ClimaxReenterCooldownDays = 40;
    public const int FastSmoothDays = 5;
    public const int SlowSmoothDays = 10;
    public const string ModelVersion = "A-2.0";

    private static readonly string[] Header =
    [
        "日期", "创业板指", "创业板MA60", "创业板MA120", "创指近一年回撤_百分比",
        "两融余额_亿元", "两融回撤_百分比", "两融20日真实增速_百分比", "两融60日真实增速_百分比",
        "创业板换手MA5_百分比", "创业板换手MA20_百分比", "距天量峰天数", "距地量谷天数",
        "趋势子分", "两融水位子分", "两融增速子分", "换手热度子分",
        "综合景气分", "景气分5日均", "景气分10日均", "牛熊状态", "转换信号", "数据口径"
    ];

    public sealed record Row(
        DateOnly Date,
        double Index,
        double Ma60,
        double Ma120,
        double IndexDd252,
        double MarginBalance,
        double MarginDd,
        double MarginChg20,
        double MarginChg60,
        double TurnMa5,
        double TurnMa20,
        int DaysSincePeak,
        int DaysSinceTrough,
        double TrendScore,
        double MarginDdScore,
        double MarginGrowthScore,
        double TurnoverScore,
        double RawScore,
        double FastScore,
        double SlowScore,
        string Regime,
        string Signal,
        bool IsProvisional,
        DateOnly MarginDate,
        bool ClimaxArmed);

    public sealed record SignalMarker(int Index, string Kind, string Label);

    public static void Run(string outputDir)
    {
        var data = ComputeFromOutputs(outputDir);
        if (data.Length < 20)
            throw new InvalidDataException("缺少足够的对齐数据，无法计算创业板四维牛熊景气度。");

        string dir = Path.Combine(outputDir, SubDir);
        var signals = new List<SignalMarker>();
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].Signal is "牛转熊确认" or "天量破位转熊" or "熊转牛确认" or "冰点筑底区")
            {
                string shortTag = data[i].Signal switch
                {
                    "牛转熊确认" or "天量破位转熊" => "转熊",
                    "熊转牛确认" => "转牛",
                    _ => "冰点"
                };
                signals.Add(new SignalMarker(i, data[i].Signal, $"{data[i].Date:yy-MM}{shortTag}"));
            }
        }

        var latest = data[^1];
        string regimeCn = RegimeLabel(latest.Regime);
        string flashTag = latest.IsProvisional ? $"【盘后快报 · 两融暂沿用 {Csv.Date(latest.MarginDate)}】  " : "";
        string banner = $"{flashTag}综合景气分 {latest.RawScore:F1}（5日 {latest.FastScore:F1} / 10日 {latest.SlowScore:F1}）  ｜  当前状态：{regimeCn}  ｜  " +
                        $"四维子分：趋势 {latest.TrendScore:F0} / 两融水位 {latest.MarginDdScore:F0} / 两融增速 {latest.MarginGrowthScore:F0} / 换手热度 {latest.TurnoverScore:F0}";

        using var outputs = new OutputBatch(dir);
        ReportCharts.RegimeScore(
            outputs.Stage(FileName + ".png"),
            data.Select(r => r.Date).ToArray(),
            data.Select(r => r.Index).ToArray(),
            data.Select(r => r.Ma60).ToArray(),
            data.Select(r => r.FastScore).ToArray(),
            data.Select(r => r.SlowScore).ToArray(),
            data.Select(r => r.Regime).ToArray(),
            banner,
            [.. signals]);

        Csv.Write(outputs.Stage(FileName + ".csv"), Header, data.Select(r => new[]
        {
            Csv.Date(r.Date),
            r.Index.ToString("F2"),
            r.Ma60.ToString("F2"),
            r.Ma120.ToString("F2"),
            r.IndexDd252.ToString("F2"),
            r.MarginBalance.ToString("F2"),
            r.MarginDd.ToString("F2"),
            r.MarginChg20.ToString("F2"),
            r.MarginChg60.ToString("F2"),
            r.TurnMa5.ToString("F3"),
            r.TurnMa20.ToString("F3"),
            r.DaysSincePeak.ToString(),
            r.DaysSinceTrough.ToString(),
            r.TrendScore.ToString("F1"),
            r.MarginDdScore.ToString("F1"),
            r.MarginGrowthScore.ToString("F1"),
            r.TurnoverScore.ToString("F1"),
            r.RawScore.ToString("F1"),
            r.FastScore.ToString("F1"),
            r.SlowScore.ToString("F1"),
            r.Regime,
            r.Signal,
            r.IsProvisional ? $"盘后快报(两融沿用{Csv.Date(r.MarginDate)})" : "正式"
        }));
        outputs.Commit();

        string modeNote = latest.IsProvisional ? $" [盘后快报·两融暂沿用{Csv.Date(latest.MarginDate)}]" : "";
        Console.WriteLine($"四维牛熊景气度 {Csv.Date(latest.Date)}{modeNote}  当日分 {latest.RawScore:F1}  5日均 {latest.FastScore:F1}  10日均 {latest.SlowScore:F1}  状态：{regimeCn}  共 {data.Length} 个交易日");
        Console.WriteLine($"四维子分：趋势(30%) {latest.TrendScore:F1} ｜ 两融水位(25%) {latest.MarginDdScore:F1} ｜ 两融真实增速(25%) {latest.MarginGrowthScore:F1} ｜ 换手热度(20%) {latest.TurnoverScore:F1}");
        Console.WriteLine($"操作建议：{BuildConsoleAdvice(latest)}");
    }

    public static Row[] ComputeFromOutputs(string outputDir)
    {
        string ddPath = Path.Combine(outputDir, "margin-drawdown-speed", "margin_drawdown_speed.csv");
        string cyPath = Path.Combine(outputDir, "chinext-turnover-cycle", "chinext_turnover_cycle.csv");
        if (!File.Exists(ddPath) || !File.Exists(cyPath))
            throw new FileNotFoundException("计算四维牛熊景气度需要先有两融回撤 CSV 与创业板换手率 CSV。");

        var ddTable = Csv.Read(ddPath);
        int ddDate = ddTable.Column("日期");
        int ddBal = ddTable.Column("两融余额_亿元");
        int ddPct = ddTable.Column("相对近一年高点回撤_百分比");

        var cyTable = Csv.Read(cyPath);
        int cyDate = cyTable.Column("日期");
        int cyIdx = cyTable.Column("创业板指");
        int cyMa5 = cyTable.Column("换手率MA5_百分比");
        int cyMa20 = cyTable.Column("换手率MA20_百分比");
        int cyPeak = cyTable.Column("距天量峰天数");
        int cyTrough = cyTable.Column("距地量谷天数");

        var cyMap = new Dictionary<DateOnly, (double Idx, double Ma5, double Ma20, int Peak, int Trough)>();
        foreach (var r in cyTable.Rows)
        {
            cyMap[Csv.ParseDate(r[cyDate])] = (
                Csv.Number(r[cyIdx]),
                Csv.Number(r[cyMa5]),
                Csv.Number(r[cyMa20]),
                int.Parse(r[cyPeak]),
                int.Parse(r[cyTrough]));
        }

        var joined = new List<(DateOnly Date, double Idx, double Bal, double MarginDd, double Ma5, double Ma20, int Peak, int Trough, bool IsProvisional, DateOnly MarginDate)>();
        foreach (var r in ddTable.Rows)
        {
            var d = Csv.ParseDate(r[ddDate]);
            if (!cyMap.TryGetValue(d, out var cy))
                continue;
            joined.Add((d, cy.Idx, Csv.Number(r[ddBal]), Csv.Number(r[ddPct]), cy.Ma5, cy.Ma20, cy.Peak, cy.Trough, false, d));
        }
        joined.Sort((a, b) => a.Date.CompareTo(b.Date));

        // T-day post-close flash preview: if ChiNext closed data is newer than T+1 margin disclosure,
        // carry forward the latest disclosed margin figures with an explicit provisional flag until T+1 calibration.
        if (joined.Count > 0)
        {
            var lastOfficial = joined[^1];
            var newerCyDates = cyMap.Keys.Where(d => d > lastOfficial.Date).Order().Take(2).ToArray();
            foreach (var d in newerCyDates)
            {
                var cy = cyMap[d];
                joined.Add((d, cy.Idx, lastOfficial.Bal, lastOfficial.MarginDd, cy.Ma5, cy.Ma20, cy.Peak, cy.Trough, true, lastOfficial.Date));
            }
        }

        int n = joined.Count;
        var ma60 = new double[n];
        var ma120 = new double[n];
        var idxDd252 = new double[n];
        var mChg20 = new double[n];
        var mChg60 = new double[n];
        var rawScore = new double[n];
        var fastScore = new double[n];
        var slowScore = new double[n];

        var pTrend = new double[n];
        var pMdd = new double[n];
        var pMchg = new double[n];
        var pVol = new double[n];

        double sum60 = 0, sum120 = 0;
        for (int i = 0; i < n; i++)
        {
            sum60 += joined[i].Idx;
            if (i >= 60) sum60 -= joined[i - 60].Idx;
            ma60[i] = sum60 / Math.Min(60, i + 1);

            sum120 += joined[i].Idx;
            if (i >= 120) sum120 -= joined[i - 120].Idx;
            ma120[i] = sum120 / Math.Min(120, i + 1);

            double hi252 = 0;
            for (int j = Math.Max(0, i - 251); j <= i; j++)
                hi252 = Math.Max(hi252, joined[j].Idx);
            idxDd252[i] = hi252 > 0 ? (joined[i].Idx / hi252 - 1.0) * 100.0 : 0.0;

            if (joined[i].IsProvisional && i > 0)
            {
                // Keep T-1 disclosed margin velocity unchanged during T-day post-close flash preview.
                mChg20[i] = mChg20[i - 1];
                mChg60[i] = mChg60[i - 1];
            }
            else
            {
                double bal20 = joined[Math.Max(0, i - 20)].Bal;
                double bal60 = joined[Math.Max(0, i - 60)].Bal;
                mChg20[i] = bal20 > 0 ? (joined[i].Bal / bal20 - 1.0) * 100.0 : 0.0;
                mChg60[i] = bal60 > 0 ? (joined[i].Bal / bal60 - 1.0) * 100.0 : 0.0;
            }

            double ma60Prev20 = ma60[Math.Max(0, i - 20)];
            double ma60Slope20 = ma60Prev20 > 0 ? (ma60[i] / ma60Prev20 - 1.0) * 100.0 : 0.0;

            double vs60 = ma60[i] > 0 ? (joined[i].Idx / ma60[i] - 1.0) * 100.0 : 0.0;
            double vs120 = ma120[i] > 0 ? (joined[i].Idx / ma120[i] - 1.0) * 100.0 : 0.0;

            // Pillar 1: Price Trend [0..1]
            double tr = 0.47 * Clamp01((vs60 + 8.0) / 16.0)
                      + 0.33 * Clamp01((vs120 + 10.0) / 20.0)
                      + 0.20 * Clamp01((ma60Slope20 + 4.0) / 8.0);

            // Pillar 2: Margin Drawdown Level [0..1] (-12% -> 0, 0% -> 1)
            double mdd = Clamp01((joined[i].MarginDd + 12.0) / 12.0);

            // Pillar 3: Real Margin Growth [0..1]
            double mg = 0.60 * Clamp01((mChg20[i] + 6.0) / 12.0)
                      + 0.40 * Clamp01((mChg60[i] + 10.0) / 20.0);

            // Pillar 4: ChiNext Turnover Heat & Peak Exhaustion [0..1]
            double vol = 0.70 * Clamp01((joined[i].Ma20 - 1.6) / 2.0)
                       + (joined[i].Ma5 >= joined[i].Ma20 ? 0.30 : 0.10);
            if (joined[i].Peak >= 45)
                vol -= Math.Min(0.50, (joined[i].Peak - 40) * 0.006);
            vol = Clamp01(vol);

            pTrend[i] = tr * 100.0;
            pMdd[i] = mdd * 100.0;
            pMchg[i] = mg * 100.0;
            pVol[i] = vol * 100.0;

            rawScore[i] = (WeightTrend * tr + WeightMarginDd * mdd + WeightMarginGrowth * mg + WeightTurnover * vol);

            double fSum = 0;
            int fCnt = Math.Min(FastSmoothDays, i + 1);
            for (int j = i - fCnt + 1; j <= i; j++) fSum += rawScore[j];
            fastScore[i] = fSum / fCnt;

            double sSum = 0;
            int sCnt = Math.Min(SlowSmoothDays, i + 1);
            for (int j = i - sCnt + 1; j <= i; j++) sSum += rawScore[j];
            slowScore[i] = sSum / sCnt;
        }

        // Strictly causal A-2.0 (V2-2) state machine:
        // 4D composite score + current-bull 95-day climax early warning + fast MA60/55 break exit + left-side ice zone.
        var rows = new Row[n];
        string cur = "BEAR";
        int iceDays = 0;
        int bullStart = -1;
        int usedPeakOrigin = -1;
        bool climaxArmed = false;
        int belowMa60Days = 0;
        int climaxBearIdx = -999;

        for (int i = 0; i < n; i++)
        {
            double sf = fastScore[i];
            double ss = slowScore[i];
            bool aboveMa60 = joined[i].Idx > ma60[i];
            belowMa60Days = aboveMa60 ? 0 : belowMa60Days + 1;

            int peakOrigin = joined[i].Peak > 0 ? i - joined[i].Peak : -1;
            bool validClimax95 = bullStart >= 0
                && peakOrigin >= bullStart
                && peakOrigin != usedPeakOrigin
                && joined[i].Peak >= ClimaxEarlyWarnDays;

            bool inIce = idxDd252[i] <= -25.0
                && joined[i].MarginDd <= -12.0
                && mChg20[i] >= -2.5
                && (joined[i].Ma5 <= 2.0 || joined[i].Trough >= 50);
            iceDays = inIce ? iceDays + 1 : 0;

            string sig = "";
            if (cur == "BULL")
            {
                if (ss < BearThreshold)
                {
                    cur = "BEAR";
                    climaxArmed = false;
                    sig = "牛转熊确认";
                }
                else if (validClimax95)
                {
                    cur = "WARN";
                    climaxArmed = true;
                    usedPeakOrigin = peakOrigin;
                    sig = "天量退潮预警";
                }
                else if (sf < WarnThreshold)
                {
                    cur = "WARN";
                    climaxArmed = false;
                    sig = "牛转熊预警";
                }
            }
            else if (cur == "WARN")
            {
                bool fastBear = climaxArmed && (belowMa60Days >= 2 || sf < ClimaxFastBearThreshold);
                if (ss < BearThreshold || fastBear)
                {
                    cur = "BEAR";
                    if (fastBear)
                        climaxBearIdx = i;
                    climaxArmed = false;
                    sig = fastBear ? "天量破位转熊" : "牛转熊确认";
                }
                else if (!climaxArmed && ss >= RecoverThreshold && aboveMa60)
                {
                    cur = "BULL";
                    sig = "解除预警";
                }
                else if (climaxArmed && joined[i].Peak == 0)
                {
                    cur = "BULL";
                    climaxArmed = false;
                    sig = "解除预警";
                }
            }
            else if (cur == "BEAR")
            {
                if (ss < WarnThreshold)
                    climaxBearIdx = -999;
                bool canReenter = i - climaxBearIdx >= ClimaxReenterCooldownDays;
                if (canReenter && ss >= BullThreshold && aboveMa60 && mChg20[i] > 0)
                {
                    cur = "BULL";
                    bullStart = i;
                    climaxArmed = false;
                    sig = "熊转牛确认";
                }
                else if (iceDays >= 5)
                {
                    cur = "BOTTOM";
                    sig = "冰点筑底区";
                }
            }
            else if (cur == "BOTTOM")
            {
                if (ss >= BullThreshold && aboveMa60 && mChg20[i] > 0)
                {
                    cur = "BULL";
                    bullStart = i;
                    climaxArmed = false;
                    sig = "熊转牛确认";
                }
                else if (mChg20[i] <= -5.0 && !aboveMa60)
                {
                    cur = "BEAR";
                    sig = "筑底中止";
                }
            }

            rows[i] = new Row(
                joined[i].Date,
                joined[i].Idx,
                ma60[i],
                ma120[i],
                idxDd252[i],
                joined[i].Bal,
                joined[i].MarginDd,
                mChg20[i],
                mChg60[i],
                joined[i].Ma5,
                joined[i].Ma20,
                joined[i].Peak,
                joined[i].Trough,
                pTrend[i],
                pMdd[i],
                pMchg[i],
                pVol[i],
                rawScore[i],
                fastScore[i],
                slowScore[i],
                cur,
                sig,
                joined[i].IsProvisional,
                joined[i].MarginDate,
                climaxArmed);
        }

        return rows;
    }

    public static string RegimeLabel(string regime) => regime switch
    {
        "BULL" => "右侧牛市（趋势扩张）",
        "WARN" => "牛转熊预警（顶部退潮）",
        "BOTTOM" => "左侧冰点筑底区（勿割肉）",
        _ => "熊市退潮（严控风险）"
    };

    private static string BuildConsoleAdvice(Row r) => r.Regime switch
    {
        "BULL" => "参考仓位 70%-100%：趋势与杠杆共振扩张，顺势持有成长/宽基，警惕 5日景气分跌破 50 预警。",
        "WARN" => "参考仓位 30%-50%：顶部退潮或杠杆松动，逢反抽降仓防回撤、不加杠杆；若 10日分跌破 36 则全面转防守。",
        "BOTTOM" => "参考仓位 20%-40%：已深跌且两融止血进入地量冰点，切忌割肉，可分批左侧定投宽基，静候 10日分 ≥ 65 右侧加仓。",
        _ => $"参考仓位 0%-20%：现金/红利低波防守，严禁重仓接飞刀；先观察两融20日增速({r.MarginChg20:+0.0;-0.0}%)止血至 ≥-2.5% 且换手MA5({r.TurnMa5:F2}%)缩至 ≤2.0%。"
    };

    private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));
}
