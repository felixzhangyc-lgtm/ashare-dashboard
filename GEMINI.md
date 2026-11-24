# A股市场图表项目规则（Gemini / Antigravity）

本项目用于抓取 A 股公开市场数据（东方财富、腾讯行情、上交所、深交所）并生成 PNG 市场图表与 CSV 数据表。

## 核心架构与边界

- **纯 C# / .NET 11 实现**：全部数据抓取、指标计算、SkiaSharp PNG 渲染与 `outputs/dashboard.html` 交互看板生成均统一由 `MarketCharts/` 工程完成（无 Python 运行时依赖，SkiaSharp 已本地化至 `MarketCharts/vendor/SkiaSharp/`，离线可快速编译）。
- **数据编码与输出**：输出默认写入 `outputs/` 目录，CSV 采用 UTF-8 BOM 编码。
- **未收盘不采纳当日数据**：A股 15:00 收盘（以北京时间 `15:05` 为收盘确认线）；北京时间 `15:05` 前拉取数据时，所有 5 张图自动过滤当日盘中数据，仅采纳前一日及之前已收盘数据。

## 运行环境约定

- 本机 `.NET 11` 路径可能不在默认 `PATH` 中，优先通过项目辅助脚本 `.agents/skills/market-charts/scripts/run_charts.sh` 调用，或显式使用 `/usr/local/share/dotnet/dotnet`。
- 未修改 C# 代码时，优先复用已编译产物（`--no-build`）执行，避免无必要的 NuGet 还原。
- 需要拉取最新行情数据时（非 `--from-csv` / `--skip-update`），命令需要访问公网（东方财富、腾讯行情、上交所、深交所接口）。

## 自然语言指令映射

当用户提出以下需求时，按对应子命令执行（通过 `.agents/skills/market-charts/scripts/run_charts.sh`）：

| 用户口令 | 执行命令 | 主要输出图片 |
| --- | --- | --- |
| 更新行业融资图 / 行业融资图 | `industry` | `outputs/margin-industry-report-project/sw_l1_margin_netbuy_cumulative_since_2025.png` |
| 行业融资日报 / 查看最新融资变化（不拉新数据） | `industry --skip-update` | 仅控制台输出日报 |
| 更新成长红利对比图 / 成长红利对比图 | `tech` | `outputs/tech-vs-dividend-2026/tech_vs_dividend_2026.png` |
| 更新两融回撤图 / 两融回撤图 | `drawdown` | `outputs/margin-drawdown-speed/margin_drawdown_speed.png` |
| 更新三张图 | `all` | 上述前三张图 + `outputs/dashboard.html`（完成后自动用 Chrome 打开看板） |
| 更新换手率顶底图 / 更新顶底温度计 / 换手率图 | `turnover` | `outputs/market-turnover-cycle/ashare_turnover_cycle.png` + `outputs/dashboard.html` |
| 更新创业板换手率图 / 创业板温度计 / 创业板顶底图 | `chinext` | `outputs/chinext-turnover-cycle/chinext_turnover_cycle.png` + `outputs/dashboard.html` |
| 更新全部图 / 更新所有图 | `everything` | 全部五张图 + `outputs/dashboard.html`（完成后自动用 Chrome 打开看板） |
| 更新交互看板 / 生成看板 | `dashboard` | `outputs/dashboard.html`（基于现有 CSV，支持滚轮/框选/时间窗缩放） |
| 打开看板 / 用 Chrome 打开面板 | `open -a "Google Chrome" "<项目根目录>/outputs/dashboard.html"` | 直接在 Google Chrome 中打开本地看板 |
| 离线重绘（不联网，按现有 CSV 重绘） | 对应命令加 `--from-csv` | 对应 PNG + `outputs/dashboard.html` |

## 看板自动打开约定

- 每次执行「更新三张图」（`all`）或「更新全部图 / 更新所有图」（`everything`）并成功生成 `outputs/dashboard.html` 后，**自动执行** `open -a "Google Chrome" "<项目根目录>/outputs/dashboard.html"` 在 Google Chrome 中打开交互式看板。
- `outputs/dashboard.html` 已内嵌全部数据并引用本地 `outputs/assets/echarts.min.js`，直接以本地文件方式用 Chrome 打开即可，**无需启动本地 HTTP 服务器，也无需使用内置 Webview 浏览器**。

## 结果汇报规范

1. **注明最新数据日期**：两融数据披露通常晚于当日盘后行情（T+1），因此“行业融资图 / 两融回撤图”与“成长红利对比图 / 换手率顶底图”的最新日期可能不同，汇报时需分别注明最新日期。
2. **附带关键指标与文件链接**：总结控制台输出的核心数值变化（如总融资余额、净买入前/后行业、成长红利收益差、两融回撤幅度、全A换手率与牛熊/筑底计时状态），并给出生成的 PNG 和 CSV 文件的可点击 `file://` 链接。
3. **图文解读**：若用户要求“解读”“分析”图表，直接读取生成的 PNG 图片和对应 CSV 进行分析，避免凭空推测。
