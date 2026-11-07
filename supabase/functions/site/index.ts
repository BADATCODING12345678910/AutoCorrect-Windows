import "jsr:@supabase/functions-js/edge-runtime.d.ts";

/**
 * Public marketing site entrypoint.
 *
 * Supabase Edge forces Content-Type: text/plain (+ CSP sandbox) on HTML bodies,
 * so browsers show raw source instead of rendering. Redirect to Cloudflare Pages
 * (advertised primary; school / Lightspeed-safe).
 */
const SITE =
  "https://autocorrect-windows-site.pages.dev/";

Deno.serve((_req) =>
  Response.redirect(SITE, 302)
);
