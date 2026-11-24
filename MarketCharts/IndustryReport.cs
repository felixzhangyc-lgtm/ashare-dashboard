using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed class IndustryReport(MarketData client, string directory)
{
    private const string RawName = "eastmoney_margin_l2_raw_since_2025.csv";
    private const string WideName = "sw_l1_margin_netbuy_cumulative_since_2025.csv";
    private const string SummaryName = "sw_l1_margin_netbuy_latest_summary.csv";
    private const string ChartName = "sw_l1_margin_netbuy_cumulative_since_2025.png";
    private const string Start = "2025-01-01";
    private static readonly DateOnly ChartStart = new(2025, 6, 1);
    private const double YuanPerUnit = 100_000_000;
    private static readonly string[] RawHeader = ["date", "board_code", "board_name", "fin_netbuy_amt", "fin_balance", "l1_industry"];

    private sealed record BoardDay(DateOnly Date, string Code, string Name, double NetBuy, double Balance, string Industry);
    private sealed record IndustryDay(DateOnly Date, double[] Totals);
    private sealed record History(string[] Industries, IndustryDay[] Days);

    public async Task RunAsync(bool fromCsv, bool skipUpdate, DateOnly maxDate)
    {
        var raw = ReadRaw(requireBalance: fromCsv || skipUpdate);
        if (skipUpdate)
        {
            PrintReport(raw, ReadHistory());
            return;
        }

        if (!fromCsv)
            raw = await UpdateAsync(raw, maxDate);
        var history = Accumulate(raw);
        if (history.Days.Length < 2)
            throw new InvalidDataException("行业融资数据至少需要两个交易日。");

        var latest = history.Days[^1];
        int[] order = Enumerable.Range(0, history.Industries.Length)
            .OrderByDescending(index => latest.Totals[index]).ToArray();

        var chartDays = history.Days.Where(day => day.Date >= ChartStart).ToArray();
        if (chartDays.Length == 0)
            throw new InvalidDataException($"行业融资数据未覆盖绘图起点 {Csv.Date(ChartStart)}。");

        using var outputs = new OutputBatch(directory);
        ReportCharts.Industry(outputs.Stage(ChartName), ChartStart, chartDays.Select(day => day.Date).ToArray(),
            order.Select(i => (history.Industries[i], chartDays.Select(day => day.Totals[i]).ToArray())).ToArray());
        Csv.Write(outputs.Stage(WideName), ["date", .. history.Industries],
            history.Days.Select(day => new[] { Csv.Date(day.Date) }.Concat(day.Totals.Select(Csv.Number))));
        Csv.Write(outputs.Stage(SummaryName), ["行业", "截至最新累计融资净买入_亿元"],
            order.Select(i => new[] { history.Industries[i], Csv.Number(latest.Totals[i]) }));
        if (!fromCsv)
            Csv.Write(outputs.Stage(RawName), RawHeader, raw.Select(row => new[]
            {
                Csv.Date(row.Date), row.Code, row.Name, Csv.Number(row.NetBuy), Csv.Number(row.Balance), row.Industry
            }));
        outputs.Commit();
        PrintReport(raw, history);
    }

    private List<BoardDay> ReadRaw(bool requireBalance)
    {
        string path = Path.Combine(directory, RawName);
        if (!File.Exists(path) && !requireBalance)
            return [];
        var table = Csv.Read(path);
        if (!table.Header.Contains("fin_balance") && !requireBalance)
            return [];
        int[] columns = RawHeader.Select(table.Column).ToArray();
        return table.Rows.Where(row => row[columns[5]].Length > 0).Select(row => new BoardDay(
            Csv.ParseDate(row[columns[0]]), row[columns[1]], row[columns[2]], Csv.Number(row[columns[3]]),
            row[columns[4]].Length == 0 ? 0 : Csv.Number(row[columns[4]]), row[columns[5]])).ToList();
    }

    private History ReadHistory()
    {
        var table = Csv.Read(Path.Combine(directory, WideName));
        return new History(table.Header[1..], table.Rows.Select(row => new IndustryDay(
            Csv.ParseDate(row[0]), row.Skip(1).Select(Csv.Number).ToArray())).ToArray());
    }

    private async Task<List<BoardDay>> UpdateAsync(List<BoardDay> existing, DateOnly maxDate)
    {
        var snapshot = (await client.DataCenterAsync("RPTA_WEB_BKJYMXN", "ALL", "FIN_NETBUY_AMT",
            "(BOARD_TYPE_CODE=\"005\")", 500)).Select(ParseBoard).Where(row => row.Date <= maxDate).ToArray();
        DateOnly? local = existing.Count == 0 ? null : existing.Max(row => row.Date);
        if (snapshot.Length == 0)
            return existing;
        DateOnly remote = snapshot.Max(row => row.Date);
        Console.WriteLine($"行业融资最新日期  本地 {local?.ToString("yyyy-MM-dd") ?? "无"}  远端 {Csv.Date(remote)}");
        if (local is { } localDate && remote <= localDate)
            return existing;

        var mapping = await LoadMappingAsync(existing);
        var missing = snapshot.Select(row => row.Name).Distinct().Where(name => !mapping.ContainsKey(name)).ToArray();
        if (missing.Length != 0)
            Console.Error.WriteLine($"未映射到申万一级行业的板块：{string.Join("、", missing)}");

        string start = local is { } date ? Csv.Date(date) : Start;
        var boards = snapshot.Where(row => mapping.ContainsKey(row.Name)).DistinctBy(row => row.Code).ToArray();
        var latestBoards = snapshot.Where(row => row.Date == remote && mapping.ContainsKey(row.Name))
            .Select(row => row.Code).ToHashSet();
        if (latestBoards.Count == 0)
            throw new InvalidDataException("远端最新交易日缺少可映射的行业融资数据。");

        var merged = existing.GroupBy(row => (row.Date, row.Code)).ToDictionary(group => group.Key, group => group.Last());
        for (int i = 0; i < boards.Length; i++)
        {
            var board = boards[i];
            var history = (await client.DataCenterAsync("RPTA_WEB_BKJYMX", "ALL", "TRADE_DATE",
                $"(BOARD_CODE=\"{board.Code}\")(TRADE_DATE>='{start}')", 500, allowEmpty: true))
                .Select(ParseBoard).Where(row => row.Date <= maxDate).ToArray();
            if (latestBoards.Contains(board.Code) && !history.Any(row => row.Date == remote && row.Code == board.Code))
                throw new InvalidDataException($"{board.Name}的历史接口尚未返回 {Csv.Date(remote)} 的数据，更新中止。");
            foreach (var row in history)
                merged[(row.Date, row.Code)] = row with { Industry = mapping[board.Name] };
            Console.WriteLine($"[{i + 1}/{boards.Length}] {board.Name} +{history.Length}");
            await Task.Delay(50);
        }

        return merged.Values.OrderBy(row => row.Date).ThenBy(row => row.Code, StringComparer.Ordinal).ToList();
    }

    private static BoardDay ParseBoard(JsonElement row)
    {
        string code = row.GetProperty("BOARD_CODE").ToString();
        string name = row.GetProperty("BOARD_NAME").ToString();
        if (code.Length == 0 || name.Length == 0)
            throw new InvalidDataException("东方财富行业融资记录缺少板块代码或名称。");
        double ReadAmount(string field) => row.GetProperty(field) is { ValueKind: JsonValueKind.Null }
            ? 0 : MarketData.Number(row.GetProperty(field));
        return new BoardDay(Csv.ParseDate(row.GetProperty("TRADE_DATE").GetString()!), code, name,
            ReadAmount("FIN_NETBUY_AMT"), ReadAmount("FIN_BALANCE"), "");
    }

    private async Task<Dictionary<string, string>> LoadMappingAsync(IEnumerable<BoardDay> existing)
    {
        var mapping = StaticGroups.Split('\n').SelectMany(line =>
        {
            var parts = line.Split(' ');
            return parts.Skip(1).Select(name => (Name: name, Industry: parts[0]));
        }).ToDictionary(row => row.Name, row => row.Industry);

        foreach (var board in existing)
            mapping[board.Name] = board.Industry;

        try
        {
            var parsed = new Dictionary<string, string>();
            string html = await client.IndustryClassificationAsync();
            foreach (string raw in Regex.Replace(WebUtility.HtmlDecode(html), "<[^>]+>", "\n").Split('\n'))
            {
                string line = Regex.Replace(raw.Trim(), @"\s+", " ");
                var match = Regex.Match(line, @"^(\d{6}) (\S+) (\d{6}) (.+?) \d{6} .+$");
                if (match.Success && match.Groups[1].Value.EndsWith("0000")
                    && match.Groups[3].Value[..2] == match.Groups[1].Value[..2])
                    parsed[match.Groups[4].Value] = match.Groups[2].Value;
            }
            if (parsed.Count >= 100)
            {
                foreach (var (name, industry) in parsed)
                    mapping[name] = industry;
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"行业分类接口暂不可用，使用内置和历史映射。{error.Message}");
        }
        return mapping;
    }

    private static History Accumulate(IEnumerable<BoardDay> raw)
    {
        var groups = raw.GroupBy(row => (row.Date, row.Code)).Select(group => group.Last()).ToArray();
        var names = groups.Select(row => row.Industry).Distinct().Order(StringComparer.Ordinal).ToArray();
        var positions = names.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index);
        var totals = new double[names.Length];
        var days = new List<IndustryDay>();
        foreach (var day in groups.GroupBy(row => row.Date).OrderBy(group => group.Key))
        {
            foreach (var industry in day.GroupBy(row => row.Industry))
                totals[positions[industry.Key]] += industry.Sum(row => row.NetBuy);
            days.Add(new IndustryDay(day.Key, totals.Select(total => total / YuanPerUnit).ToArray()));
        }
        return new History(names, days.ToArray());
    }

    private static void PrintReport(List<BoardDay> raw, History history)
    {
        if (history.Days.Length < 2)
            throw new InvalidDataException("行业融资日报至少需要两个交易日。");
        var latest = history.Days[^1];
        var previous = history.Days[^2];
        var balances = raw.Where(row => row.Date == latest.Date).ToArray();
        if (balances.Length == 0)
            throw new InvalidDataException($"原始数据缺少 {Csv.Date(latest.Date)} 的行业融资余额。");
        var changes = history.Industries.Select((name, i) => (Name: name, Change: latest.Totals[i] - previous.Totals[i]))
            .OrderByDescending(row => row.Change).ToArray();
        double total = changes.Sum(row => row.Change);
        Console.WriteLine($"总融资余额：{balances.Sum(row => row.Balance) / YuanPerUnit:F2} 亿元（截至 {Csv.Date(latest.Date)}）");
        Console.WriteLine($"当日融资净买入合计：{Csv.Signed(total)} 亿元");
        Console.WriteLine(history.Days.Length >= 3
            ? $"相对前一交易日变化量：{Csv.Signed(total - previous.Totals.Zip(history.Days[^3].Totals, (a, b) => a - b).Sum())} 亿元"
            : "相对前一交易日变化量：暂无（历史数据不足）");
        Console.WriteLine($"融资变化列表：{Csv.Date(latest.Date)} 相对 {Csv.Date(previous.Date)}");
        Console.WriteLine("单位：亿元。口径：申万一级行业累计融资净买入的日变化。");
        Console.WriteLine();
        foreach (var (name, change) in changes)
            Console.WriteLine($"{name}: {Csv.Signed(change)}");
    }

    private const string StaticGroups = """
        农林牧渔 种植业 渔业 林业Ⅱ 饲料 农产品加工 养殖业 动物保健Ⅱ 农业综合Ⅱ
        基础化工 化学原料 化学制品 化学纤维 塑料 橡胶 农化制品 非金属材料Ⅱ
        钢铁 冶钢原料 普钢 特钢Ⅱ
        有色金属 金属新材料 工业金属 贵金属 小金属 能源金属
        电子 半导体 元件 光学光电子 消费电子 电子化学品Ⅱ 其他电子Ⅱ
        家用电器 白色家电 黑色家电 小家电 厨卫电器 家电零部件Ⅱ 其他家电Ⅱ 照明设备Ⅱ
        食品饮料 白酒Ⅱ 非白酒 饮料乳品 休闲食品 食品加工 调味发酵品Ⅱ
        纺织服饰 服装家纺 纺织制造 饰品
        轻工制造 家居用品 造纸 包装印刷 文娱用品
        医药生物 化学制药 中药Ⅱ 生物制品 医药商业 医疗器械 医疗服务
        公用事业 电力 燃气Ⅱ
        交通运输 航运港口 航空机场 铁路公路 物流
        房地产 房地产开发 房地产服务
        商贸零售 一般零售 贸易Ⅱ 互联网电商 旅游零售Ⅱ 专业连锁Ⅱ
        社会服务 旅游及景区 酒店餐饮 教育 体育Ⅱ 专业服务
        综合 综合Ⅱ
        建筑材料 水泥 玻璃玻纤 装修建材
        建筑装饰 房屋建设Ⅱ 装修装饰Ⅱ 基础建设 专业工程 工程咨询服务Ⅱ
        电力设备 电池 光伏设备 风电设备 电网设备 电机Ⅱ 其他电源设备Ⅱ
        国防军工 航天装备Ⅱ 航空装备Ⅱ 地面兵装Ⅱ 航海装备Ⅱ 军工电子Ⅱ
        计算机 计算机设备 IT服务Ⅱ 软件开发
        传媒 游戏Ⅱ 广告营销 数字媒体 影视院线 出版 电视广播Ⅱ
        通信 通信设备 通信服务
        银行 银行Ⅱ
        非银金融 证券Ⅱ 保险Ⅱ 多元金融
        汽车 乘用车 商用车 汽车零部件 汽车服务 摩托车及其他
        机械设备 工程机械 自动化设备 专用设备 通用设备 轨交设备Ⅱ
        煤炭 煤炭开采 焦炭Ⅱ
        石油石化 油气开采Ⅱ 油服工程 炼化及贸易
        环保 环境治理 环保设备Ⅱ
        美容护理 个护用品 化妆品 医疗美容
        """;
}
