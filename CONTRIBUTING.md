# Contributing to AutoCorrect for Windows

Thanks for your interest in improving AutoCorrect.

## Main contributor

**Kyle Welch** ([@BADATCODING12345678910](https://github.com/BADATCODING12345678910))

- X: [@phd_in_sleeping](https://x.com/phd_in_sleeping)
- GitHub: [BADATCODING12345678910](https://github.com/BADATCODING12345678910)

## How to help

1. Open an issue describing the bug or idea before large changes.
2. Fork the repo and work on a focused branch.
3. Keep pull requests small and reviewable.
4. Do **not** commit secrets, API keys, license keys, or credential `.ini` files.
5. Do **not** add automation that posts to social accounts from this repository.

## Local setup

```powershell
dotnet build
dotnet run --project src/AutoCorrect.App
```

Optional: paste your own AI provider key in **Settings → Grammar** (bring your own key). Secrets belong in `%LocalAppData%\AutoCorrect\` on your machine — never in git.

## Coding notes

- Target: .NET 8, Avalonia UI, Windows-first (`AutoCorrect.Platform.Windows`).
- Prefer matching existing patterns in `src/AutoCorrect.App` and `src/AutoCorrect.Core`.
- Smoke: `dotnet run --project tools/PipelineSmoke` when pipeline behavior changes.

## License

By contributing, you agree your changes are licensed under the MIT License (see `LICENSE`).
