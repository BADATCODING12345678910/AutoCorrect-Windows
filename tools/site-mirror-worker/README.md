# Site hosting notes

**Advertised primary:** Cloudflare Pages  
https://autocorrect-windows-site.pages.dev/

Same look/function as the GitHub Pages mirror (landing, support, language packs).  
Tray / Settings → **Website…** opens this URL. **GitHub mirror…** is fallback only.

**Future (planned):** custom domain on Cloudflare; keep GitHub Pages as an optional mirror; optionally mirror Setup.exe off GitHub for school networks.

Publish both hosts with:

```powershell
cd AutoCorrectNative
.\tools\publish-site.ps1
```
