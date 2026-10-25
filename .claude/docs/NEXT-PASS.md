# Next pass: phoenix-lai

Findings from a review on 2026-10-24. Work top to bottom; tick items off as you go. Line numbers are as of commit `4b95819` and may have shifted. This file lives in `.claude/docs/` because CLAUDE.md restricts what may sit in the repo root.

The source code already follows the CLAUDE.md hard rules: no banned APIs outside the allowed files, no C# 6+ syntax, no external URLs in `html/`, and no secrets. The work below is repo contents, not code.

## 1. Sanitize (do first)

This branch already removed the files below from the current tree. They are **still in git history**, in this repo and in every fork of it.

- [ ] **Back up anything you still need before pulling.** Pulling deletes these from your working copy:
  - `swagger-openai-provider-syntax.txt` (internal gateway docs)
  - `logs.txt` (session log)
  - `Phoenix_LAI_2026_10_22_1600.tar.gz` and `.package`

  They are ignored from now on. Keep the gateway doc outside the repo.
- [ ] **Rewrite history** to drop those files and the real name that was in the dev plan's byline.
  1. Build `names.txt` *outside the repo* with one `Real Name==>author` line.
  2. Run:
     ```bash
     git clone --mirror <repo-url> phoenix-lai.git && cd phoenix-lai.git
     git filter-repo --invert-paths \
       --path swagger-openai-provider-syntax.txt --path logs.txt \
       --path-glob 'Phoenix_LAI_*.tar.gz' --path-glob 'Phoenix_LAI_*.package' \
       --replace-text ../names.txt
     git remote add origin <repo-url>
     git push --force --mirror
     ```
  3. Delete `names.txt`. Re-clone everywhere, and delete old branches, tags and PRs that point at pre-rewrite commits. Rewrite every fork in the network the same way.
  4. The pack drops from about 61 MiB to about 12 MiB.
- [ ] **Regenerate `Phoenix_Client.lnk` and `Phoenix_LAI_Server.lnk`.** Both embed the dev machine's user SID. Recreate them with `.claude\tools\make-shortcut.ps1`, and if possible strip the SID or regenerate on a neutral account. Add them to the rewrite if they must leave history.
- [ ] **Package.ps1 exclusions:** line 24 skips the `logs\` folder but not a root `logs.txt`. Exclude `.claude/`, `logs*`, `*.tar.gz`, `*.package` and `swagger-*.txt` from packages.

## 2. Root folder vs CLAUDE.md

CLAUDE.md allows only the `.lnk` files, `CLAUDE.md`, `README.md`, `__version.info`, `.gitignore` and the listed folders in the root. These are still there:

- [ ] `Package.ps1`, `Package.bat`, `Unpack.ps1`, `Unpack.bat`: move them to `src\`, as CLAUDE.md says Phoenix scripts live there, and update the `.bat` paths.
- [ ] `__vs.code-workspace`: ignore it or move it out.
- [ ] `TechStack`: the shared stack doc for the org's local web apps. Decide whether to add it to the allowed list or move it to `.claude/docs/`. Note that it describes the web-app stack (React, Tailwind, `TcpListener`, Rust/WASM), not Phoenix's own rules, which take precedence.

## 3. Bugs and drift

- [ ] **`nvidia/tests/KernelTests.cs` is out of date** with `.claude/claude_tests/KernelTests.cs`. It's missing `if (!Flash2Test.Run(output)) fail.Add("flash2 attention");` (line 21), and `Flash2Test.cs` isn't copied. Re-copy both, per CLAUDE.md.
- [ ] **Wrong shortcut name** in user-facing text: `src/WebServer.cs:726` and `html/js/providers.js:214` say `PhoenixLAI_Client.lnk`. The file is `Phoenix_Client.lnk`.
- [ ] **`Unpack.ps1` last line:** `'{0}\: {1} files'` prints a stray backslash.
- [ ] **edge-ops catalog:** Phoenix is listed as `"network": false` (`catalog.json:12`), but the client can reach any OpenAI provider on the LAN or the internet. Phoenix also takes ports 8765-8789 (server) and 8790-8799 (client) (`src/WebServer.cs:302-305`, `ServerHost.cs:56`). Those collide with SnipHero (8765), MailHawk (8766), Keystone (8769) and the 8770-8799 range for new tools in `PORTS.md`. Fix the registry or move the ranges. *(2026-10-25: the port overlap is fixed on `ports/fleet-port`, see CHANGELOG; the `network` flag is still open.)*

## 4. Rule edge cases (decide; not necessarily wrong)

- [ ] `.claude/claude_tests/ref/llama.cpp/` has 10 verbatim llama.cpp sources. They are dev reference only and never compiled, but "no third-party code" has no written exception. Add one, or remove them.
- [ ] `html/fonts/` (Inter, Roboto, Exo2, SyneMono) has no license files. Add the OFL/Apache texts.
- [ ] `nvidia/3-collect.ps1:53` runs `Start-Process dxdiag.exe`. It's a diagnostic script, not Phoenix code, but the process-start rule doesn't mention it. Note an exception.
- [ ] CLAUDE.md names only `WebServer.cs` for `HttpListener`, but `WebServer.OpenAi.cs` uses it under the separate API-server approval. Make the wording match.

## 5. Weight (optional)

- [ ] `html/assets/*.png`: five logos at 1.8-2.0 MB each, plus a 1.08 MB `toolbar_icon.png`, all served to the UI. Resize them.
- [ ] `vscode-ext/` keeps six `.vsix` builds (2.5.0-2.8.1). Keep only the current one.
- [ ] Large test artifacts: `.claude/claude_tests/ref/images/photo0_orig.png` (3.3 MB), `rag-eval/results/final-e4b/report.png` (3.2 MB), `ref/outputs/.../load-log.txt` (883 KB).
