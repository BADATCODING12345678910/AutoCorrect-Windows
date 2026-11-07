/**
 * Lightspeed-safe AutoCorrect site mirror.
 * Fetches canonical GitHub Pages content server-side and re-serves with
 * correct Content-Type (schools that block github.io can still load this).
 */
const PRIMARY = "https://badatcoding12345678910.github.io/AutoCorrect-Windows";
const FIREBASE = "https://ac-win-site-79604.web.app";

function contentTypeFor(path) {
  const p = path.toLowerCase();
  if (p.endsWith(".html") || p === "/" || p === "") return "text/html; charset=utf-8";
  if (p.endsWith(".json")) return "application/json; charset=utf-8";
  if (p.endsWith(".js")) return "application/javascript; charset=utf-8";
  if (p.endsWith(".css")) return "text/css; charset=utf-8";
  if (p.endsWith(".zip")) return "application/zip";
  if (p.endsWith(".txt")) return "text/plain; charset=utf-8";
  if (p.endsWith(".svg")) return "image/svg+xml";
  if (p.endsWith(".png")) return "image/png";
  return "application/octet-stream";
}

function normalizePath(pathname) {
  let path = pathname || "/";
  if (!path.startsWith("/")) path = "/" + path;
  if (path === "/") path = "/index.html";
  return path;
}

async function fetchUpstream(path) {
  const origins = [PRIMARY, FIREBASE];
  let lastErr;
  for (const origin of origins) {
    try {
      const res = await fetch(origin + path, {
        headers: { "User-Agent": "AutoCorrect-SiteMirror/1.0" },
        redirect: "follow",
      });
      if (res.ok) return res;
      lastErr = new Error(`HTTP ${res.status} from ${origin}`);
    } catch (e) {
      lastErr = e;
    }
  }
  throw lastErr || new Error("upstream failed");
}

export default {
  async fetch(request) {
    if (request.method === "OPTIONS") {
      return new Response(null, {
        status: 204,
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, HEAD, OPTIONS",
        },
      });
    }
    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method not allowed", { status: 405 });
    }

    const url = new URL(request.url);
    const path = normalizePath(url.pathname);

    try {
      const upstream = await fetchUpstream(path);
      const body = await upstream.arrayBuffer();
      const headers = new Headers({
        "Content-Type": contentTypeFor(path),
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": path.includes("language-packs")
          ? "public, max-age=3600"
          : "no-store",
        "X-AC-Mirror": "cloudflare-worker",
      });
      if (request.method === "HEAD") {
        return new Response(null, { status: 200, headers });
      }
      return new Response(body, { status: 200, headers });
    } catch (e) {
      const msg = e && e.message ? e.message : String(e);
      return new Response(
        `<!DOCTYPE html><html><body style="font-family:system-ui;padding:2rem"><h1>Mirror temporarily unavailable</h1><p>${msg}</p></body></html>`,
        {
          status: 502,
          headers: { "Content-Type": "text/html; charset=utf-8" },
        },
      );
    }
  },
};
