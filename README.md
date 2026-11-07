# AutoCorrect for Windows

System-wide typing helper for Windows: autocorrect, grammar / Perfect Fix, rewrite, dictation session, language packs, and in-app updates. Built with **.NET 8** and **Avalonia**.

**License:** MIT  
**Main contributor:** [Kyle Welch](https://github.com/BADATCODING12345678910) (`@BADATCODING12345678910`)

| | |
|--|--|
| Site (primary) | https://autocorrect-windows-site.pages.dev/ |
| Support | https://autocorrect-windows-site.pages.dev/support |
| GitHub mirror | https://badatcoding12345678910.github.io/AutoCorrect-Windows/ |
| X | https://x.com/phd_in_sleeping |
| GitHub | https://github.com/BADATCODING12345678910 |

## Features

- Tray app with hotkeys (Fix, rewrite, privacy, dictation)
- Rule packs + AutoLearn + review UI
- Optional AI grammar / Perfect Fix via **bring your own key (BYOK)** in Settings → Grammar
- Language packs and in-app updates

## Build / run

```powershell
dotnet build
dotnet run --project src/AutoCorrect.App
# or:
.\run-app.cmd
```

Quit the tray app before rebuild if you hit DLL lock errors.

**Usage:** tray → focus a field → **Win+Alt+F** (Fix) / **Win+H** dictation / **Win+Alt+R** rewrite / **Win+Alt+Shift+P** privacy.

## Secrets and BYOK

API keys and similar credentials must **never** be committed. Paste an AI key in **Settings → Grammar** (stored under `%LocalAppData%\AutoCorrect\` on that PC).

## Layout

| Path | Role |
|------|------|
| `src/AutoCorrect.Core` | Pipeline, packs, license, updates |
| `src/AutoCorrect.App` | Avalonia tray / settings / review |
| `src/AutoCorrect.Platform.*` | Text bridge (Windows UIA + hooks; Linux/Mac stubs) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). PRs welcome; credit goes with Kyle Welch as main contributor on this project.
