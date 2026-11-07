import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

/**
 * PayPal → Supabase entitlement updates.
 * Point PayPal (sandbox/live) webhook URL here.
 * verify_jwt must be false (PayPal does not send Supabase JWT).
 *
 * Handled event families (string includes):
 * - BILLING.SUBSCRIPTION.ACTIVATED / CREATED
 * - BILLING.SUBSCRIPTION.CANCELLED / EXPIRED / SUSPENDED
 * - PAYMENT.SALE.COMPLETED (renewal)
 * - PAYMENT.CAPTURE.REFUNDED / payment.capture.refunded
 */

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type, paypal-transmission-id, paypal-transmission-time, paypal-transmission-sig, paypal-cert-url, paypal-auth-algo",
};

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

function tierFromCustom(customId: string | undefined | null): string {
  const c = (customId ?? "").toLowerCase();
  if (c.includes("pro")) return "pro";
  if (c.includes("team")) return "team";
  if (c.includes("lifetime")) return "lifetime";
  return "personal";
}

function addPeriod(from: Date, unit: "YEAR" | "MONTH" | "DAY", count = 1): Date {
  const d = new Date(from);
  if (unit === "YEAR") d.setUTCFullYear(d.getUTCFullYear() + count);
  else if (unit === "MONTH") d.setUTCMonth(d.getUTCMonth() + count);
  else d.setUTCDate(d.getUTCDate() + count);
  return d;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return json({ error: "POST only" }, 405);

  const raw = await req.text();
  let body: Record<string, unknown>;
  try {
    body = JSON.parse(raw);
  } catch {
    return json({ error: "invalid json" }, 400);
  }

  // Optional: set PAYPAL_WEBHOOK_VERIFY=strict later with PayPal verify API.
  // For v1 we accept signed HTTPS delivery and trust PayPal → this URL secrecy + optional shared secret header.
  const shared = Deno.env.get("PAYPAL_WEBHOOK_SHARED_SECRET");
  if (shared) {
    const hdr = req.headers.get("x-autocorrect-webhook-secret");
    if (hdr !== shared) return json({ error: "unauthorized" }, 401);
  }

  const eventType = String(body.event_type ?? body.eventType ?? "");
  const resource = (body.resource ?? {}) as Record<string, unknown>;
  const client = sb();

  try {
    if (/BILLING\.SUBSCRIPTION\.(ACTIVATED|CREATED)/i.test(eventType)) {
      const subId = String(resource.id ?? "");
      const subscriber = (resource.subscriber ?? {}) as Record<string, unknown>;
      const email = String(subscriber.email_address ?? subscriber.emailAddress ?? "").toLowerCase();
      const customId = String(resource.custom_id ?? resource.customId ?? "");
      const billingInfo = (resource.billing_info ?? resource.billingInfo ?? {}) as Record<string, unknown>;
      const nextBilling = billingInfo.next_billing_time ?? billingInfo.nextBillingTime;
      const periodEnd = nextBilling
        ? new Date(String(nextBilling))
        : addPeriod(new Date(), "YEAR", 1);

      if (!subId || !email) return json({ ok: true, skipped: "missing sub/email" });

      const tier = tierFromCustom(customId);
      const { data: existing } = await client
        .from("entitlements")
        .select("license_id")
        .eq("paypal_subscription_id", subId)
        .maybeSingle();

      if (existing?.license_id) {
        await client.from("entitlements").update({
          email,
          tier,
          status: "active",
          current_period_end: periodEnd.toISOString(),
        }).eq("license_id", existing.license_id);
      } else {
        await client.from("entitlements").insert({
          email,
          tier,
          status: "active",
          paypal_subscription_id: subId,
          current_period_end: periodEnd.toISOString(),
        });
      }
      return json({ ok: true, action: "subscription_active" });
    }

    if (/BILLING\.SUBSCRIPTION\.(CANCELLED|EXPIRED|SUSPENDED)/i.test(eventType)) {
      const subId = String(resource.id ?? "");
      if (!subId) return json({ ok: true, skipped: "no sub id" });
      const status = /SUSPENDED/i.test(eventType) ? "past_due" : "cancelled";
      await client.from("entitlements").update({ status }).eq("paypal_subscription_id", subId);
      return json({ ok: true, action: status });
    }

    if (/PAYMENT\.SALE\.COMPLETED/i.test(eventType) || /PAYMENT\.CAPTURE\.COMPLETED/i.test(eventType)) {
      const billingAgreementId = String(
        resource.billing_agreement_id ??
          resource.billingAgreementId ??
          ((resource.supplementary_data as Record<string, unknown>)?.related_ids as Record<string, unknown>)
            ?.subscription_id ??
          "",
      );
      const custom = String(resource.custom ?? resource.custom_id ?? "");
      // Lifetime one-time: no subscription id — create/update by email if present
      const payer = (resource.payer ?? {}) as Record<string, unknown>;
      const payerInfo = (payer.payer_info ?? payer.payerInfo ?? payer) as Record<string, unknown>;
      const email = String(
        payerInfo.email ?? payerInfo.email_address ?? resource.payee_email_address ?? "",
      ).toLowerCase();

      if (billingAgreementId) {
        const periodEnd = addPeriod(new Date(), custom.toLowerCase().includes("month") ? "MONTH" : "YEAR", 1);
        await client.from("entitlements").update({
          status: "active",
          current_period_end: periodEnd.toISOString(),
        }).eq("paypal_subscription_id", billingAgreementId);
        return json({ ok: true, action: "renewed" });
      }

      if (email && /lifetime/i.test(custom)) {
        const { data: row } = await client
          .from("entitlements")
          .select("license_id")
          .eq("email", email)
          .eq("tier", "lifetime")
          .maybeSingle();
        if (row?.license_id) {
          await client.from("entitlements").update({ status: "active", current_period_end: null })
            .eq("license_id", row.license_id);
        } else {
          await client.from("entitlements").insert({
            email,
            tier: "lifetime",
            status: "active",
            paypal_order_id: String(resource.id ?? resource.order_id ?? ""),
            current_period_end: null,
          });
        }
        return json({ ok: true, action: "lifetime_active" });
      }

      return json({ ok: true, skipped: "unhandled payment shape" });
    }

    if (/REFUND|REFUNDED/i.test(eventType)) {
      const custom = String(resource.custom ?? resource.custom_id ?? "");
      const email = String(
        ((resource.payer as Record<string, unknown>)?.email_address) ?? "",
      ).toLowerCase();
      if (email) {
        await client.from("entitlements").update({ status: "revoked" }).eq("email", email);
      }
      if (/lifetime/i.test(custom) && email) {
        await client.from("entitlements").update({ status: "revoked" })
          .eq("email", email).eq("tier", "lifetime");
      }
      return json({ ok: true, action: "revoked" });
    }

    return json({ ok: true, ignored: eventType || "unknown" });
  } catch (e) {
    console.error(e);
    return json({ error: String(e) }, 500);
  }
});
