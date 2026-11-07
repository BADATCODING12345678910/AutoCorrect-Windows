# Windows installer (Phase C)

1. Run `..\publish-win.cmd` (self-contained `win-x64` → `artifacts\win-x64`, plus repo `Data\` curated/imported/wordlist and `Core\AutoCorrectHotstrings.ahk`). **Required only to rebuild Setup**, not to run an already-built `AutoCorrectSetup.exe`.
2. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php).
3. Open `AutoCorrect.iss` → **Compile** (or `ISCC.exe AutoCorrect.iss`).
4. Install/ship `Output\AutoCorrectSetup.exe` — it is self-contained; end users do not need `publish-win.cmd` or `artifacts\`.

**Updates after first install:** use `..\push-update.cmd 0.2.24 "notes"` (GitHub Releases zip + Supabase `app_update_feed` / `update-feed`). Live feed is currently **0.2.23**. Users get updates via tray **Check for updates…** / startup check — no new Setup for normal ships. If PostgREST rejects `sb_secret_`, SQL-update `app_update_feed` then warm the feed.

License keys are created with:

```bat
dotnet run --project ..\tools\LicenseKeyGen -- buyer@email.com personal
dotnet run --project ..\tools\LicenseKeyGen -- buyer@email.com lifetime
```

Payments: **PayPal** Lifetime NCP + Personal/Pro/Team subscriptions (see `Documentation/PAYPAL_PLANS_LIVE.json` and in-app License tab). Online entitlements via Supabase. Offline keys still work for core Fix + autocorrect.
