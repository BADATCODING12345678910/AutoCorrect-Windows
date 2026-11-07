import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

/**
 * Public update feed for AutoCorrect.
 * GET  → latest.json shape from public.app_update_feed (+ mirrors Storage)
 * POST → admin upsert { version, minVersion, url, sha256, notes, secret }
 *        secret = Deno UPDATE_PUSH_SECRET or public.edge_secrets.UPDATE_PUSH_SECRET
 *
 * push-update.ps1 uses POST so sb_secret_ PostgREST blocks are irrelevant.
 */

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
};

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" },
  });
}

function sb() {
  return createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );
}

type FeedRow = {
  version: string;
  min_version: string;
  url: string;
  sha256: string;
  notes: string;
  released_at: string;
};

function toManifest(row: FeedRow) {
  return {
    version: row.version,
    minVersion: row.min_version,
    url: row.url,
    sha256: row.sha256,
    releasedAt: row.released_at,
    notes: row.notes ?? "",
  };
}

async function loadFeed() {
  const { data, error } = await sb().from("app_update_feed").select("*").eq("id", 1).maybeSingle();
  if (error) throw error;
  return data as FeedRow | null;
}

async function loadPushSecret(): Promise<string> {
  const fromEnv = Deno.env.get("UPDATE_PUSH_SECRET") ?? "";
  if (fromEnv) return fromEnv;

  const { data, error } = await sb()
    .from("edge_secrets")
    .select("value")
    .eq("name", "UPDATE_PUSH_SECRET")
    .maybeSingle();
  if (error) {
    console.error("edge_secrets UPDATE_PUSH_SECRET read failed", error.message);
    return "";
  }
  return (data?.value as string) ?? "";
}

async function mirrorToStorage(manifest: Record<string, unknown>) {
  const body = JSON.stringify(manifest, null, 2);
  const { error } = await sb().storage.from("app-updates").upload("latest.json", body, {
    contentType: "application/json",
    upsert: true,
  });
  if (error) {
    console.error("storage mirror failed", error);
  }
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: cors });
  }

  try {
    if (req.method !== "GET" && req.method !== "POST") {
      return json({ error: "method not allowed" }, 405);
    }

    // Admin upsert: POST { version, minVersion, url, sha256, notes, secret }
    if (req.method === "POST") {
      const body = await req.json().catch(() => ({})) as Record<string, string>;
      const secret = await loadPushSecret();
      if (!secret || body.secret !== secret) {
        return json({ error: "unauthorized" }, 401);
      }
      if (!body.version || !body.url || !body.sha256) {
        return json({ error: "version, url, sha256 required" }, 400);
      }
      const row = {
        id: 1,
        version: body.version,
        min_version: body.minVersion || "",
        url: body.url,
        sha256: body.sha256,
        notes: body.notes || "",
        released_at: new Date().toISOString(),
      };
      const { error } = await sb().from("app_update_feed").upsert(row);
      if (error) return json({ error: error.message }, 500);
    }

    const row = await loadFeed();
    if (!row) {
      return json({ error: "no feed row" }, 404);
    }

    const manifest = toManifest(row);
    await mirrorToStorage(manifest);
    return json(manifest);
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    return json({ error: msg }, 500);
  }
});
