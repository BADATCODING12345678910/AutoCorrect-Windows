# AutoCorrect Feature Debug Lab — MCP server

Stdio [Model Context Protocol](https://modelcontextprotocol.io/) server so **Cursor can call the Debug Lab directly**.  
Product live feed is unrelated (app **0.2.23**); this is **dev tooling** only. Lab GUI docs: `../FeatureDebugLab/README.md`.
## Tools

| Tool | Purpose |
|------|---------|
| `debuglab_run_offline` | Run offline bug hunt; return status + Fix hints |
| `debuglab_get_status` | Summarize last report (no re-run) |
| `debuglab_get_failures` | Agent-ready `last-failures.txt` |
| `debuglab_get_report` | Full `last-report.md` |
| `debuglab_get_report_json` | Structured `last-report.json` |
| `debuglab_paths` | Report + project paths |

Reports live in `%LocalAppData%\AutoCorrect\debug-lab\`.

## Cursor wiring

Project config: `.cursor/mcp.json` → server name **`autocorrect-debug-lab`**.

1. Build once:
   ```bat
   cd AutoCorrectNative\tools\FeatureDebugLab.Mcp
   build-mcp.cmd
   ```
2. Restart Cursor / reload MCP so the server shows green.
3. In Agent chat, ask it to use **autocorrect-debug-lab** tools (e.g. “run Debug Lab offline via MCP and fix any failures”).

## Manual smoke

```bat
build-mcp.cmd
run-mcp.cmd
```

`run-mcp.cmd` starts the stdio server (Cursor normally owns this). For a one-shot lab without MCP:

```bat
..\run-feature-debug-lab.cmd --offline
```
