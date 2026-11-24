using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class RunLog
{
    public const string HistoryFileName = "run_history.json";
    private const int MaxRuns = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed record ModuleResult(
        string Key,
        string Label,
        string Status,
        double DurationSec,
        string LatestDate,
        string Error);

    public sealed record RunEntry(
        string Timestamp,
        string Trigger,
        string Status,
        double TotalSeconds,
        int OkCount,
        int FailCount,
        string RunUrl,
        List<ModuleResult> Modules);

    public static async Task RunCiAsync(MarketData client, DateOnly maxDate, DateTime nowShanghai, string outputDir)
    {
        var totalSw = Stopwatch.StartNew();
        var modules = new (string Key, string Label, Func<Task> Run, string CsvPath)[]
        {
            ("industry", "行业融资",
                () => new IndustryReport(client, Path.Combine(outputDir, "margin-industry-report-project")).RunAsync(false, false, maxDate),
                Path.Combine(outputDir, "margin-industry-report-project", "sw_l1_margin_netbuy_cumulative_since_2025.csv")),
            ("tech", "成长红利",
                () => TechReport.RunAsync(client, maxDate, Path.Combine(outputDir, "tech-vs-dividend-2026"), false),
                Path.Combine(outputDir, "tech-vs-dividend-2026", "tech_vs_dividend_2026.csv")),
            ("drawdown", "两融回撤",
                () => DrawdownReport.RunAsync(client, maxDate, Path.Combine(outputDir, "margin-drawdown-speed"), false),
                Path.Combine(outputDir, "margin-drawdown-speed", "margin_drawdown_speed.csv")),
            ("turnover", "全A温度计",
                () => TurnoverReport.RunAsync(client, maxDate, Path.Combine(outputDir, "market-turnover-cycle"), false),
                Path.Combine(outputDir, "market-turnover-cycle", "ashare_turnover_cycle.csv")),
            ("chinext", "创业板温度计",
                () => ChinextTurnoverReport.RunAsync(client, maxDate, Path.Combine(outputDir, "chinext-turnover-cycle"), false),
                Path.Combine(outputDir, "chinext-turnover-cycle", "chinext_turnover_cycle.csv"))
        };

        var results = new List<ModuleResult>(modules.Length);
        foreach (var m in modules)
        {
            Console.WriteLine($"=== [{m.Key}] 开始更新 {m.Label} ===");
            var sw = Stopwatch.StartNew();
            string status = "ok";
            string error = "";
            try
            {
                await m.Run();
            }
            catch (Exception ex)
            {
                status = "error";
                error = FormatError(ex);
                Console.Error.WriteLine($"::warning title={m.Label}({m.Key}) 更新失败::{error}");
                Console.Error.WriteLine($"[{m.Key}] 更新失败：{ex}");
            }
            sw.Stop();
            string latestDate = ReadLatestDate(m.CsvPath);
            results.Add(new ModuleResult(m.Key, m.Label, status, Math.Round(sw.Elapsed.TotalSeconds, 1), latestDate, error));
        }

        totalSw.Stop();
        int okCount = results.Count(r => r.Status == "ok");
        int failCount = results.Count - okCount;
        string overallStatus = failCount == 0 ? "ok" : okCount == 0 ? "failed" : "partial";

        string eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "local";
        string trigger = eventName switch
        {
            "schedule" => "定时自动",
            "workflow_dispatch" => "手动触发",
            "push" => "代码推送",
            _ => "本机运行"
        };
        string runUrl = BuildGitHubRunUrl();

        var entry = new RunEntry(
            nowShanghai.ToString("yyyy-MM-dd HH:mm:ss"),
            trigger,
            overallStatus,
            Math.Round(totalSw.Elapsed.TotalSeconds, 1),
            okCount,
            failCount,
            runUrl,
            results);

        AppendHistory(outputDir, entry);
        WriteStepSummary(entry);
        WriteGitHubOutputs(entry);
    }

    public static List<RunEntry> LoadHistory(string outputDir)
    {
        string path = Path.Combine(outputDir, HistoryFileName);
        if (!File.Exists(path))
            return [];
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<RunEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void AppendHistory(string outputDir, RunEntry entry)
    {
        Directory.CreateDirectory(outputDir);
        var history = LoadHistory(outputDir);
        history.Insert(0, entry);
        if (history.Count > MaxRuns)
            history.RemoveRange(MaxRuns, history.Count - MaxRuns);

        using var outputs = new OutputBatch(outputDir);
        File.WriteAllText(outputs.Stage(HistoryFileName),
            JsonSerializer.Serialize(history, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        outputs.Commit();
    }

    private static string ReadLatestDate(string csvPath)
    {
        try
        {
            if (!File.Exists(csvPath))
                return "—";
            string? lastLine = null;
            foreach (string line in File.ReadLines(csvPath, Encoding.UTF8))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lastLine = line;
            }
            if (string.IsNullOrEmpty(lastLine))
                return "—";
            int comma = lastLine.IndexOf(',');
            string first = (comma > 0 ? lastLine[..comma] : lastLine).Trim().TrimStart('\uFEFF');
            return DateOnly.TryParse(first, out var d) ? Csv.Date(d) : "—";
        }
        catch
        {
            return "—";
        }
    }

    private static string FormatError(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        if (ex.InnerException is not null)
            sb.Append(" -> ").Append(ex.InnerException.GetType().Name).Append(": ").Append(ex.InnerException.Message);
        string msg = sb.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        return msg.Length > 260 ? msg[..260] + "..." : msg;
    }

    private static string BuildGitHubRunUrl()
    {
        string? server = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        string? repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        string? runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(runId))
            return "";
        return $"{server.TrimEnd('/')}/{repo}/actions/runs/{runId}";
    }

    private static void WriteStepSummary(RunEntry entry)
    {
        string? summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var sb = new StringBuilder();
        string icon = entry.FailCount == 0 ? "✅" : "⚠️";
        sb.AppendLine($"## {icon} A股图表云端更新报告（{entry.Timestamp} CST · {entry.Trigger}）");
        sb.AppendLine();
        sb.AppendLine($"- **总体结果**：成功 `{entry.OkCount}/{entry.Modules.Count}`，失败 `{entry.FailCount}`，总耗时 `{entry.TotalSeconds:F1}s`");
        sb.AppendLine();
        sb.AppendLine("| 模块 | 状态 | 最新数据日期 | 耗时 | 错误摘要 |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var m in entry.Modules)
        {
            string st = m.Status == "ok" ? "✅ 成功" : "❌ 失败";
            string err = string.IsNullOrEmpty(m.Error) ? "—" : m.Error.Replace("|", "\\|");
            sb.AppendLine($"| {m.Label} (`{m.Key}`) | {st} | `{m.LatestDate}` | {m.DurationSec:F1}s | {err} |");
        }
        sb.AppendLine();
        File.AppendAllText(summaryPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteGitHubOutputs(RunEntry entry)
    {
        string? outputPath = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        bool hasFailures = entry.FailCount > 0;
        string failedSummary = hasFailures
            ? string.Join("；", entry.Modules.Where(m => m.Status != "ok").Select(m => $"{m.Label}({m.Key}): {m.Error}"))
            : "";

        var sb = new StringBuilder();
        sb.AppendLine($"has_failures={(hasFailures ? "true" : "false")}");
        sb.AppendLine($"ok_count={entry.OkCount}");
        sb.AppendLine($"fail_count={entry.FailCount}");
        sb.AppendLine($"failed_summary={failedSummary.Replace('\r', ' ').Replace('\n', ' ')}");
        File.AppendAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
    }
}
