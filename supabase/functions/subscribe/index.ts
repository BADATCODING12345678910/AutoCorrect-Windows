import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

/**
 * Creates a live PayPal subscription and redirects to the approve URL.
 *
 * Credentials (first match wins):
 * 1. Edge secrets PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET
 * 2. public.edge_secrets rows (service_role only)
 *
 * GET ?tier=personal|pro|team
 */

const PLANS: Record<string, { planId: string; customId: string }> = {
  personal: { planId: "P-7Y128409G99930929NLTLILY", customId: "personal" },
  pro: { planId: "P-9BH982067W200693HNLTLILY", customId: "pro" },
  team: { planId: "P-5ET6519198954273HNLTLILY", customId: "team" },
};

const LIFETIME = "https://www.paypal.com/ncp/payment/PA3R3XQRAF8QG";
const SITE = "https://autocorrect-windows-site.pages.dev/";

function htmlError(title: string, detail: string) {
  return new Response(
    `<!doctype html><meta charset=utf-8><title>${title}</title>
     <body style="font-family:system-ui;background:#07070c;color:#f4f2ff;padding:2rem">
     <h1>${title}</h1><p>${detail}</p>
     <p><a style="color:#a78bfa" href="${LIFETIME}">Buy Lifetime instead</a> ·
     <a style="color:#a78bfa" href="${SITE}">Back to site</a></p></body>`,
    { status: 503, headers: { "Content-Type": "text/html; charset=utf-8" } },
  );
}

async function loadPaypalCreds(): Promise<{ clientId: string; secret: string }> {
  let clientId = Deno.env.get("PAYPAL_CLIENT_ID") || "";
  let secret = Deno.env.get("PAYPAL_CLIENT_SECRET") || "";
  if (clientId && secret) return { clientId, secret };

  const url = Deno.env.get("SUPABASE_URL");
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!url || !key) return { clientId, secret };

  const sb = createClient(url, key);
  const { data, error } = await sb
    .from("edge_secrets")
    .select("name, value")
    .in("name", ["PAYPAL_CLIENT_ID", "PAYPAL_CLIENT_SECRET"]);
  if (error) {
    console.error("edge_secrets read failed", error.message);
    return { clientId, secret };
  }
  for (const row of data ?? []) {
    if (row.name === "PAYPAL_CLIENT_ID" && row.value) clientId = row.value;
    if (row.name === "PAYPAL_CLIENT_SECRET" && row.value) secret = row.value;
  }
  return { clientId, secret };
}

async function paypalToken(clientId: string, secret: string) {
  const basic = btoa(`${clientId}:${secret}`);
  const resp = await fetch("https://api-m.paypal.com/v1/oauth2/token", {
    method: "POST",
    headers: {
      Authorization: `Basic ${basic}`,
      "Content-Type": "application/x-www-form-urlencoded",
    },
    body: "grant_type=client_credentials",
  });
  if (!resp.ok) {
    const t = await resp.text();
    throw new Error(`PayPal token HTTP ${resp.status}: ${t.slice(0, 180)}`);
  }
  const json = await resp.json();
  return json.access_token as string;
}

Deno.serve(async (req) => {
  try {
    const url = new URL(req.url);
    const tier = (url.searchParams.get("tier") || "personal").toLowerCase();
    const plan = PLANS[tier];
    if (!plan) {
      return htmlError("Unknown plan", "tier must be personal, pro, or team.");
    }

    const { clientId, secret } = await loadPaypalCreds();
    if (!clientId || !secret) {
      return htmlError(
        "Subscriptions not configured yet",
        "PayPal API credentials are missing on the server. Lifetime checkout still works.",
      );
    }

    const token = await paypalToken(clientId, secret);
    const body = {
      plan_id: plan.planId,
      custom_id: plan.customId,
      application_context: {
        brand_name: "AutoCorrect",
        user_action: "SUBSCRIBE_NOW",
        return_url: SITE,
        cancel_url: SITE,
      },
    };

    const subResp = await fetch(
      "https://api-m.paypal.com/v1/billing/subscriptions",
      {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
          "PayPal-Request-Id": crypto.randomUUID(),
        },
        body: JSON.stringify(body),
      },
    );
    const subText = await subResp.text();
    if (!subResp.ok) {
      console.error(subText);
      return htmlError(
        "PayPal error",
        `Could not start subscription (HTTP ${subResp.status}).`,
      );
    }
    const sub = JSON.parse(subText);
    const approve = (sub.links || []).find(
      (l: { rel: string }) => l.rel === "approve",
    )?.href;
    if (!approve) {
      return htmlError("PayPal error", "No approve link returned.");
    }

    return Response.redirect(approve, 302);
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    return htmlError("Subscribe failed", msg);
  }
});
