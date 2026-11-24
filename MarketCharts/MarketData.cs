using System.Net;
using System.Text.Json;

internal sealed class MarketData : IDisposable
{
    private const string DataCenterUrl = "https://datacenter-web.eastmoney.com/api/data/v1/get";
    private const string MarginReferer = "https://data.eastmoney.com/rzrq/";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<List<JsonElement>> DataCenterAsync(
        string report, string columns, string sortColumn, string filter, int pageSize, bool allowEmpty = false)
    {
        var rows = new List<JsonElement>();
        int pages = 1;
        int count = 0;
        for (int page = 1; page <= pages; page++)
        {
            var payload = await GetJsonAsync(DataCenterUrl, MarginReferer,
                ("reportName", report), ("columns", columns), ("source", "WEB"),
                ("sortColumns", sortColumn), ("sortTypes", "-1"), ("filter", filter),
                ("pageNumber", page.ToString()), ("pageSize", pageSize.ToString()));
            if (!payload.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            {
                string? message = payload.TryGetProperty("message", out var detail) ? detail.ToString() : null;
                if (allowEmpty && page == 1 && payload.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.Number && code.GetInt32() == 9201)
                    return [];
                throw new InvalidDataException($"东方财富 {report} 第 {page} 页请求失败：{message}");
            }

            var result = payload.GetProperty("result");
            if (result.ValueKind == JsonValueKind.Null && allowEmpty && page == 1)
                return [];
            if (page == 1)
            {
                pages = result.GetProperty("pages").GetInt32();
                count = result.GetProperty("count").GetInt32();
            }
            rows.AddRange(result.GetProperty("data").EnumerateArray());
        }
        if (rows.Count != count || (rows.Count == 0 && !allowEmpty))
            throw new InvalidDataException($"东方财富 {report} 预期 {count} 条，收到 {rows.Count} 条。");
        return rows;
    }

    public async Task<Dictionary<DateOnly, double>> FundPricesAsync(string code, DateOnly start, DateOnly end)
    {
        try
        {
            var payload = await GetJsonAsync("https://push2his.eastmoney.com/api/qt/stock/kline/get",
                "https://quote.eastmoney.com/", ("secid", code), ("fields1", "f1,f2,f3,f4,f5,f6"),
                ("fields2", "f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61"), ("klt", "101"), ("fqt", "1"),
                ("beg", start.ToString("yyyyMMdd")), ("end", end.ToString("yyyyMMdd")));
            if (payload.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("klines", out var klines) && klines.GetArrayLength() > 0)
            {
                return klines.EnumerateArray().Select(row => row.GetString()!.Split(','))
                    .Select(row => (Date: Csv.ParseDate(row[0]), Close: Csv.Number(row[2])))
                    .Where(row => row.Date >= start && row.Date <= end)
                    .ToDictionary(row => row.Date, row => row.Close);
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Fallback to Tencent forward-adjusted daily kline when Eastmoney push2his throttles.
        }

        return await TencentQfqPricesAsync(code, start, end);
    }

    private async Task<Dictionary<DateOnly, double>> TencentQfqPricesAsync(string secid, DateOnly start, DateOnly end)
    {
        string[] parts = secid.Split('.');
        string symbol = (parts[0] == "1" ? "sh" : "sz") + parts[1];
        var byYear = await Task.WhenAll(Enumerable.Range(start.Year, end.Year - start.Year + 1).Select(async year =>
        {
            string beg = Csv.Date(year == start.Year ? start : new DateOnly(year, 1, 1));
            string fin = Csv.Date(year == end.Year ? end : new DateOnly(year, 12, 31));
            var payload = await GetJsonAsync("https://web.ifzq.gtimg.cn/appstock/app/fqkline/get", null,
                ("param", $"{symbol},day,{beg},{fin},320,qfq"));
            if (payload.GetProperty("code").GetInt32() != 0)
                throw new InvalidDataException($"腾讯行情未返回 {symbol} ({year}) 的日线数据。");
            var item = payload.GetProperty("data").GetProperty(symbol);
            if (!item.TryGetProperty("qfqday", out var daily) && !item.TryGetProperty("day", out daily))
                throw new InvalidDataException($"腾讯行情未返回 {symbol} ({year}) 的前复权日线。");
            return daily.EnumerateArray()
                .Select(row => (Date: Csv.ParseDate(row[0].GetString()!), Close: Number(row[2]))).ToArray();
        }));
        var prices = byYear.SelectMany(rows => rows)
            .Where(row => row.Date >= start && row.Date <= end)
            .GroupBy(row => row.Date)
            .ToDictionary(group => group.Key, group => group.Last().Close);

        // Tencent fqkline can lag today's post-close bar; supplement from qt.gtimg.cn when newer.
        var latestQuote = await TencentLatestQuoteAsync(symbol);
        if (latestQuote is { } quote && quote.Date >= start && quote.Date <= end
            && quote.Close > 0 && !prices.ContainsKey(quote.Date))
        {
            if (prices.Count > 0 && quote.PrevClose > 0)
            {
                var lastDate = prices.Keys.Max();
                double lastClose = prices[lastDate];
                if (lastClose > 0 && Math.Abs(quote.PrevClose - lastClose) / quote.PrevClose > 0.002)
                {
                    double factor = quote.PrevClose / lastClose;
                    foreach (var key in prices.Keys.ToArray())
                        prices[key] *= factor;
                }
            }
            prices[quote.Date] = quote.Close;
        }

        if (prices.Count == 0)
            throw new InvalidDataException($"未获取到 {secid} 的日线行情。");
        return prices;
    }

    private async Task<(DateOnly Date, double Close, double PrevClose)?> TencentLatestQuoteAsync(string symbol)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://qt.gtimg.cn/q={symbol}");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            // qt.gtimg.cn returns charset=GBK; ASCII fields (~, dates, numbers) are safe under Latin1.
            string text = System.Text.Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync());
            int start = text.IndexOf('"');
            int end = text.LastIndexOf('"');
            if (start < 0 || end <= start)
                return null;
            string[] parts = text[(start + 1)..end].Split('~');
            if (parts.Length <= 30 || parts[30].Length < 8)
                return null;
            var date = DateOnly.ParseExact(parts[30].AsSpan(0, 8), "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture);
            double close = Csv.Number(parts[3]);
            double prevClose = Csv.Number(parts[4]);
            return close > 0 ? (date, close, prevClose) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<Dictionary<DateOnly, double>> ChinextAsync(int firstYear, DateOnly today)
    {
        var byYear = await Task.WhenAll(Enumerable.Range(firstYear, today.Year - firstYear + 1).Select(async year =>
        {
            string end = Csv.Date(year == today.Year ? today : new DateOnly(year, 12, 31));
            var payload = await GetJsonAsync("https://web.ifzq.gtimg.cn/appstock/app/fqkline/get", null,
                ("param", $"sz399006,day,{year}-01-01,{end},300,qfq"));
            if (payload.GetProperty("code").GetInt32() != 0)
                throw new InvalidDataException($"腾讯行情未返回 {year} 年创业板指数据。");
            var index = payload.GetProperty("data").GetProperty("sz399006");
            if (!index.TryGetProperty("day", out var daily) && !index.TryGetProperty("qfqday", out daily))
                throw new InvalidDataException($"腾讯行情未返回 {year} 年创业板指日线。");
            return daily.EnumerateArray()
                .Select(row => (Date: Csv.ParseDate(row[0].GetString()!), Close: Number(row[2]))).ToArray();
        }));
        return byYear.SelectMany(rows => rows).GroupBy(row => row.Date)
            .ToDictionary(group => group.Key, group => group.Last().Close);
    }

    public Task<string> IndustryClassificationAsync() =>
        GetTextAsync("https://emt.18.cn/api/quant-help/data/stock.html", MarginReferer);

    public readonly record struct IndexDay(double Close, double Amount);

    public async Task<Dictionary<DateOnly, IndexDay>> BaiduIndexDailyAsync(string code, DateOnly start)
    {
        var result = new Dictionary<DateOnly, IndexDay>();
        string? endTime = null;
        for (int page = 0; page < 10; page++)
        {
            var args = new List<(string Key, string Value)>
            {
                ("srcid", "5353"), ("all", "1"), ("pointType", "string"),
                ("group", "quotation_index_kline"), ("query", code), ("code", code),
                ("market_type", "ab"), ("newFormat", "1"), ("ktype", "1"), ("count", "1000")
            };
            if (endTime is not null)
                args.Add(("end_time", endTime));

            var payload = await GetJsonAsync("https://finance.pae.baidu.com/vapi/v1/getquotation", null, [.. args]);
            var market = payload.GetProperty("Result").GetProperty("newMarketData");
            var keys = market.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToArray();
            int timeIdx = Array.IndexOf(keys, "time");
            int closeIdx = Array.IndexOf(keys, "close");
            int amountIdx = Array.IndexOf(keys, "amount");
            if (timeIdx < 0 || closeIdx < 0 || amountIdx < 0)
                throw new InvalidDataException($"百度股市通未返回 {code} 的完整日线字段。");

            string raw = market.GetProperty("marketData").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
                break;

            int added = 0;
            DateOnly? earliest = null;
            foreach (string line in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(',');
                if (parts.Length <= Math.Max(timeIdx, Math.Max(closeIdx, amountIdx)))
                    continue;
                if (parts[closeIdx] is "" or "--" || parts[amountIdx] is "" or "--")
                    continue;
                var date = Csv.ParseDate(parts[timeIdx]);
                if (earliest is null || date < earliest)
                    earliest = date;
                if (result.TryAdd(date, new IndexDay(Csv.Number(parts[closeIdx]), Csv.Number(parts[amountIdx]))))
                    added++;
            }

            if (added == 0 || earliest is null || earliest <= start)
                break;
            endTime = Csv.Date(earliest.Value);
            await Task.Delay(120);
        }

        if (result.Count == 0)
            throw new InvalidDataException($"百度股市通未返回 {code} 的历史日线数据。");
        return result;
    }

    public readonly record struct ChinextExchangeDay(double AmountYi, double FloatCapYi, double TotalCapYi);

    public async Task<ChinextExchangeDay?> SzseChinextStatsAsync(DateOnly date)
    {
        var payload = await GetJsonAsync("https://www.szse.cn/api/report/ShowReport/data",
            "https://www.szse.cn/market/overview/index.html", ("SHOWTYPE", "JSON"),
            ("CATALOGID", "1803_sczm"), ("TABKEY", "tab1"), ("txtQueryDate", Csv.Date(date)));
        var table = payload.EnumerateArray().Single(row =>
            row.GetProperty("metadata").GetProperty("tabkey").GetString() == "tab1");
        var rows = table.GetProperty("data");
        if (rows.ValueKind == JsonValueKind.Null || rows.GetArrayLength() == 0)
            return null;
        if (table.GetProperty("metadata").GetProperty("subname").GetString() != Csv.Date(date))
            return null;
        var byBoard = rows.EnumerateArray().ToDictionary(
            row => WebUtility.HtmlDecode(row.GetProperty("lbmc").GetString()!).Trim());
        if (!byBoard.TryGetValue("创业板A股", out var rowElem) && !byBoard.TryGetValue("创业板", out rowElem))
            return null;
        var amt = ReadReportedAmount(rowElem.GetProperty("cjje"));
        var floatCap = ReadReportedAmount(rowElem.GetProperty("ltsz"));
        var totalCap = ReadReportedAmount(rowElem.GetProperty("sjzz"));
        return amt is > 0 && floatCap is > 0 && totalCap is > 0
            ? new ChinextExchangeDay(amt.Value, floatCap.Value, totalCap.Value)
            : null;
    }

    public async Task<double?> ExchangeNegotiableCapYuanAsync(DateOnly date)
    {
        try
        {
            var sseTask = GetJsonAsync("https://query.sse.com.cn/commonQuery.do", "https://www.sse.com.cn/",
                ("sqlId", "COMMON_SSE_SJ_GPSJ_CJGK_MRGK_C"), ("PRODUCT_CODE", "01,03"),
                ("type", "inParams"), ("SEARCH_DATE", Csv.Date(date)));
            var szseTask = GetJsonAsync("https://www.szse.cn/api/report/ShowReport/data",
                "https://www.szse.cn/market/overview/index.html", ("SHOWTYPE", "JSON"),
                ("CATALOGID", "1803_sczm"), ("TABKEY", "tab1"), ("txtQueryDate", Csv.Date(date)));
            await Task.WhenAll(sseTask, szseTask);

            var sseRows = (await sseTask).GetProperty("result");
            if (sseRows.ValueKind == JsonValueKind.Null || sseRows.GetArrayLength() == 0)
                return null;
            string expectedDate = date.ToString("yyyyMMdd");
            double sseNegoYi = 0;
            int sseCount = 0;
            foreach (var row in sseRows.EnumerateArray())
            {
                if (row.GetProperty("TRADE_DATE").GetString() != expectedDate)
                    return null;
                var val = ReadReportedAmount(row.GetProperty("NEGO_VALUE"));
                if (val is > 0)
                {
                    sseNegoYi += val.Value;
                    sseCount++;
                }
            }
            if (sseCount < 2)
                return null;

            var table = (await szseTask).EnumerateArray().Single(row =>
                row.GetProperty("metadata").GetProperty("tabkey").GetString() == "tab1");
            var szRows = table.GetProperty("data");
            if (szRows.ValueKind == JsonValueKind.Null || szRows.GetArrayLength() == 0)
                return null;
            if (table.GetProperty("metadata").GetProperty("subname").GetString() != Csv.Date(date))
                return null;
            var byBoard = szRows.EnumerateArray().ToDictionary(
                row => WebUtility.HtmlDecode(row.GetProperty("lbmc").GetString()!).Trim());
            var szMain = ReadReportedAmount(byBoard["主板A股"].GetProperty("ltsz"));
            var szChinext = ReadReportedAmount(byBoard["创业板A股"].GetProperty("ltsz"));
            if (szMain is not > 0 || szChinext is not > 0)
                return null;

            return (sseNegoYi + szMain.Value + szChinext.Value) * 100_000_000.0;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double? ReadReportedAmount(JsonElement value)
    {
        string text = value.ToString().Trim();
        if (text is "" or "-" or "--")
            return null;
        return Csv.Number(text.Replace(",", ""));
    }

    public static double Number(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? value.GetDouble() : Csv.Number(value.GetString()!);

    private async Task<JsonElement> GetJsonAsync(string url, string? referer, params (string Key, string Value)[] args)
    {
        string query = string.Join('&', args.Select(arg => $"{arg.Key}={Uri.EscapeDataString(arg.Value)}"));
        using var document = JsonDocument.Parse(await GetTextAsync($"{url}?{query}", referer));
        return document.RootElement.Clone();
    }

    private async Task<string> GetTextAsync(string url, string? referer)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                if (referer is not null)
                    request.Headers.Referrer = new Uri(referer);
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception error) when (attempt < 5 && (error is TaskCanceledException
                || error is HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError }))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600 * attempt));
            }
        }
    }

    public void Dispose() => http.Dispose();
}
