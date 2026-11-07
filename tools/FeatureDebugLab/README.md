# Feature Debug Lab

Dev-only Avalonia tool that exercises AutoCorrect features offline and across live text apps. **Not** shipped in Setup.

## Run

```bat
cd AutoCorrectNative
tools\run-feature-debug-lab.cmd
tools\run-feature-debug-lab.cmd --offline
```

- **Run offline** — fast product/settings/snippet/data-path regressions (no UI apps).
- **Run full lab** — launches tray if needed; walks Notepad/WordPad/Word/custom; live autocorrect, Undo, Pause, Fix card, Rewrite.

Exit codes (`--offline`): `0` = pass, `1` = failures, `2` = lab error.

## Reports

Written under `%LocalAppData%\AutoCorrect\debug-lab\`:

| File | Purpose |
|------|---------|
| `last-report.md` | Full run summary |
| `last-report.json` | Machine-readable |
| `last-failures.txt` | FAIL/WARN with **Fix hints** for Cursor |

GUI: **Copy failures** pastes agent-ready text.

## Cursor MCP

Prefer MCP server **`autocorrect-debug-lab`** (workspace `.cursor/mcp.json`):

1. `tools\FeatureDebugLab.Mcp\build-mcp.cmd`
2. Reload MCP in Cursor
3. Call `debuglab_run_offline` → `debuglab_get_failures` → fix → re-run

See `../FeatureDebugLab.Mcp/README.md` and rule `.cursor/rules/debug-lab-mcp.mdc`.
