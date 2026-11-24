using System.Globalization;
using System.Security.Cryptography;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

const string usage = """
    dotnet run --project MarketCharts -- <industry|tech|drawdown|turnover|chinext|all|everything|dashboard> [--from-csv] [--output-dir PATH]
    industry --skip-update  读取现有行业 CSV，打印日报
    dashboard               用现有 CSV 重新生成 outputs/dashboard.html 交互看板
    --from-csv              用本地数据重绘 PNG 与交互看板
    --output-dir PATH       指定存放图表及其 CSV 的 outputs 目录
    """;

if (args.Length == 0 || args is ["--help"] or ["-h"])
{
    Console.WriteLine(usage);
    return 0;
}

try
{
    string command = args[0] switch
    {
        "industry" or "行业融资图" => "industry",
        "tech" or "成长红利对比图" => "tech",
        "drawdown" or "两融回撤图" => "drawdown",
        "turnover" or "换手率顶底图" or "换手率图" or "顶底温度计" => "turnover",
        "chinext" or "创业板换手率图" or "创业板温度计" or "创业板顶底图" => "chinext",
        "all" or "三张图" => "all",
        "everything" or "全部图" or "所有图" => "everything",
        "ci" => "ci",
        "dashboard" or "交互看板" or "看板" => "dashboard",
        _ => throw new ArgumentException($"未知命令 {args[0]}。\n{usage}")
    };
    string? output = null;
    bool fromCsv = false;
    bool skipUpdate = false;
    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--output-dir" when output is null && i + 1 < args.Length && !args[i + 1].StartsWith("--"):
                output = Path.GetFullPath(args[++i]);
                break;
            case "--from-csv" when !fromCsv:
                fromCsv = true;
                break;
            case "--skip-update" when command == "industry" && !skipUpdate:
                skipUpdate = true;
                break;
            default:
                throw new ArgumentException($"无法识别的参数 {args[i]}。\n{usage}");
        }
    }
    if (fromCsv && skipUpdate)
        throw new ArgumentException("--from-csv 与 --skip-update 请择一使用。");

    output ??= FindOutputDirectory();
    string lockKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
    using var outputLock = File.Open(Path.Combine(Path.GetTempPath(), $"market-charts-{lockKey}.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var client = new MarketData();
    DateTime nowShanghai = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
        DateTimeOffset.UtcNow, "Asia/Shanghai").DateTime;
    DateOnly today = DateOnly.FromDateTime(nowShanghai);
    bool beforeClose = nowShanghai.TimeOfDay < new TimeSpan(15, 5, 0);
    DateOnly maxDate = beforeClose ? today.AddDays(-1) : today;
    if (!fromCsv && !skipUpdate && command != "dashboard" && beforeClose)
        Console.WriteLine($"当前北京时间 {nowShanghai:yyyy-MM-dd HH:mm}（当日未收盘），已自动过滤盘中数据，仅采纳 {Csv.Date(maxDate)} 及之前已收盘数据。");

    if (command == "ci")
    {
        await RunLog.RunCiAsync(client, maxDate, nowShanghai, output);
    }
    else
    {
        if (command is "industry" or "all" or "everything")
            await new IndustryReport(client, Path.Combine(output, "margin-industry-report-project"))
                .RunAsync(fromCsv, skipUpdate, maxDate);
        if (command is "tech" or "all" or "everything")
            await TechReport.RunAsync(client, maxDate, Path.Combine(output, "tech-vs-dividend-2026"), fromCsv);
        if (command is "drawdown" or "all" or "everything")
            await DrawdownReport.RunAsync(client, maxDate, Path.Combine(output, "margin-drawdown-speed"), fromCsv);
        if (command is "turnover" or "everything")
            await TurnoverReport.RunAsync(client, maxDate, Path.Combine(output, "market-turnover-cycle"), fromCsv);
        if (command is "chinext" or "everything")
            await ChinextTurnoverReport.RunAsync(client, maxDate, Path.Combine(output, "chinext-turnover-cycle"), fromCsv);
    }
    if (command == "dashboard" || !skipUpdate)
        DashboardReport.Run(output);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string FindOutputDirectory()
{
    foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MarketCharts", "MarketCharts.csproj")))
                return Path.Combine(dir.FullName, "outputs");
        }
    }
    throw new DirectoryNotFoundException("找不到项目根目录。请从项目目录运行，或使用 --output-dir 指定输出目录。");
}
