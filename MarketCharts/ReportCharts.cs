using SkiaSharp;

internal static class ReportCharts
{
    private static readonly SKColor Ink = SKColor.Parse("#26323a");
    private static readonly SKColor Grid = SKColor.Parse("#e3e7e9");
    private static readonly SKColor Zero = SKColor.Parse("#8a9499");
    private static readonly SKColor Gold = SKColor.Parse("#b67b00");
    private static readonly SKColor Violet = SKColor.Parse("#8055aa");
    private static readonly SKColor Positive = SKColor.Parse("#6aa99d");
    private static readonly SKColor Negative = SKColor.Parse("#d88b8b");
    private static readonly SKColor Red = SKColor.Parse("#8d1115");
    private static readonly SKColor Orange = SKColor.Parse("#e77b19");
    private static readonly SKColor Blue = SKColor.Parse("#2e6894");

    private static readonly SKColor[] IndustryColors =
        ("#1f77b4 #aec7e8 #ff7f0e #ffbb78 #2ca02c #98df8a #d62728 #ff9896 #9467bd #c5b0d5 " +
        "#8c564b #c49c94 #e377c2 #f7b6d2 #7f7f7f #c7c7c7 #bcbd22 #dbdb8d #17becf #9edae5 " +
        "#393b79 #5254a3 #6b6ecf #9c9ede #637939 #8ca252 #b5cf6b #cedb9c #8c6d31 #bd9e39 #e7ba52")
        .Split(' ').Select(SKColor.Parse).ToArray();

    public static void Industry(string path, DateOnly start, DateOnly[] dates, (string Name, double[] Values)[] series)
    {
        using var chart = new Chart(dates, 1440, 810, new SKRect(102, 78, 1397, 624),
            endPaddingDays: 4, start: start);
        chart.Title($"申万一级行业累计融资净买入（{Csv.Date(start)} 至 {Csv.Date(dates[^1])}）");
        var axis = Axis.Fit(series.SelectMany(item => item.Values).Append(0), "自2025年初累计（亿元）");
        chart.Axis(axis, chart.Plot.Left, right: false, grid: true);
        chart.Dates(months: 1, format: "yyyy/MM", rotate: true);
        chart.ZeroLine(axis);
        for (int i = series.Length - 1; i >= 0; i--)
        {
            var color = IndustryColors[i % IndustryColors.Length];
            chart.Series(series[i].Values, axis, color.WithAlpha(i < 5 ? (byte)255 : (byte)200), i < 5 ? 2.3f : 1.3f);
            if (i < 5)
                chart.Markers(series[i].Values, axis, color, i);
        }
        chart.Legend(series.Select((item, i) => (item.Name, IndustryColors[i % IndustryColors.Length])).ToArray(),
            x: 103, y: 694, columns: 8, columnWidth: 163);
        chart.Save(path);
    }

    public static void Tech(string path, DateOnly[] dates, double[] growth, double[] dividend, double[] spread, int peak)
    {
        using var chart = new Chart(dates, 1440, 760, new SKRect(102, 80, 1322, 625), endPaddingDays: 17);
        chart.Title($"2026年以来，科创50与创业板各半对比红利低波（截至 {Csv.Date(dates[^1])}）");
        var axis = Axis.Fit(growth.Concat(dividend).Concat(spread).Append(0), "累计收益率", multiplier: 100, suffix: "%");
        chart.Axis(axis, chart.Plot.Left, right: false, grid: true);
        chart.Axis(axis with { Label = "差值（百分点）", Suffix = "" }, chart.Plot.Right, right: true);
        chart.Dates(months: 1, format: "yyyy/MM");
        chart.Bars(spread, axis, Positive.WithAlpha(130), Negative.WithAlpha(130));
        chart.ZeroLine(axis, dashed: true);
        chart.Series(growth, axis, Gold, 3);
        chart.Series(dividend, axis, Violet, 3);
        chart.PointLabel(dates.Length - 1, growth[^1], axis, Gold, $"{Csv.Signed(growth[^1] * 100)}%", -9);
        chart.PointLabel(dates.Length - 1, dividend[^1], axis, Violet, $"{Csv.Signed(dividend[^1] * 100)}%", 19);
        chart.PointLabel(peak, growth[peak], axis, Gold, $"{Csv.Date(dates[peak])}  {Csv.Signed(growth[peak] * 100)}%", -14,
            alignLeft: true);
        chart.Legend([("科创50ETF华夏 + 创业板ETF易方达（各50%）", Gold),
            ("红利低波ETF华泰柏瑞", Violet), ("差值（正值为绿，负值为粉）", Positive)],
            x: 152, y: 704, columns: 3, columnWidth: 432);
        chart.Save(path);
    }

    public static void Drawdown(string path, DateOnly[] dates, double[] drawdown, double[] speed, double[] index)
    {
        using var chart = new Chart(dates, 1440, 760, new SKRect(100, 80, 1194, 625), endPaddingDays: 120);
        chart.Title($"两融余额回撤、20日变化与创业板指（2016-01-01 至 {Csv.Date(dates[^1])}）");
        var left = Axis.Fit(drawdown.Concat([-60d, 3d]), "回撤", suffix: "%") with
        {
            Low = Math.Min(-60, drawdown.Min() - 3), High = 3
        };
        var right = Axis.Fit(speed.Concat([-16d, 25d]), "20交易日回撤变化（百分点）") with
        {
            Low = Math.Min(-16, speed.Min() - 2), High = Math.Max(25, speed.Max() + 2)
        };
        var indexAxis = Axis.Fit(index, "创业板指（点）", color: Blue);
        chart.Axis(left, chart.Plot.Left, right: false, grid: true);
        chart.Axis(right, chart.Plot.Right, right: true);
        chart.Axis(indexAxis, 1328, right: true);
        chart.Dates(months: 12, format: "yyyy");
        chart.Area(drawdown, left, Red.WithAlpha(39));
        chart.ZeroLine(right, dashed: true);
        chart.Series(index, indexAxis, Blue.WithAlpha(215), 1.4f);
        chart.Series(speed, right, Orange.WithAlpha(218), 1.15f);
        chart.Series(drawdown, left, Red, 1.65f);
        chart.PointLabel(dates.Length - 1, drawdown[^1], left, Red,
            $"{Csv.Date(dates[^1])}  回撤 {drawdown[^1]:F2}%  20日变化 {Csv.Signed(speed[^1])} 个百分点",
            25, alignLeft: true);
        chart.Legend([("相对近一年高点回撤", Red), ("回撤较20个交易日前变化", Orange), ("创业板指收盘点位", Blue)],
            x: 190, y: 701, columns: 3, columnWidth: 340);
        chart.Save(path);
    }

    public static void Turnover(string path, DateOnly[] dates, double[] turnover, double[] ma5, double[] ma20,
        double[] shIndex, string timerBanner, TurnoverReport.SignalMarker[] signals)
    {
        using var chart = new Chart(dates, 1520, 840, new SKRect(102, 110, 1396, 682), endPaddingDays: 95);
        chart.Title($"全A流通换手率与上证指数：市场顶底温度计（{Csv.Date(dates[0])} 至 {Csv.Date(dates[^1])}）");
        chart.Caption(timerBanner, y: 78);
        double maxTurn = Math.Max(4.8, Math.Ceiling(turnover.Max() * 2) / 2.0);
        var left = new Axis(0, maxTurn, 0.5, "全A流通换手率（成交额÷流通市值）", 1, "%", Ink);
        var right = Axis.Fit(shIndex, "上证指数（点）", color: Blue);

        // Temperature bands: overheat (>=3.0%) and cold trough (<=1.0%)
        chart.Band(3.0, left.High, left, Red.WithAlpha(24));
        chart.Band(0, 1.0, left, Positive.WithAlpha(32));
        chart.Axis(left, chart.Plot.Left, right: false, grid: true);
        chart.Axis(right, chart.Plot.Right, right: true);
        chart.Dates(months: 12, format: "yyyy");

        chart.Bars(turnover, left, Orange.WithAlpha(78), Orange.WithAlpha(78), widthInDays: 1.4f);
        chart.Series(shIndex, right, Blue.WithAlpha(235), 1.9f);
        chart.Series(ma5, left, Orange.WithAlpha(210), 1.35f);
        chart.Series(ma20, left, Red, 2.2f);
        chart.HLine(3.0, left, Red.WithAlpha(200), dashed: true, label: "过热警戒 3.0%（天量勿追）");
        chart.HLine(1.0, left, SKColor.Parse("#167a58"), dashed: true, label: "低谷冷清 1.0%（地量勿割）");
        chart.HLine(0.6, left, SKColor.Parse("#167a58").WithAlpha(160), dashed: true);

        // Draw cycle confirmation markers on Shanghai Composite (right axis)
        foreach (var sig in signals)
        {
            bool isBear = sig.Kind.StartsWith("牛转熊");
            var color = isBear ? SKColor.Parse("#b82323") : SKColor.Parse("#167a58");
            chart.PointLabel(sig.Index, shIndex[sig.Index], right, color, sig.Label, isBear ? -14 : 20, alignLeft: sig.Index > dates.Length - 220);
        }

        // Annotate classic historical extremes if present
        (string DateStr, string Tag, float Dy, bool LeftAlign)[] anchors =
        [
            ("2012-11-26", "12-11地量", -14, false),
            ("2015-05-28", "15-05天量", -12, false),
            ("2018-09-17", "18-09地量", -14, false),
            ("2024-08-13", "24-08地量", 18, true),
            ("2024-10-08", "24-10天量", -12, true),
            ("2026-01-12", "26-01天量", -12, true)
        ];
        var dateIndex = dates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        foreach (var (dateStr, tag, dy, alignLeft) in anchors)
        {
            if (DateOnly.TryParse(dateStr, out var anchorDate) && dateIndex.TryGetValue(anchorDate, out int idx))
            {
                bool isHot = turnover[idx] >= 2.5;
                chart.PointLabel(idx, turnover[idx], left, isHot ? Red : SKColor.Parse("#1a8a63"),
                    $"{tag} {turnover[idx]:F2}%", dy, alignLeft);
            }
        }

        chart.PointLabel(dates.Length - 1, ma20[^1], left, Red,
            $"最新 {turnover[^1]:F2}% / MA20 {ma20[^1]:F2}%", 20, alignLeft: true);

        chart.Legend([
            ("全A日换手率（左轴柱）", Orange.WithAlpha(140)),
            ("换手率 MA5", Orange),
            ("换手率 MA20（主温度线）", Red),
            ("上证指数（右轴）", Blue)
        ], x: 140, y: 752, columns: 4, columnWidth: 318, barEntry: 0);
        chart.Caption("规则：MA5≥2.2%(MA20≥1.8%)峰后40日未超+破60线=牛转熊预警、95日未超=牛转熊确认；MA5≤0.90%(已跌≥10%)谷后75日未破=筑底确认", y: 800);
        chart.Save(path);
    }

    public static void ChinextTurnover(string path, DateOnly[] dates, double[] turnover, double[] ma5, double[] ma20,
        double[] chinextIndex, string timerBanner, TurnoverReport.SignalMarker[] signals)
    {
        using var chart = new Chart(dates, 1520, 840, new SKRect(102, 110, 1396, 682), endPaddingDays: 95);
        chart.Title($"创业板流通换手率与创业板指：顶底温度计（{Csv.Date(dates[0])} 至 {Csv.Date(dates[^1])}）");
        chart.Caption(timerBanner, y: 78);
        double maxTurn = Math.Max(10.0, Math.Ceiling(turnover.Max()));
        var left = new Axis(0, maxTurn, 1.0, "创业板流通换手率（成交额÷流通市值）", 1, "%", Ink);
        var right = Axis.Fit(chinextIndex, "创业板指（点）", color: Blue);

        chart.Band(5.0, left.High, left, Red.WithAlpha(24));
        chart.Band(0, 2.0, left, Positive.WithAlpha(32));
        chart.Axis(left, chart.Plot.Left, right: false, grid: true);
        chart.Axis(right, chart.Plot.Right, right: true);
        chart.Dates(months: 12, format: "yyyy");

        chart.Bars(turnover, left, Orange.WithAlpha(78), Orange.WithAlpha(78), widthInDays: 1.4f);
        chart.Series(chinextIndex, right, Blue.WithAlpha(235), 1.9f);
        chart.Series(ma5, left, Orange.WithAlpha(210), 1.35f);
        chart.Series(ma20, left, Red, 2.2f);
        chart.HLine(5.0, left, Red.WithAlpha(200), dashed: true, label: "过热警戒 5.0%（天量勿追）");
        chart.HLine(2.0, left, SKColor.Parse("#167a58"), dashed: true, label: "低谷冷清 2.0%（地量勿割）");
        chart.HLine(1.6, left, SKColor.Parse("#167a58").WithAlpha(160), dashed: true);

        foreach (var sig in signals)
        {
            bool isBear = sig.Kind.StartsWith("牛转熊");
            var color = isBear ? SKColor.Parse("#b82323") : SKColor.Parse("#167a58");
            float dy = isBear || dates[sig.Index].Year == 2024 ? -14 : 20;
            chart.PointLabel(sig.Index, chinextIndex[sig.Index], right, color, sig.Label, dy, alignLeft: sig.Index > dates.Length - 70);
        }

        (string DateStr, string Tag, float Dy, bool LeftAlign)[] anchors =
        [
            ("2015-05-28", "15-05天量", -12, false),
            ("2018-09-17", "18-09地量", -14, false),
            ("2022-10-11", "22-10地量", 18, true),
            ("2024-08-13", "24-08地量", 18, false),
            ("2024-10-08", "24-10天量", -12, true),
            ("2026-01-14", "26-01天量", -14, true)
        ];
        var dateIndex = dates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        foreach (var (dateStr, tag, dy, alignLeft) in anchors)
        {
            if (DateOnly.TryParse(dateStr, out var anchorDate) && dateIndex.TryGetValue(anchorDate, out int idx))
            {
                bool isHot = turnover[idx] >= 4.5;
                chart.PointLabel(idx, turnover[idx], left, isHot ? Red : SKColor.Parse("#1a8a63"),
                    $"{tag} {turnover[idx]:F2}%", dy, alignLeft);
            }
        }

        chart.PointLabel(dates.Length - 1, ma20[^1], left, Red,
            $"最新 {turnover[^1]:F2}% / MA20 {ma20[^1]:F2}%", 20, alignLeft: true);

        chart.Legend([
            ("创业板日换手率（左轴柱）", Orange.WithAlpha(140)),
            ("换手率 MA5", Orange),
            ("换手率 MA20（主温度线）", Red),
            ("创业板指（右轴）", Blue)
        ], x: 140, y: 752, columns: 4, columnWidth: 318, barEntry: 0);
        chart.Caption("规则：MA5≥5.0%(MA20≥4.0%)峰后40日未超+破60线=牛转熊预警、95日未超=牛转熊确认；MA5≤2.0%(已跌≥20%)谷后75日未破=筑底确认", y: 800);
        chart.Save(path);
    }

    public static void RegimeScore(string path, DateOnly[] dates, double[] chinextIndex, double[] ma60,
        double[] fastScore, double[] slowScore, string[] regimes, string banner, RegimeScoreReport.SignalMarker[] signals)
    {
        using var chart = new Chart(dates, 1520, 840, new SKRect(102, 110, 1396, 682), endPaddingDays: 80);
        chart.Title($"创业板四维牛熊景气度仪表盘（{Csv.Date(dates[0])} 至 {Csv.Date(dates[^1])}）");
        chart.Caption(banner, y: 78);

        var left = new Axis(0, 100, 10, "四维综合景气分（0-100）", 1, "", Ink);
        var right = Axis.Fit(chinextIndex, "创业板指（点）", color: Blue);

        chart.RegimeBands(regimes);
        chart.Axis(left, chart.Plot.Left, right: false, grid: true);
        chart.Axis(right, chart.Plot.Right, right: true);
        chart.Dates(months: 12, format: "yyyy");

        chart.HLine(RegimeScoreReport.BullThreshold, left, SKColor.Parse("#167a58"), dashed: true, label: "65 右侧转牛线");
        chart.HLine(RegimeScoreReport.WarnThreshold, left, Orange, dashed: true, label: "50 预警线");
        chart.HLine(RegimeScoreReport.BearThreshold, left, Red, dashed: true, label: "36 转熊确认线");

        chart.Series(fastScore, left, Orange.WithAlpha(185), 1.35f);
        chart.Series(slowScore, left, Red, 2.25f);
        chart.Series(ma60, right, Blue.WithAlpha(110), 1.3f);
        chart.Series(chinextIndex, right, Blue.WithAlpha(240), 2.0f);

        int prevIdx = -999;
        for (int k = 0; k < signals.Length; k++)
        {
            var sig = signals[k];
            var color = sig.Kind switch
            {
                "牛转熊确认" => SKColor.Parse("#b82323"),
                "熊转牛确认" => SKColor.Parse("#15803d"),
                _ => SKColor.Parse("#0d7490")
            };
            bool crowded = sig.Index - prevIdx < 160;
            bool alignLeft = sig.Index > dates.Length - 80 || (crowded && k % 2 == 1);
            float dy = sig.Kind switch
            {
                "牛转熊确认" => crowded ? -26 : -14,
                "冰点筑底区" => crowded ? 32 : 20,
                _ => crowded ? -14 : 20
            };
            if (dates[sig.Index].Year == 2024 && sig.Kind == "冰点筑底区")
            {
                alignLeft = true;
                dy = 20;
            }
            chart.PointLabel(sig.Index, chinextIndex[sig.Index], right, color, sig.Label, dy, alignLeft);
            prevIdx = sig.Index;
        }

        chart.PointLabel(dates.Length - 1, slowScore[^1], left, Red,
            $"最新 10日 {slowScore[^1]:F1} / 5日 {fastScore[^1]:F1}", -18, alignLeft: true);

        chart.Legend([
            ("景气分 5日均（预警快线）", Orange),
            ("景气分 10日均（确认主线）", Red),
            ("创业板指（右轴）", Blue),
            ("创业板指 MA60", Blue.WithAlpha(120))
        ], x: 140, y: 752, columns: 4, columnWidth: 318);
        chart.Caption("四维权重：趋势30% + 两融回撤水位25% + 两融真实增速25% + 创业板换手热度20%（背景色带：绿=牛市 黄=预警 红=熊市 青=冰点）", y: 800);
        chart.Save(path);
    }

    private sealed record Axis(double Low, double High, double Step, string Label, double Multiplier, string Suffix, SKColor Color)
    {
        public static Axis Fit(IEnumerable<double> values, string label, double multiplier = 1,
            string suffix = "", SKColor? color = null)
        {
            var data = values.ToArray();
            double low = data.Min();
            double high = data.Max();
            double range = high - low;
            double approx = range > 0 ? range / 6 : Math.Max(1, Math.Abs(high) * 0.1);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(approx)));
            double step = new[] { 1d, 2d, 2.5, 5d, 10d }.First(factor => factor * magnitude >= approx) * magnitude;
            return new Axis(Math.Floor((low - range * .03) / step) * step,
                Math.Ceiling((high + (range > 0 ? range * .05 : step)) / step) * step,
                step, label, multiplier, suffix, color ?? Ink);
        }
    }

    private sealed class Chart : IDisposable
    {
        private const int Scale = 2;
        private readonly SKSurface surface;
        private readonly SKCanvas canvas;
        private readonly SKTypeface font;
        private readonly DateOnly[] dates;
        private readonly DateOnly start;
        private readonly int dateSpan;
        private readonly int width;
        public SKRect Plot { get; }

        public Chart(DateOnly[] dates, int width, int height, SKRect plot, int endPaddingDays, DateOnly? start = null)
        {
            this.dates = dates;
            this.start = start ?? dates[0];
            this.width = width;
            Plot = plot;
            dateSpan = dates[^1].DayNumber - this.start.DayNumber + endPaddingDays;
            font = SKFontManager.Default.MatchCharacter('融')
                ?? throw new InvalidOperationException("系统缺少中文字体，无法生成图表。");
            surface = SKSurface.Create(new SKImageInfo(width * Scale, height * Scale))
                ?? throw new InvalidOperationException("无法创建绘图画布。");
            canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.Scale(Scale);
        }

        private float X(DateOnly date) => Plot.Left + Plot.Width * (date.DayNumber - start.DayNumber) / dateSpan;
        private float Y(double value, Axis axis) => Plot.Bottom - Plot.Height * (float)((value - axis.Low) / (axis.High - axis.Low));

        public void Title(string title) => Text(title, width / 2f, 45, 27, Ink, SKTextAlign.Center);

        public void Caption(string text, float y) => Text(text, width / 2f, y, 17, Ink, SKTextAlign.Center);

        public void Axis(Axis axis, float x, bool right, bool grid = false)
        {
            float direction = right ? 1 : -1;
            for (double value = Math.Ceiling(axis.Low / axis.Step - 1e-8) * axis.Step;
                value <= axis.High + axis.Step * 1e-8; value += axis.Step)
            {
                float y = Y(value, axis);
                if (grid)
                    Line(Plot.Left, y, Plot.Right, y, Grid);
                Line(x, y, x + direction * 5, y, axis.Color);
                double labelValue = Math.Abs(value) < axis.Step * 1e-8 ? 0 : value * axis.Multiplier;
                Text($"{labelValue:0.##}{axis.Suffix}", x + direction * 9, y + 5, 15,
                    axis.Color, right ? SKTextAlign.Left : SKTextAlign.Right);
            }
            Line(x, Plot.Top, x, Plot.Bottom, axis.Color);
            canvas.Save();
            canvas.Translate(x + direction * (right ? 68 : 69), Plot.MidY);
            canvas.RotateDegrees(right ? 90 : -90);
            Text(axis.Label, 0, 0, 17, axis.Color, SKTextAlign.Center);
            canvas.Restore();
        }

        public void Dates(int months, string format, bool rotate = false)
        {
            var first = new DateOnly(start.Year, months == 12 ? 1 : start.Month, 1);
            for (var date = first; X(date) <= Plot.Right - 12; date = date.AddMonths(months))
            {
                float x = X(date);
                if (x < Plot.Left - 12)
                    continue;
                x = Math.Max(x, Plot.Left);
                Line(x, Plot.Top, x, Plot.Bottom, Grid, .7f);
                if (rotate)
                {
                    canvas.Save();
                    canvas.Translate(x + 5, Plot.Bottom + 12);
                    canvas.RotateDegrees(-45);
                    Text(date.ToString(format), 0, 0, 15, Ink, SKTextAlign.Right);
                    canvas.Restore();
                }
                else
                    Text(date.ToString(format), x, Plot.Bottom + 24, 15, Ink, SKTextAlign.Center);
            }
            Line(Plot.Left, Plot.Bottom, Plot.Right, Plot.Bottom, Ink);
        }

        public void ZeroLine(Axis axis, bool dashed = false)
        {
            float y = Y(0, axis);
            using var effect = dashed ? SKPathEffect.CreateDash([5, 4], 0) : null;
            using var paint = Paint(Zero, 1);
            paint.PathEffect = effect;
            canvas.DrawLine(Plot.Left, y, Plot.Right, y, paint);
        }

        public void Band(double low, double high, Axis axis, SKColor color)
        {
            float top = Math.Clamp(Y(high, axis), Plot.Top, Plot.Bottom);
            float bottom = Math.Clamp(Y(low, axis), Plot.Top, Plot.Bottom);
            if (bottom <= top)
                return;
            using var paint = Paint(color);
            canvas.DrawRect(Plot.Left, top, Plot.Width, bottom - top, paint);
        }

        public void RegimeBands(string[] regimes)
        {
            if (regimes.Length == 0)
                return;
            int segStart = 0;
            for (int i = 1; i <= regimes.Length; i++)
            {
                if (i == regimes.Length || regimes[i] != regimes[segStart])
                {
                    var color = regimes[segStart] switch
                    {
                        "BULL" => SKColor.Parse("#16a34a").WithAlpha(24),
                        "WARN" => SKColor.Parse("#d97706").WithAlpha(32),
                        "BOTTOM" => SKColor.Parse("#0891b2").WithAlpha(34),
                        _ => SKColor.Parse("#dc2626").WithAlpha(22)
                    };
                    float x0 = Math.Max(Plot.Left, X(dates[segStart]));
                    float x1 = Math.Min(Plot.Right, i < regimes.Length ? X(dates[i]) : X(dates[^1]));
                    if (x1 > x0)
                    {
                        using var paint = Paint(color);
                        canvas.DrawRect(x0, Plot.Top, x1 - x0, Plot.Height, paint);
                    }
                    segStart = i;
                }
            }
        }

        public void HLine(double value, Axis axis, SKColor color, bool dashed = true, string? label = null)
        {
            float y = Y(value, axis);
            if (y < Plot.Top || y > Plot.Bottom)
                return;
            using var effect = dashed ? SKPathEffect.CreateDash([6, 4], 0) : null;
            using var paint = Paint(color, 1.25f);
            paint.PathEffect = effect;
            canvas.DrawLine(Plot.Left, y, Plot.Right, y, paint);
            if (!string.IsNullOrEmpty(label))
            {
                const float size = 13.5f;
                using var labelFont = new SKFont(font, size);
                float labelWidth = labelFont.MeasureText(label);
                float left = Plot.Left + 10;
                float baseline = y - 6;
                using var bg = Paint(SKColors.White.WithAlpha(225));
                canvas.DrawRect(left - 4, baseline - size, labelWidth + 8, size + 5, bg);
                Text(label, left, baseline, size, color);
            }
        }

        public void Series(double[] values, Axis axis, SKColor color, float stroke)
        {
            using var path = Trace(values, axis);
            using var paint = Paint(color, stroke);
            canvas.DrawPath(path, paint);
        }

        public void Area(double[] values, Axis axis, SKColor color)
        {
            using var path = Trace(values, axis);
            path.LineTo(X(dates[^1]), Y(0, axis));
            path.LineTo(X(dates[0]), Y(0, axis));
            path.Close();
            using var paint = Paint(color);
            canvas.DrawPath(path, paint);
        }

        public void Bars(double[] values, Axis axis, SKColor positive, SKColor negative, float widthInDays = 1.8f)
        {
            using var up = Paint(positive);
            using var down = Paint(negative);
            float halfWidth = Plot.Width / dateSpan * widthInDays / 2;
            float zero = Y(0, axis);
            for (int i = 0; i < values.Length; i++)
            {
                float x = X(dates[i]);
                float y = Y(values[i], axis);
                canvas.DrawRect(Math.Max(Plot.Left, x - halfWidth), Math.Min(y, zero),
                    x + halfWidth - Math.Max(Plot.Left, x - halfWidth), Math.Abs(y - zero),
                    values[i] >= 0 ? up : down);
            }
        }

        public void Markers(double[] values, Axis axis, SKColor color, int shape)
        {
            using var paint = Paint(color);
            for (int i = 0; i < values.Length; i += Math.Max(1, values.Length / 10))
            {
                float x = X(dates[i]);
                float y = Y(values[i], axis);
                if (shape == 0)
                {
                    canvas.DrawCircle(x, y, 3.4f, paint);
                    continue;
                }
                using var marker = new SKPath();
                int sides = shape is 1 or 4 ? 3 : 4;
                double rotation = shape switch { 2 => Math.PI / 4, 4 => Math.PI / 2, _ => -Math.PI / 2 };
                for (int j = 0; j < sides; j++)
                {
                    double angle = rotation + 2 * Math.PI * j / sides;
                    var point = new SKPoint(x + 4 * (float)Math.Cos(angle), y + 4 * (float)Math.Sin(angle));
                    if (j == 0)
                        marker.MoveTo(point);
                    else
                        marker.LineTo(point);
                }
                marker.Close();
                canvas.DrawPath(marker, paint);
            }
        }

        public void PointLabel(int i, double value, Axis axis, SKColor color, string label, float dy, bool alignLeft = false)
        {
            float x = X(dates[i]);
            float y = Y(value, axis);
            using var marker = Paint(color);
            canvas.DrawCircle(x, y, 3.8f, marker);

            const float size = 15;
            using var labelFont = new SKFont(font, size);
            float labelWidth = labelFont.MeasureText(label);
            float left = Math.Clamp(alignLeft ? x - labelWidth - 10 : x + 9, Plot.Left, Plot.Right - labelWidth);
            float baseline = Math.Clamp(y + dy, Plot.Top + size, Plot.Bottom - 3);
            using var background = Paint(SKColors.White.WithAlpha(228));
            canvas.DrawRect(left - 3, baseline - size, labelWidth + 6, size + 5, background);
            Text(label, left, baseline, size, color);
        }

        public void Legend((string Label, SKColor Color)[] entries, float x, float y, int columns, float columnWidth,
            int barEntry = -1)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var (label, color) = entries[i];
                float left = x + i % columns * columnWidth;
                float baseline = y + i / columns * 24;
                if (i == barEntry)
                {
                    using var paint = Paint(color);
                    canvas.DrawRect(left, baseline - 13, 22, 13, paint);
                }
                else
                    Line(left, baseline - 5, left + 24, baseline - 5, color, 2.6f);
                Text(label, left + 32, baseline, 16, Ink);
            }
        }

        private SKPath Trace(double[] values, Axis axis)
        {
            var path = new SKPath();
            path.MoveTo(X(dates[0]), Y(values[0], axis));
            for (int i = 1; i < values.Length; i++)
                path.LineTo(X(dates[i]), Y(values[i], axis));
            return path;
        }

        private void Line(float x1, float y1, float x2, float y2, SKColor color, float stroke = 1)
        {
            using var paint = Paint(color, stroke);
            canvas.DrawLine(x1, y1, x2, y2, paint);
        }

        private void Text(string text, float x, float y, float size, SKColor color, SKTextAlign align = SKTextAlign.Left)
        {
            using var face = new SKFont(font, size);
            using var paint = Paint(color);
            canvas.DrawText(text, x, y, align, face, paint);
        }

        private static SKPaint Paint(SKColor color, float stroke = 0) => new()
        {
            Color = color,
            IsAntialias = true,
            Style = stroke > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = stroke
        };

        public void Save(string path)
        {
            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("无法编码 PNG 图表。");
            using var file = File.Create(path);
            encoded.SaveTo(file);
        }

        public void Dispose()
        {
            surface.Dispose();
            font.Dispose();
        }
    }
}
