# Distribution — Store, winget, Chocolatey, Scoop

## Live status (0.2.23)

| Channel | Status | Link / command |
|---------|--------|----------------|
| **GitHub Setup** | Uploaded to `v0.2.23` + `v0.2.0` | [AutoCorrectSetup.exe](https://github.com/BADATCODING12345678910/AutoCorrect-Windows/releases/download/v0.2.23/AutoCorrectSetup.exe) |
| **winget** | PR open (CLA met; await Microsoft review). Local `--manifest` install tested OK | https://github.com/microsoft/winget-pkgs/pull/469767 → `winget install AutoCorrect.AutoCorrect` |
| **Scoop** | Bucket live | `scoop bucket add autocorrect https://github.com/BADATCODING12345678910/scoop-autocorrect` then `scoop install autocorrect/autocorrect-windows` |
| **Chocolatey** | **Pushed** `autocorrect-windows` 0.2.23 — in moderation | https://community.chocolatey.org/packages/autocorrect-windows · queue https://ch0.co/moderation |
| **Microsoft Store** | Listing kit ready; **REMINDER: create `autocorrectlab@outlook.com`** then Partner Center | See `STORE_SUBMISSION.md` + NATIVE_V2 USER REMINDER |

Package ids use **autocorrect-windows** on Scoop/Chocolatey because Scoop `main` already has an unrelated `autocorrect` (CJK linter). winget id is `AutoCorrect.AutoCorrect` (distinct from `Texthelp.AutoCorrect`).

## Release assets

| File | SHA256 |
|------|--------|
| Setup | `ad9c7280852a00a7b7006666c645ed01e2705c8cc533a70ff697e39c33a2a240` |
| Zip | `67f9571f547f41f3b28392e88bcf8ff9f8a3b9bb4d0dc293ffee217de77a0245` |

## Finish Chocolatey push

1. Register/login: https://community.chocolatey.org/  
2. Account → API Keys → create key for `https://push.chocolatey.org/`  
3. Save locally (never commit):

```bat
choco apikey --key YOUR_KEY --source https://push.chocolatey.org/
choco push AutoCorrectNative\distribution\chocolatey\autocorrect-windows\autocorrect-windows.0.2.23.nupkg --source https://push.chocolatey.org/
```

Or paste the key in chat once and ask the agent to push (prefer local `choco apikey` so it isn’t logged).

## Finish Microsoft Store

1. Sign in at https://partner.microsoft.com/dashboard (browser tab may already be open).  
2. Follow `STORE_SUBMISSION.md` (Win32 as-is → Setup URL).  
3. Tell the agent when you’re logged in so it can continue the UI steps.
