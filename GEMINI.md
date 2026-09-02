- **Architecture Context**: Always read and follow `KNOWLEDGE_GRAPH.md` for IPC structures, VMT hook indices, and module lifecycles before diagnosing presentation or sync issues.

## Strict Token Saving Rules
1. **Targeted Diffs Only:** When modifying existing files, only output changed functions or targeted replacements. Never reprint whole multi-hundred-line files for trivial updates. When triggering builds, compilation, or execution scripts, DO NOT poll or check the completion status repeatedly in short intervals. Wait/sleep at least 2 minutes before checking task status.
2. **Absolute Exclusion Zone:** Never read, index, parse, or output contents from:
   - `node_modules/`
   - `.next/`
   - `out/`
   - `package-lock.json` / `pnpm-lock.yaml` / `yarn.lock`
   - Binary assets (images, fonts, PDFs)
3. **No Redundant Comments:** Write self-documenting code. Do not write essay-style comment blocks or repetitive docstrings.
4. **Lean Dependencies:** Keep bundle lean. Use only essential dependencies