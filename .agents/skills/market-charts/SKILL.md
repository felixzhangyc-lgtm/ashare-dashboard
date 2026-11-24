---
name: market-charts
description: >-
  Run and maintain the A-share MarketCharts (.NET 11) reporting tool to fetch Eastmoney, Tencent, SSE, SZSE, and Baidu market data, generate PNG charts, CSV tables, and interactive dashboard.html, print daily margin reports, and interpret chart trends. Use this skill whenever the user asks to update, redraw, inspect, or analyze the A-share charts (行业融资图, 成长红利对比图, 两融回撤图, 换手率顶底图, 顶底温度计, 三张图, 全部图, or 融资日报).
---

# A股市场图表更新与分析（MarketCharts）

使用本 Skill 执行 `MarketCharts`（C# / .NET 11 + SkiaSharp）的数据拉取、图表重绘、日报输出及结果核验。

## 执行脚本

统一通过辅助脚本 [run_charts.sh](./scripts/run_charts.sh) 执行，脚本会自动定位 `dotnet`（含 `/usr/local/share/dotnet/dotnet`），并在 `MarketCharts/` 源码未变更时自动使用 `--no-build` 跳过冗余编译与 NuGet 还原：

```bash
./.agents/skills/market-charts/scripts/run_charts.sh <command> [options]
```

### 支持的子命令

- `industry`：增量更新申万一级行业自 2025 年初累计融资净买入，重绘图表并打印日报
- `industry --skip-update`：不联网，仅用现有 CSV 打印最新行业融资日报
- `tech`：更新科创50与创业板 ETF 等权组合 vs 红利低波 ETF 对比图（以 `2025-12-31` 前复权收盘价为基准）
- `drawdown`：更新两融余额相对近 252 个交易日高点的回撤、20 日变化与创业板指图
- `all`：一次更新前三类图（`industry` + `tech` + `drawdown`）
- `turnover`：更新 2010-03-31 以来全A流通换手率（沪深成交额 ÷ 全A流通市值）、MA5/MA20、近 5 年历史分位、牛熊/筑底计时器与上证指数顶底温度计图
- `chinext`：更新 2012-12-03 以来创业板流通换手率（深交所官方锚点校准）、MA5/MA20、近 5 年历史分位、牛熊/筑底计时器与创业板指顶底温度计图
- `everything`：更新全部五类图表（`industry` + `tech` + `drawdown` + `turnover` + `chinext`），并同步刷新 `outputs/dashboard.html`
- `dashboard`：仅基于现有 CSV 重新生成可缩放交互式网页看板 `outputs/dashboard.html`（依赖本地 `outputs/assets/echarts.min.js`，离线可用）

### 常用参数

- `--from-csv`：不联网，按本地现有 CSV 离线重绘对应图表（支持 `industry` / `tech` / `drawdown` / `turnover` / `all` / `everything`）
- `--output-dir PATH`：将输出重定向到指定目录（默认输出到项目根目录的 `outputs/`）
- `--rebuild`：强制重新编译 `MarketCharts`（修改过 `.cs` 或 `.csproj` 时脚本也会自动检测并重新编译）

## 输出文件清单

1. **行业融资图**（`outputs/margin-industry-report-project/`）
   - `sw_l1_margin_netbuy_cumulative_since_2025.png`（横轴从 `2025-06-01` 起，累计起点 `2025-01-01`）
   - `sw_l1_margin_netbuy_cumulative_since_2025.csv`
   - `sw_l1_margin_netbuy_latest_summary.csv`
   - `eastmoney_margin_l2_raw_since_2025.csv`
2. **成长红利对比图**（`outputs/tech-vs-dividend-2026/`）
   - `tech_vs_dividend_2026.png`
   - `tech_vs_dividend_2026.csv`
3. **两融回撤图**（`outputs/margin-drawdown-speed/`）
   - `margin_drawdown_speed.png`
   - `margin_drawdown_speed.csv`
4. **全A换手率顶底温度计**（`outputs/market-turnover-cycle/`）
   - `ashare_turnover_cycle.png`
   - `ashare_turnover_cycle.csv`
5. **创业板换手率顶底温度计**（`outputs/chinext-turnover-cycle/`）
   - `chinext_turnover_cycle.png`
   - `chinext_turnover_cycle.csv`

## 标准执行流程

1. **判断模式**：
   - 若用户要求“更新 / 拉取最新数据”，在线执行对应子命令（联网请求东方财富、腾讯、上交所、深交所）。
   - 若用户要求“离线重绘 / 用现有数据画图”，附加 `--from-csv`。
   - 若用户只看最新行业融资日报，执行 `industry --skip-update`。
2. **核验输出与自动打开看板**：
   - 确认命令退出码为 `0`，并检查控制台打印的最新交易日及汇总数据。
   - 若执行的是「更新三张图」（`all`）或「更新全部图 / 更新所有图」（`everything`），更新完成后**自动用 Google Chrome 打开本地看板**：
     ```bash
     open -a "Google Chrome" "<项目根目录>/outputs/dashboard.html"
     ```
     （`dashboard.html` 为离线自包含文件，直接用 Chrome 打开，不要启动 HTTP 服务，也不要改用内置 Webview。）
   - 若用户需要看图或分析趋势，用 `read_file` 直接读取生成的 PNG 图片与相关 CSV，结合视觉走势与数值进行解读。
3. **汇报结果**：
   - 分别标明每类图表的最新数据日期（两融 T+1 披露通常晚于当日盘后行情，属正常情况）。
   - 列出核心指标摘要，并提供生成的 PNG / CSV 的可点击文件路径链接。

## 常见问题排查

- **找不到 `dotnet` 命令**：脚本会自动回退检查 `/usr/local/share/dotnet/dotnet` 和 `$HOME/.dotnet/dotnet`。
- **离线环境下报 `NU1301`（无法连接 NuGet）**：未修改源码时脚本默认使用 `--no-build`；如果修改了源码需要重新编译，可先用 `--no-restore` 构建（依赖包已缓存在本地 NuGet 缓存中）。
- **接口返回空数据或日期滞后**：通常发生在非交易日、盘中尚未收盘、或交易所/东方财富盘后数据尚未发布；两融数据一般于次一交易日早间完整更新。
