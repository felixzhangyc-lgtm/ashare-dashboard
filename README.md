# 市场图表项目

用 C# / .NET 11 获取东方财富和腾讯行情数据，生成三张市场图表。首次运行需要 .NET 11 SDK、NuGet 访问和系统中文字体。PNG 由 SkiaSharp 渲染。

在项目根目录运行以下命令。

| 固定名称 | 内容 | 更新命令 | 图片位置 |
| --- | --- | --- | --- |
| 行业融资图 | 申万一级行业融资净买入自 2025 年初累计，图表从 2025 年 6 月起显示，附控制台日报 | `dotnet run --project MarketCharts -- industry` | `outputs/margin-industry-report-project/sw_l1_margin_netbuy_cumulative_since_2025.png` |
| 成长红利对比图 | 科创50与创业板 ETF 各半，对比红利低波 ETF，含收益率差值柱状图 | `dotnet run --project MarketCharts -- tech` | `outputs/tech-vs-dividend-2026/tech_vs_dividend_2026.png` |
| 两融回撤图 | 两融余额相对过去 252 个交易日高点的回撤、20 交易日变化与创业板指 | `dotnet run --project MarketCharts -- drawdown` | `outputs/margin-drawdown-speed/margin_drawdown_speed.png` |

一次更新三张图

```bash
dotnet run --project MarketCharts -- all
```

使用现有 CSV 离线重绘三张图。也可以将 `all` 换成单张图的命令。

```bash
dotnet run --project MarketCharts -- all --from-csv
```

仅打印行业融资日报

```bash
dotnet run --project MarketCharts -- industry --skip-update
```

默认输出到项目的 `outputs` 目录，可用 `--output-dir PATH` 指定另一个目录。离线运行时，该目录须包含对应子目录及现有 CSV。CSV 采用 UTF-8 BOM 编码，可继续使用原有数据。

行业融资优先增量读取，缺少带融资余额的原始 CSV 时会拉取 2025 年以来的历史数据。ETF 以 2025-12-31 的前复权收盘价为基准。两融披露通常晚于当日行情，因此三张图的最新日期可能不同。

也可以直接提出“更新行业融资图”“更新成长红利对比图”“更新两融回撤图”或“更新三张图”。更新按需运行。

## 主板、创业板与科创板的总市值与日成交额

`dotnet run --project MarketCharts -- market-cap` 更新从 2024 年 7 月 1 日起的三张双轴图，分别对应沪深主板 A 股、创业板和科创板。每张图的左轴折线为该板块总市值，右轴柱形为日成交额。主板使用沪深主板 A 股的同日数据之和。图中两轴单位为万亿元，CSV 各金额列单位为亿元。文件保存在 `outputs/ashare-market-cap/`。添加 `--from-csv` 可离线重绘。
