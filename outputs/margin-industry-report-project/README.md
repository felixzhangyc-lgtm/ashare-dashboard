# 2025 年以来申万一级行业融资净买入累计

- `sw_l1_margin_netbuy_cumulative_since_2025.png` 为行业融资图。
- `sw_l1_margin_netbuy_cumulative_since_2025.csv` 为申万一级行业逐日累计值，单位亿元。
- `sw_l1_margin_netbuy_latest_summary.csv` 为最新交易日的一级行业累计值汇总。
- `eastmoney_margin_l2_raw_since_2025.csv` 为东方财富二级行业原始日度数据。

数据取自东方财富数据中心。程序将东方财富二级行业映射到申万 2021 一级行业，从 2025-01-01 起逐日累计 `FIN_NETBUY_AMT`（融资净买入额）。

图表横轴从 2025-06-01 开始，纵轴按 2025 年初以来的净买入额累计。每次更新及离线重绘均使用此显示范围。

从项目根目录更新图表、CSV 并打印日报

```bash
dotnet run --project MarketCharts -- industry
```

程序比较本地原始 CSV 与远端快照的最新日期。有新交易日时，从本地最新日期起获取行业历史，按交易日和板块代码去重。原始 CSV 缺失或缺少融资余额字段时获取全量历史。

离线重绘图表并重算汇总

```bash
dotnet run --project MarketCharts -- industry --from-csv
```

仅用现有 CSV 打印日报

```bash
dotnet run --project MarketCharts -- industry --skip-update
```
