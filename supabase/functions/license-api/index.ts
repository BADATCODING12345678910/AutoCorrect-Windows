import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

/**
 * POST JSON:
 * { "action": "activate" | "refresh" | "dev_grant" | "status", ... }
 *
 * activate: { email } → finds active entitlement, issues refresh token + grant
 * refresh:  { licenseId, refreshToken }
 * status:   { licenseId }
 * dev_grant: { email, tier, secret } — secret must match LICENSE_DEV_SECRET
 */

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
};

const GRANT_DAYS = 14;

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { ...cors, "Content-Type": "application/json" },
  });
}

function sb() {
  return createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );
}

async function sha256(text: string): Promise<string> {
  const data = new TextEncoder().encode(text);
  const hash = await crypto.subtle.digest("SHA-256", data);
  return [...new Uint8Array(hash)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function newToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function isEntitled(row: {
  status: string;
  current_period_end: string | null;
}): boolean {
  if (row.status !== "active") return false;
  if (!row.current_period_end) return true; // lifetime
  return new Date(row.current_period_end).getTime() > Date.now();
}

function grantFrom(row: Record<string, unknown>, refreshToken: string) {
  const grantExpiresAt = new Date();
  grantExpiresAt.setUTCDate(grantExpiresAt.getUTCDate() + GRANT_DAYS);
  return {
    entitled: true,
    licenseId: row.license_id,
    email: row.email,
    tier: row.tier,
    status: row.status,
    periodEnd: row.current_period_end,
    grantExpiresAt: grantExpiresAt.toISOString(),
    refreshToken,
  };
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return json({ error: "POST only" }, 405);

  let body: Record<string, unknown>;
  try {
    body = await req.json();
  } catch {
    return json({ error: "invalid json" }, 400);
  }

  const action = String(body.action ?? "");
  const client = sb();

  try {
    if (action === "dev_grant") {
      const secret = String(body.secret ?? "");
      const expected = Deno.env.get("LICENSE_DEV_SECRET") ?? "";
      if (!expected || secret !== expected) return json({ error: "forbidden" }, 403);
      const email = String(body.email ?? "").trim().toLowerCase();
      const tier = String(body.tier ?? "lifetime");
      if (!email) return json({ error: "email required" }, 400);

      const refreshToken = newToken();
      const hash = await sha256(refreshToken);

      const { data: existing } = await client
        .from("entitlements")
        .select("*")
        .eq("email", email)
        .eq("tier", tier)
        .maybeSingle();

      let row: Record<string, unknown>;
      if (existing) {
        const { data, error } = await client.from("entitlements").update({
          status: "active",
          current_period_end: tier === "lifetime" ? null : existing.current_period_end,
          refresh_token_hash: hash,
        }).eq("license_id", existing.license_id).select("*").single();
        if (error) throw error;
        row = data;
      } else {
        const { data, error } = await client.from("entitlements").insert({
          email,
          tier,
          status: "active",
          current_period_end: null,
          refresh_token_hash: hash,
        }).select("*").single();
        if (error) throw error;
        row = data;
      }
      return json(grantFrom(row, refreshToken));
    }

    if (action === "activate") {
      const email = String(body.email ?? "").trim().toLowerCase();
      if (!email) return json({ error: "email required" }, 400);

      const { data: rows, error } = await client
        .from("entitlements")
        .select("*")
        .eq("email", email)
        .order("updated_at", { ascending: false });
      if (error) throw error;

      const row = (rows ?? []).find((r) => isEntitled(r));
      if (!row) {
        return json({
          entitled: false,
          error: "No active license for that email. Pay first, or wait for PayPal webhook.",
        }, 404);
      }

      const refreshToken = newToken();
      const hash = await sha256(refreshToken);
      await client.from("entitlements").update({ refresh_token_hash: hash })
        .eq("license_id", row.license_id);

      return json(grantFrom(row, refreshToken));
    }

    if (action === "refresh") {
      const licenseId = String(body.licenseId ?? "");
      const refreshToken = String(body.refreshToken ?? "");
      if (!licenseId || !refreshToken) return json({ error: "licenseId and refreshToken required" }, 400);

      const { data: row, error } = await client
        .from("entitlements")
        .select("*")
        .eq("license_id", licenseId)
        .maybeSingle();
      if (error) throw error;
      if (!row) return json({ entitled: false, error: "unknown license" }, 404);

      const hash = await sha256(refreshToken);
      if (!row.refresh_token_hash || row.refresh_token_hash !== hash) {
        return json({ entitled: false, error: "invalid refresh token" }, 401);
      }

      if (!isEntitled(row)) {
        return json({
          entitled: false,
          licenseId: row.license_id,
          status: row.status,
          periodEnd: row.current_period_end,
          error: "subscription not active",
        }, 403);
      }

      const next = newToken();
      const nextHash = await sha256(next);
      await client.from("entitlements").update({ refresh_token_hash: nextHash })
        .eq("license_id", licenseId);

      return json(grantFrom(row, next));
    }

    if (action === "status") {
      const licenseId = String(body.licenseId ?? "");
      if (!licenseId) return json({ error: "licenseId required" }, 400);
      const { data: row, error } = await client
        .from("entitlements")
        .select("license_id, email, tier, status, current_period_end")
        .eq("license_id", licenseId)
        .maybeSingle();
      if (error) throw error;
      if (!row) return json({ entitled: false }, 404);
      return json({
        entitled: isEntitled(row),
        licenseId: row.license_id,
        email: row.email,
        tier: row.tier,
        status: row.status,
        periodEnd: row.current_period_end,
      });
    }

    return json({ error: "unknown action" }, 400);
  } catch (e) {
    console.error(e);
    return json({ error: String(e) }, 500);
  }
});
