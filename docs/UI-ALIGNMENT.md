# Phoenix LAI: Fleet UI alignment plan and tech stack review

**2026-10-25 · against Fleet `docs/UNIFIED-UI.md` v0.1 and the repo's `TechStack` file · Phoenix `master` @ 609494f (VERSION 2026.1022.1600)**

Phoenix today is a well-built app with its own look: a dark charcoal "terminal" theme, gold accent, Exo 2 / Syne Mono
type, a grid-and-glow canvas background, and a glossy black-and-gold cyber phoenix logo. It is plain HTML/CSS/JS
(about 3,800 lines in `html\`) served by a C# `HttpListener` on loopback. None of the Fleet requirements are met yet,
but the structure (sidebar, top bar, modal windows, slash commands) maps onto the Fleet shell cleanly. This is a
reskin and a shell change, not a rewrite.

---

## 0. Decisions needed before work starts

| # | Decision | Why it blocks | Recommendation |
|---|---|---|---|
| D1 | Does Phoenix join the Fleet? | It is not in UNIFIED-UI section 1 (no row, no accent, no port entry). | Yes. Add a row; it is the Fleet's local-AI tool. |
| D2 | Name | Every Fleet name is nautical; "Phoenix" is not. | Keep **Phoenix** as the name, and make the emblem subject a phoenix **figurehead** (a carved ship's figurehead), which keeps the name and gives the nautical link. The README's "The name" note can say so. Your call. |
| D3 | Signature accent | Phoenix's gold `#e8c15a` sits between Lazarette brass `#c9a66b` and Eight Bells bronze `#d6a258`; three golds in one family is unreadable as a signature. | **Ember**: night `#f2884b` (7.1:1), chart `#b34a12` (5.1:1). Alternative: **flame rose** `#f0789a` / `#b02a5a` (6.6:1 / 6.0:1), the only unused hue family. Ember is closer to Capstan copper `#d9845a`; check them side by side before choosing. |
| D4 | Lucide icons | UNIFIED-UI requires Lucide (ISC). Phoenix's CLAUDE.md says "no third-party code". | Approve vendored Lucide **SVG files** (data, not code) as a dated exception in CLAUDE.md, like the existing ones. Phoenix's gear icon is already Lucide's path. |
| D5 | Where `fleet.css` comes from | No `fleet-design` repo yet. | Copy `Lazarette\ui-src\vendor\fleet\fleet.css` (v0.1, 108 lines) to `html\vendor\fleet\fleet.css` and add a `[data-app='phoenix']` accent line upstream in every copy. |
| D6 | User accent picker | Settings has a 10-swatch accent picker ("recolours everything"). Fleet: the accent is the app's signature. | Remove it. |
| D7 | VS Code panel | `vscode.html` / `vscode.css` follow VS Code's own theme via `vscode-theme.js`. | Exempt it: a webview inside VS Code should match VS Code, not the Fleet. Apply only fonts and tokens for status colors. |

---

## 1. Gap analysis

Legend: ✅ meets · 🟡 partial · ❌ missing or conflicting.

### Color and themes (UNIFIED-UI section 3)

| Requirement | Phoenix today | Status |
|---|---|---|
| Only `--fl-*` tokens, no raw colors | ~271 raw hex/rgba literals across `css\`, `js\` and HTML; own token names (`--bg`, `--text`, `--accent`…). Status colors hard-coded: `#4caf50`, `#e05555`, `#4a9eff`, `#ff3b3b`. | ❌ |
| Night watch + Chart room | Dark charcoal `#1b1b1b` default; light mode `#f4f3ef` via `:root.light` | 🟡 two themes, wrong palette |
| Follows `prefers-color-scheme`, `data-theme` override | Not used; a checkbox toggles a `.light` class | ❌ |
| Theme saved as `<app>-theme` in try/catch | Saved inside `phoenix.settings` (try/catch ✅) | 🟡 |
| Accent only for primary action, active nav, selection | Accent drives glows, headings, chevrons, user text, background canvas | ❌ |
| `server.css` (dashboard, console) | Separate copy of the same palette | ❌ |

### Typography (section 4)

| Requirement | Phoenix today | Status |
|---|---|---|
| Segoe UI Variable body, Cascadia Mono | Exo 2 (sans), Syne Mono (chat default), Roboto, Inter | ❌ |
| MUST NOT use Inter | `html\fonts\Inter.ttf` shipped and offered as a chat font | ❌ |
| Scale 12/13/14/16/20/28 | 15 px body, plus 10.5, 11.5, 12.5, 13.5, 18, 26 | ❌ |
| Display serif only for wordmark, titles, empty state | None | ❌ (font still an open Fleet decision) |

The four TTFs (1.7 MB) can all be deleted once the stacks switch to built-in Windows faces.

### Shell and layout (section 5)

| Requirement | Phoenix today | Status |
|---|---|---|
| Sidebar 240 px, collapsible to 64 px, emblem + wordmark at top | 264 px, not collapsible; hidden entirely under 760 px; 28 px icon + "Phoenix Client" text | 🟡 |
| Header 56 px: title left, actions right | 52 px top bar: model picker + speed. Settings gear is in the sidebar brand row. | 🟡 |
| Status bar 28 px: engine · address · session · version | None. A 25 px green "LOCAL DEMO" banner sits across the top. | ❌ |
| 4 px grid, radii 6/10/14 | Mixed (5 px `--r`, 4, 6, 8 px radii; 5/6/9/10/22 px paddings) | ❌ |
| Cards use borders, one shadow level for popovers | Menus use `0 12px 40px` shadows; background canvas glows | 🟡 |
| 1280×720 without horizontal scroll; auto-collapse < 1024 px | Works at 1280; sidebar disappears < 760 px instead of collapsing | 🟡 |

### Components (section 6)

| Fleet class | Phoenix equivalent | Gap |
|---|---|---|
| `.fl-btn` | Mixed `button`, `.primary`, `.prow > button` | Heights vary (~24–30 px); not 32 px |
| `.fl-input` | Inputs with only a bottom border | Needs full `--fl-control-border` box, 32 px, brass focus ring |
| `.fl-card` | Dashboard `.card` (8 px radius, 12/14 px padding) | 10 px radius, 16 px padding, tokens |
| `.fl-table` | Dashboard tables (4 px padding rows) | 36 px rows, sticky header, mono right-aligned numbers (mono ✅ already) |
| `.fl-badge` | Provider `.feat` chips, `.ptag` | Status tokens; always text (mostly ✅) |
| `.fl-toast` | None (system lines in chat) | Add for background events (provider down, index done) |
| `.fl-dialog` | `.modal > .window.settings` | 14 px radius, focus trap, Esc ✅ already |
| `.fl-empty` | `.empty` with stacked dark/light logos that cross-fade on click (1.8 s) | Line-art emblem, serif headline, one primary action, ≤ 200 ms |
| `.fl-nav` | `.snav` / `.sitem` (active project shown in **green**) | Accent text + 3 px left accent bar |

### Interaction and accessibility (section 7)

| Requirement | Phoenix today | Status |
|---|---|---|
| Visible 2 px brass focus ring everywhere | One `:focus-visible` rule; several `:focus { outline: 0 }` (e.g. `.dd .ddbtn`, `.dd .ddmenu button`) | ❌ |
| Status never color-only | Provider list and model menu color the **text** green/red/blue with a dot, no word | ❌ |
| `?` shortcut help dialog | `/help` slash command exists | 🟡 add `?` |
| ≤ 200 ms transitions, off under reduced motion | 1.8 s logo fade, 0.4 s bars, typing effect, flashing ShellGuard banner; no `prefers-reduced-motion` | ❌ |
| 32×32 targets | Sidebar `+` buttons and row delete icons are about 15–24 px | ❌ |
| Contrast AA | e.g. disabled model `#55545c` on `#2a2a2a` and `--status-text #55555f` fail 4.5:1 | ❌ |
| No placeholder numbers; "—" + hint | Dashboard shows "–" (en dash) without a hint | 🟡 |

The ShellGuard WARNING banner must keep working: under reduced motion show it static, with an icon and the word
"WARNING", instead of flashing.

### Brand (section 8)

| Requirement | Phoenix today | Status |
|---|---|---|
| Scrimshaw emblem frame | Glossy 3D black-and-gold phoenix with circuit traces, "PhoenixLAI" metallic wordmark | ❌ (image brief in section 5 below) |
| `assets/brand/`: emblem, `mark.svg`, `favicon.ico`, `tray.ico` | `html\assets\` holds 6 PNGs (≈8.6 MB total; each logo ≈1.8–2 MB); favicon is a 1254 px yellow rounded-square PNG | ❌ |
| README opens with emblem, one-liner, "The name" | Opens with title and tagline, no emblem | ❌ |

Keep the current PNGs in the repo (or move them to `assets/brand/originals/`); they are the originals.

### Launch, ports and naming (section 9)

| Requirement | Phoenix today | Status |
|---|---|---|
| `Launch-<App>.bat`, `Start-<App>.ps1`, `Stop-<App>.ps1` | `Phoenix_LAI.bat`, `src\Start-PhoenixLAI.ps1`, `.lnk` shortcuts, `.claude\tools\close-phoenix.ps1` (dev only) | 🟡 |
| Registry port, fall back to a free port, show it | 1234 (API), 8780–8789 server, 8790–8798 client, from edge-ops `PORTS.md` | 🟡 works; not in UNIFIED-UI table; not shown in UI |
| Stop after last tab closes, `--stay` | VS Code instance stops ~30 s after last chat; client window has an Exit dialog | 🟡 |
| `%LOCALAPPDATA%\<App>` | `%LOCALAPPDATA%\LocalLLMStudio` and `%APPDATA%\PhoenixLAI` | ❌ |
| Storage keys `<app>-…` | `phoenix.settings` | ❌ (rename to `phoenix-settings`, `phoenix-theme`, with migration) |

**Risk:** the ShellGuard lockout's HMAC-signed files live in both of those folders, and `run.ps1` refuses to start if
the guard is tampered with. Moving the data folder must copy the lockout files forward *and* keep reading the old
location until the new one exists, or a folder move becomes a way to clear a lockout. Treat this as a security change
and add it to `.claude\docs\SECURITY.md`.

---

## 2. Tech stack review

Three documents set the limits, and they are not the same:

| | `TechStack` (repo root) | Phoenix `CLAUDE.md` hard rules | Fleet `UNIFIED-UI.md` |
|---|---|---|---|
| Server | PS 5.1 + `TcpListener` | C# `HttpListener` on loopback (approved exception, only in `WebServer.cs`) | No rule |
| UI framework | React 19 + TS, or Svelte 5 | Plain HTML/JS, no third-party code | Any; plain HTML uses `.fl-*` directly |
| Styling | Tailwind 4 + shadcn (CVA, Radix, Lucide) | Hand-written CSS | `fleet.css` + tokens |
| Charts | ECharts, React Flow, Motion | Hand-drawn SVG sparklines | No rule |
| Build | Built off-workstation, ship compiled bundles | Nothing is built; PowerShell compiles C# in memory | No rule |
| Native | C# via `Add-Type` (.NET 4.8.1) | C# 5 via CodeDom `Add-Type` (.NET 4.8) | — |
| Secrets | DPAPI | Windows Credential Manager (PasswordVault) | — |
| Storage | IndexedDB, localStorage | localStorage + files on disk | `<app>-` key prefix |

### What the stack would allow that Phoenix doesn't use

- **React/Svelte + Tailwind + shadcn.** Allowed by `TechStack`, but it would break Phoenix's own "zero third-party
  code" rule and add an off-workstation build to a project whose whole point is that nothing is built. Not worth it:
  the Fleet path for plain-HTML apps (`fleet.css` + `.fl-*` classes) gets Phoenix fully aligned with no framework.
  **Recommendation: stay plain HTML/JS.**
- **ECharts.** The dashboard has two sparklines and a bar. Hand-drawn SVG is fine; revisit only if the dashboard
  grows real charts.
- **Web Workers.** Worth considering for Markdown rendering and syntax highlighting of very long replies
  (`markdown.js`, `highlight.js` run on the main thread). Optional, not an alignment item.
- **IndexedDB.** Chats live on disk via the client already; no need.
- **Service workers.** Not useful: the pages are served by the local client, and offline is already the default.

### Where Phoenix is stricter than the stack, on purpose

- No network except `Providers.cs` and the loopback server; no `Process.Start` except the browser app window.
- No third-party code at all (BCL only), no DLLs, Vulkan loader from the driver only.
- PowerShell 5.1 only, C# 5 only.

These are stricter than `TechStack`, not in conflict with it, and nothing in the alignment work needs to loosen them.
The one loosening needed is D4 (vendored Lucide SVGs, and `fleet.css` as a Fleet-owned file).

### Where Phoenix differs from the stack and should note it

- `HttpListener` instead of `TcpListener`: already approved; record the reason in `TechStack` or `CLAUDE.md` so
  the two files stop disagreeing.
- `TechStack` lists Lucide inside the shadcn row; for a plain-HTML app, Lucide means the SVG files only.

---

## 3. Work plan

Each phase is a separate PR. Phases 1–2 change the look the most; do them first.

### Phase 1: Tokens, themes, type (foundation)
1. Vendor `html\vendor\fleet\fleet.css`; add `[data-app='phoenix']` with the D3 accent (and add it to every Fleet copy).
2. Link it before `chat.css`, `skills.css` and `server.css`; set `<html data-app="phoenix">` on every page.
3. Map Phoenix's variables onto Fleet tokens first (`--bg: var(--fl-background)`, `--text: var(--fl-foreground)`,
   `--ok: var(--fl-success)`, `--err: var(--fl-danger)`…), then replace the raw literals file by file.
   Leave the syntax-highlight palette (`.hl-*`) as its own small token set, `--fl-code-*`, in both themes.
4. Theme: drop the `.light` class; follow `prefers-color-scheme`, override with `data-theme`, store `phoenix-theme`.
   Settings › appearance becomes System / Night watch / Chart room.
5. Fonts: switch to the Fleet stacks; delete `Inter.ttf`, `Roboto.ttf`, `Exo2-Variable.ttf`, `SyneMono-Regular.ttf`.
   Chat font picker becomes Sans / Mono (Segoe UI Variable vs Cascadia Mono). Snap sizes to the Fleet scale.
6. Remove the accent picker (D6).
7. Grid background: off by default, drawn in `--fl-border` with no accent glows (or remove it).

### Phase 2: Shell
1. Sidebar 240 px, collapse to 64 px (icon rail) with a toggle; auto-collapse under 1024 px instead of hiding.
2. Brand row: `mark.svg` + PHOENIX wordmark; move the settings gear to the header actions.
3. Header 56 px: page title (chat title or "New chat") left; model picker, speed and settings right.
4. Status bar 28 px:
   `● Running / Disconnected` (the active provider; Phoenix server state when it is the provider) ·
   `127.0.0.1:<bound client port>` · `<provider> · <model>` · `v2026.1022.1600`.
   Fold "LOCAL DEMO" into it as an optional badge instead of a full-width banner.
5. Tray pop-up (`src\TrayPanel.cs`, WinForms): it copies `server.css` colors and fonts in C# and draws
   `toolbar_icon.png`; give it the Night watch / Chart room values, Segoe UI, and the new mark.
6. Dashboard and console: the same shell (nav: Dashboard, Console; status bar: server state, `127.0.0.1:1234` and
   the dashboard port, loaded model, version).

### Phase 3: Components
Buttons, inputs, cards, tables, badges, dialogs, nav and empty state to `.fl-*` (section 1 table). Add `.fl-toast`
for provider up/down and indexing events. Empty state: line-art phoenix, serif "What are we building?"-style
headline, one primary action (New project or Pick a model).

### Phase 4: Accessibility
Brass focus ring on everything (remove every `outline: 0`), text labels next to every status dot, 32 px targets,
`?` help dialog listing keys and slash commands, `prefers-reduced-motion` (no typing effect, static ShellGuard banner,
no cross-fade), contrast pass on both themes, "—" plus hint for missing dashboard numbers.

### Phase 5: Brand
New emblem (section 5 brief), then `assets/brand/` with `emblem-dark.png`, `emblem-light.png`, `mark.svg`,
`favicon.ico` (16/32/48), `tray.ico` (16/20/24/32, used by `TrayPanel.cs`). Point pages at the small files instead
of the 2 MB PNGs. Update README header with emblem, one-liner and "The name". Move current logos to
`assets/brand/originals/`.

### Phase 6: Launch, data, naming
`Launch-Phoenix.bat` (starts server + client), `Start-Phoenix.ps1`, `Stop-Phoenix.ps1` (stops both; promote
`close-phoenix.ps1`). Keep the `.lnk` shortcuts. Data to `%LOCALAPPDATA%\Phoenix` with copy-forward migration and
the ShellGuard care above. `phoenix.settings` → `phoenix-settings` + `phoenix-theme`. Add Phoenix to UNIFIED-UI
section 1 (ports 1234 / 8780 / 8790) and section 3.3 (accent).

### Phase 7: Verify
Run the existing checks (`run.ps1` locality rules, `tests\*.Tests.ps1`, `client-e2e.py`) after each phase; then the
UNIFIED-UI section 11 checklist and both-theme screenshots in `docs\`.

**Out of scope:** the slash-command workflow, the bottom-anchored chat layout, the "no menus" principle. Those are
Phoenix's own workflow and the Fleet keeps them ("family, not clones").

---

## 4. Effort

| Phase | Size | Notes |
|---|---|---|
| 1 Tokens/themes/type | M | Mostly mechanical; ~271 literals |
| 2 Shell | M | Status bar needs `/api/info` fields (port, version) the page may not have yet |
| 3 Components | M | |
| 4 Accessibility | S–M | |
| 5 Brand | S (after art exists) | |
| 6 Launch/data | M | ShellGuard migration needs care and a security note |
| 7 Verify | S | |

---

## 5. Emblem brief (for image generation)

Attach two images: the current Phoenix logo (`dark_phoenix_logo.png`) for the subject and the Halyard emblem
(`Halyard\assets\brand\emblem-dark.png`) for the frame and style. Then use this prompt:

> Redraw this phoenix as an emblem in the exact style and frame of the second image (the Halyard emblem). Keep the
> second image's layout and only change the subject and the name.
>
> **Frame:** a circular deep ink-navy field (#071a26) with a thin brass ring (#c9a66b). Small compass-point rays stick
> out of the ring at 3 o'clock and 9 o'clock. Set the whole emblem on a plain #071a26 background.
>
> **Subject:** a phoenix rising, wings spread, drawn as a hand-engraved scrimshaw / copperplate etching: fine ivory
> (#f1e4c8) line work with cross-hatching for shadow, like a carved ship's figurehead. The wing tips and tail may break
> out of the circle. Inside the circle behind it: a dark night sea and a few small stars, as in the reference. Use
> ember orange (#f2884b) only in the flames at the bird's tail and wing edges, sparingly. Optionally, a few feathers
> carry very faint engraved circuit-trace lines, as a quiet nod to AI; they must read as engraving, not glowing
> electronics.
>
> **Wordmark:** "PHOENIX" below the circle in a classic engraved display serif, all capitals, ivory (#f4ead6), the same
> style and weight as "HALYARD" in the reference. Under it, a small four-point compass star between two thin brass
> rules.
>
> **Avoid:** glossy or metallic 3D rendering, chrome, lens flares, neon glow, glitch pixels, gradients on the
> lettering, the word "LAI", any other text, and yellow-gold as the main color.
>
> Square, 1024×1024, centered, flat illustration.

Follow-ups to ask for in the same chat:

1. "Now the light version: same drawing on #fbfaf2 parchment, navy (#132633) line work and wordmark, brass (#8a6b45)
   ring and rules, ember (#b34a12) flames."
2. "Now an icon-only mark: just the phoenix inside the brass ring, no wordmark, no sea or stars, bold simplified lines
   that still read at 32×32 pixels, on a transparent background." (This becomes `mark.svg` after tracing; the
   detailed PNGs stay as the originals.)

If D3 picks flame rose instead of ember, swap `#f2884b` / `#b34a12` for `#f0789a` / `#b02a5a`.
