const DEFAULT_OWNER = "felixzhangyc-lgtm";
const DEFAULT_REPO = "ashare-dashboard";
const DEFAULT_WORKFLOW = "update-charts.yml";
const DEFAULT_REF = "main";

async function dispatchWorkflow(env, { mode = "everything", source = "cloudflare-cron", cron = "" } = {}) {
  const token = env.GITHUB_TOKEN;
  if (!token) {
    throw new Error("Missing secret GITHUB_TOKEN in Cloudflare Worker environment.");
  }

  const owner = env.GITHUB_OWNER || DEFAULT_OWNER;
  const repo = env.GITHUB_REPO || DEFAULT_REPO;
  const workflow = env.GITHUB_WORKFLOW || DEFAULT_WORKFLOW;
  const ref = env.GITHUB_REF || DEFAULT_REF;

  const url = `https://api.github.com/repos/${owner}/${repo}/actions/workflows/${workflow}/dispatches`;
  const body = JSON.stringify({
    ref,
    inputs: { mode, source }
  });

  let lastError = null;
  for (let attempt = 1; attempt <= 2; attempt++) {
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${token}`,
          "Accept": "application/vnd.github+json",
          "X-GitHub-Api-Version": "2022-11-28",
          "User-Agent": "ashare-dashboard-cloudflare-cron",
          "Content-Type": "application/json"
        },
        body
      });

      if (res.status === 204) {
        console.log(`[OK] Dispatched ${owner}/${repo}/${workflow}@${ref} (mode=${mode}, source=${source}, cron=${cron || "manual"})`);
        return { ok: true, status: 204, owner, repo, workflow, ref, mode, source };
      }

      const detail = await res.text();
      lastError = new Error(`GitHub dispatch failed: HTTP ${res.status} - ${detail}`);
      if (res.status >= 500 && attempt < 2) {
        await new Promise((r) => setTimeout(r, 1500));
        continue;
      }
      throw lastError;
    } catch (err) {
      lastError = err;
      if (attempt < 2) {
        await new Promise((r) => setTimeout(r, 1500));
        continue;
      }
      throw lastError;
    }
  }
  throw lastError;
}

export default {
  async scheduled(controller, env, ctx) {
    ctx.waitUntil(
      dispatchWorkflow(env, {
        mode: "everything",
        source: "cloudflare-cron",
        cron: controller.cron
      })
    );
  },

  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/") {
      return Response.json({
        service: "ashare-dashboard-cron",
        status: "ok",
        target: `${env.GITHUB_OWNER || DEFAULT_OWNER}/${env.GITHUB_REPO || DEFAULT_REPO}/${env.GITHUB_WORKFLOW || DEFAULT_WORKFLOW}`,
        tokenConfigured: Boolean(env.GITHUB_TOKEN)
      });
    }

    if (request.method === "POST" && url.pathname === "/trigger") {
      if (!env.TRIGGER_SECRET) {
        return Response.json(
          { error: "Manual HTTP trigger is disabled (TRIGGER_SECRET not set)." },
          { status: 403 }
        );
      }
      const auth = request.headers.get("Authorization") || "";
      if (auth !== `Bearer ${env.TRIGGER_SECRET}`) {
        return Response.json({ error: "Unauthorized" }, { status: 401 });
      }
      const mode = url.searchParams.get("mode") === "dashboard" ? "dashboard" : "everything";
      const result = await dispatchWorkflow(env, { mode, source: "cloudflare-cron", cron: "http-test" });
      return Response.json(result, { status: 200 });
    }

    return new Response("Not Found", { status: 404 });
  }
};
