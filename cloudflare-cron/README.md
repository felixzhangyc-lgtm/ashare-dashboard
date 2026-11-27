# Cloudflare Workers 准点定时触发器

通过 Cloudflare Workers Cron Triggers（免费版即享秒级准时触发）调用 GitHub Actions `workflow_dispatch` API，彻底解决 GitHub Actions 自带 `schedule` 排队延迟约 60 分钟的问题。

## 定时档位（周一至周五）

| Cron (UTC) | 对应北京时间 (CST) | 用途 |
| --- | --- | --- |
| `15,40 1 * * MON-FRI` | `09:15`、`09:40` | 拉取前一交易日 T+1 两融全行业披露与官方流通市值校准 |
| `10 7 * * MON-FRI` | `15:10` | 15:00 收盘后第一时间快报 |
| `30 9 * * MON-FRI` | `17:30` | 沪深交易所盘后官方流通市值落地后最终校准 |

> 注意：Cloudflare Cron 的数字星期采用 `1=周日..7=周六`（`1-5` 是周日到周四，会跳过周五），务必使用 `MON-FRI`。

仅占用 **3 条** Cron Trigger（完全在 Cloudflare 免费账号 5 条总限额内）。

---

## 第一步：创建最小权限 GitHub Token（1 分钟）

1. 打开 GitHub：[Settings → Developer settings → Personal access tokens → Fine-grained tokens](https://github.com/settings/personal-access-tokens/new)
2. 配置参数：
   - **Token name**：`ashare-dashboard-cloudflare-cron`
   - **Expiration**：按需选择（例如 1 年或 Custom）
   - **Repository access**：选 **Only select repositories** → 仅勾选 `felixzhangyc-lgtm/ashare-dashboard`
   - **Repository permissions**：仅将 **Actions** 设为 **Read and write**（其余全部保持 No access）
3. 点击 **Generate token** 并复制生成的 `github_pat_...` 字符串。

---

## 第二步：部署到 Cloudflare Workers（任选一种方式）

### 方式 A：网页控制台部署（零本地依赖，推荐）

1. 登录 [Cloudflare Dashboard](https://dash.cloudflare.com/) → 左侧 **Compute (Workers) → Workers & Pages** → 点击 **Create** → **Create Worker**（名称填 `ashare-dashboard-cron`）→ **Deploy**。
2. 点击 **Edit code**，将本目录 [src/index.js](./src/index.js) 的全部代码粘贴覆盖，点击右上角 **Deploy**。
3. 返回该 Worker 的 **Settings**：
   - **Variables and Secrets** → 点击 **Add**：
     - Type 选 **Secret**
     - Name 填 `GITHUB_TOKEN`
     - Value 粘贴第一步生成的 `github_pat_...` → 保存并部署。
   - **Triggers → Cron Triggers** → 点击 **Add**，依次添加以下 3 条（UTC）：
     - `15,40 1 * * MON-FRI`
     - `10 7 * * MON-FRI`
     - `30 9 * * MON-FRI`
4. **立刻验证**：
   - 在 Worker 页面的 **Edit code** 或 **Triggers** 页触发一次 Cron 测试（或打开 GitHub Actions 页面查看是否立刻出现一条新运行，且运行日志中触发方式显示为 `准点定时`）。

### 方式 B：命令行 `wrangler` 一键部署

```bash
cd cloudflare-cron
npx wrangler login
npx wrangler secret put GITHUB_TOKEN   # 粘贴第一步的 github_pat_...
npx wrangler deploy
```

---

## 第三步：验证通过后精简 GitHub 自带 Cron

确认 Cloudflare 准点触发工作正常后，可将 [.github/workflows/update-charts.yml](../.github/workflows/update-charts.yml) 中的 `on.schedule` 精简为仅保留兜底档位（或完全移除），避免与 Cloudflare 重复运行。
