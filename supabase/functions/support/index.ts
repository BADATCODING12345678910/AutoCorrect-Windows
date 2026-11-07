import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

/**
 * Public support ticket intake from the marketing site.
 * POST JSON → inserts into support_tickets (service_role).
 * verify_jwt must be false (anonymous website form).
 */

const ALLOWED = new Set([
  "bug",
  "install",
  "license",
  "autocorrect",
  "dictation",
  "rewrite",
  "feature",
  "performance",
  "other",
]);

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

function json(status: number, body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS, "Content-Type": "application/json; charset=utf-8" },
  });
}

function clip(s: unknown, max: number): string {
  return String(s ?? "").trim().slice(0, max);
}

function looksLikeEmail(s: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s) && s.length <= 254;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: CORS });
  }
  if (req.method !== "POST") {
    return json(405, { ok: false, error: "POST only" });
  }

  let payload: Record<string, unknown>;
  try {
    payload = await req.json();
  } catch {
    return json(400, { ok: false, error: "Invalid JSON" });
  }

  // Honeypot — bots fill hidden fields; humans leave empty.
  if (clip(payload.company, 80)) {
    return json(200, { ok: true, id: "ok" });
  }

  const category = clip(payload.category, 32).toLowerCase();
  const subject = clip(payload.subject, 160);
  const body = clip(payload.body, 6000);
  const contactEmail = clip(payload.email, 254).toLowerCase();
  const appVersion = clip(payload.app_version, 64) || null;
  const windowsVersion = clip(payload.windows_version, 64) || null;

  if (!ALLOWED.has(category)) {
    return json(400, { ok: false, error: "Unknown category" });
  }
  if (subject.length < 4) {
    return json(400, { ok: false, error: "Subject is too short" });
  }
  if (body.length < 20) {
    return json(400, { ok: false, error: "Please describe the issue in more detail" });
  }
  if (!looksLikeEmail(contactEmail)) {
    return json(400, { ok: false, error: "A valid reply email is required" });
  }

  const url = Deno.env.get("SUPABASE_URL");
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!url || !key) {
    return json(503, { ok: false, error: "Support backend not configured" });
  }

  const sb = createClient(url, key);
  const { data, error } = await sb
    .from("support_tickets")
    .insert({
      category,
      subject,
      body,
      contact_email: contactEmail,
      app_version: appVersion,
      windows_version: windowsVersion,
      user_agent: clip(req.headers.get("user-agent"), 400) || null,
      source: "website",
      status: "open",
    })
    .select("id")
    .single();

  if (error) {
    console.error("support insert failed", error.message);
    return json(500, { ok: false, error: "Could not save ticket" });
  }

  return json(200, { ok: true, id: data.id });
});
