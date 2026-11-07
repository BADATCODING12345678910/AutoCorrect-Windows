# Microsoft Store — submission kit (Win32 as-is)

**Status:** packages/ready; Partner Center login required (agent cannot complete Microsoft account auth).

## USER REMINDER
Create Microsoft account **`autocorrectlab@outlook.com`** (exact email the user wants) in a **normal** browser — not Cursor. Then sign in at Partner Center. See `Documentation/NATIVE_V2_HANDOFF.md` § USER REMINDER.

## Installer to list

- **URL:** https://github.com/BADATCODING12345678910/AutoCorrect-Windows/releases/download/v0.2.23/AutoCorrectSetup.exe  
  (same binary also on `v0.2.0` for the historic Setup link)
- **SHA256:** `ad9c7280852a00a7b7006666c645ed01e2705c8cc533a70ff697e39c33a2a240`
- **Type:** Inno Setup EXE, offline, x64, per-user (`PrivilegesRequired=lowest`)
- **Silent:** `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-`

## Listing copy (paste into Partner Center)

**Name:** AutoCorrect  
**Short description:** Grammarly-like autocorrect and dictation fix for every Windows app.  
**Full description:**

AutoCorrect is a system-wide writing assistant for Windows 10/11. It works in every app from a tray icon — not just a browser extension.

- As-you-type autocorrect with curated + learned pairs  
- Win+H dictation cleanup after Windows Voice Typing  
- Snippets, clipboard fix, selection rewrite tone packs  
- Privacy mode (local-only Fix; persists across restart)  
- On-demand language packs; Light Lavender theme  

14-day full trial. Lifetime and subscription plans available in Settings → License.

**Category:** Productivity  
**Website:** https://autocorrect-windows-site.pages.dev/  
**Support:** https://autocorrect-windows-site.pages.dev/support  
**GitHub mirror (fallback):** https://badatcoding12345678910.github.io/AutoCorrect-Windows/  
**Privacy policy URL:** use Support page until a dedicated privacy.html exists (add one before final submit if Store requires a distinct policy URL).

## Screenshots needed (capture locally)

1. Tray menu  
2. Settings → General  
3. Fix suggestion card in a text app  
4. Appearance / Light Lavender theme  
5. License / trial screen  

## Path in Partner Center

1. Sign in → create / open developer account if needed (~$19).  
2. Create new product → **Win32 / classic desktop** (EXE/MSI as-is), not UWP-only.  
3. Paste listing copy; upload screenshots.  
4. Packages → provide installer URL + metadata.  
5. Submit for certification. Be ready to explain global keyboard assist + UIA for accessibility/fix.

## After publish

Keep GitHub Setup URL stable or update the Store package URL on each Setup rebuild. In-app `push-update` remains for direct installs; Store-acquired installs may still self-update unless you disable that channel later.
