# Phoenix on the NVIDIA production machine

These steps test Phoenix on the production laptop (HP ZBook, NVIDIA GPU, 32 GB RAM) without changing anything there.
Every script in this folder is read-only. None installs anything, downloads anything, changes a Windows setting, or
edits Phoenix's code. They write only to `nvidia\results\`. Phoenix's own settings window can switch the GPU profile,
which writes one small file, `%LOCALAPPDATA%\LocalLLMStudio\gpu-profile.txt`.

The goal is to bring back **one zip file** with everything needed to fix Phoenix for this machine on the dev machine.
Production code is never edited.

Allow about **30 minutes**, mostly unattended.

---

## 0. Before you start

1. **Copy the Phoenix folder without** `.claude` (dev-only) and `.git`. Phoenix and these tests need nothing from
   either. Everything the probe uses is inside `nvidia\`: `tests\` holds the GPU-vs-CPU kernel tests and the accuracy
   gate, and `ref\` holds llama.cpp's reference answers.
   - You need these models in `models\`: `gemma-4-E4b_q4_0-it.gguf`, `gemma-4-E2b_q4_0-it.gguf`,
     `embeddinggemma-2-Q8_0.gguf`. `gemma-4-12b-q4_0-it-qat.gguf` is optional.
   - Put it somewhere **your own account can write to**, for example `%USERPROFILE%\Phoenix` (that is,
     `C:\Users\<you>\Phoenix`). Phoenix writes its logs into the folder, and the scripts write `nvidia\results`. Not
     `C:\Program Files`: without admin rights nothing can be written there. The commands below assume you are in
     that folder.
   - No admin rights are needed for any step. Run everything as your normal account; there is no need to "Run as
     administrator".
2. **Plug in the charger.** On battery, laptop GPUs run far slower and the numbers would be wrong.
3. **Close Phoenix** if it is running. Close its window and wait 10 seconds.
4. Open **Windows PowerShell** (not "PowerShell 7"): Start menu → type `Windows PowerShell` → open it.
   Then go to the Phoenix folder:

```bash
cd $env:USERPROFILE\Phoenix
```

> Each script keeps its window open at the end until you press **Enter**, so you can read the result. Everything a
> window shows is also saved to `nvidia\logs\<script>-<date>-<time>.log`, one file per run. Step 2 also saves its full
> report there as `...-report.txt`. Step 4 puts all of these in the zip.

> If a step prints red text you don't understand, keep going. Every step writes its findings to
> `nvidia\results\`, and the last step packs them up. A failure is useful data.

---

## 0. /unsafe check (a few seconds)

Phoenix compiles its C# with `/unsafe` (pointers) every time it starts. This checks that the machine allows it, using
every kind of pointer use Phoenix has: pointers, `stackalloc`, pinned arrays, unmanaged memory, a memory-mapped file
read through a pointer (how model files are read), and a native call. No admin rights, no GPU.

```bash
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\0-unsafe-check.ps1
```

All lines should say **PASS**, ending with `RESULT: /unsafe is not an issue on this machine.` If any line says
**FAIL**, write down that line: Phoenix cannot run as it is, and that line says why. Still run step 1.

---

## 1. Machine check (1 minute, no GPU use)

```bash
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\1-check.ps1
```

It checks:

| Check | Why it matters |
|---|---|
| Graphics cards, driver versions, real VRAM size | Laptops have two GPUs (Intel + NVIDIA). Phoenix must use the NVIDIA one. |
| `C:\Windows\System32\vulkan-1.dll` and the Vulkan driver files | Phoenix runs everything on the GPU through Vulkan. The NVIDIA driver installs it. |
| PowerShell **language mode** | Must be `FullLanguage`. Under AppLocker/WDAC it is `ConstrainedLanguage`, and Phoenix cannot compile itself at all. |
| Execution policy | `-ExecutionPolicy Bypass` on the command line normally overrides it. A `MachinePolicy` set by IT does not. |
| C# compiler test | Phoenix compiles its own C# at every start (`csc.exe` from .NET Framework 4.8). Security software sometimes blocks it. |
| Ports 127.0.0.1:8780-8789 (server), 8790-8798 (client) and :1234 | Phoenix's window and its API listen there (this computer only). |
| Chrome / Edge | Phoenix opens its window in one of them. |
| `Phoenix_Projects`, `.phoenix`, `LocalLLMStudio` folders writable | Where projects, chats and settings live. |
| Battery, power plan, GPU timeout (TDR) setting | Laptop GPU speed, and how long Windows lets one GPU job run (default 2 s). |
| `src\run.ps1 -CheckOnly` | Phoenix's own rule checks (no network, no third-party code). |

**Look at the Summary at the end.** "No problems found" means go on to step 2. If it lists problems:

| Problem | What it means / what to do |
|---|---|
| `language mode ConstrainedLanguage` | **Show-stopper.** IT policy blocks Phoenix. Note it and still run step 4 (collect). |
| `C# compiler blocked` | **Show-stopper**, same as above. |
| `MachinePolicy: AllSigned` / `RemoteSigned` | Scripts may need to be signed. Try anyway; if steps 2–3 refuse to run, note it. |
| `No NVIDIA graphics card is visible` | Phoenix will use the Intel GPU (slow) or the CPU. Carry on; the probe shows what Vulkan sees. |
| `vulkan-1.dll is missing` | The NVIDIA driver is incomplete. Phoenix will run on the CPU only. Carry on. |
| `Running on battery` | Plug in and run step 1 again. |
| Port in use | Phoenix is still running: close it, wait, run again. |

---

## 2. GPU probe (10–20 minutes, uses the GPU heavily)

Phoenix must be **closed**. Don't use the laptop for anything heavy while this runs. The screen may flicker once; that's
fine and it's recorded.

```bash
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\2-probe.ps1
```

If the card has 10 GB of VRAM or more (step 1 shows it), also test the 12B model:

```bash
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\2-probe.ps1 -Big
```

The probe runs, in **normal** mode and then **safe** mode (no subgroup / integer-dot fast paths):

1. **Every Vulkan device in full**: name, vendor, driver, limits, subgroup size (NVIDIA is 32, the AMD dev card is 64),
   fp16/int8/integer-dot support, memory heaps and types, and all extensions. This is what decides whether a kernel
   needs a change.
2. **Which GPU Phoenix picks** with profile `auto` and with profile `nvidia`.
3. **Kernel tests**: every GPU kernel against the same math on the CPU.
4. **Accuracy gate**: E2B and E4B answers compared token by token with llama.cpp's reference log-probabilities. This
   catches "it runs but gives garbage".
5. **Speed**: load time, prompt speed at 128 / 1,024 / 3,000 tokens, generation speed, and the **longest single GPU
   job**. It must stay well under 2,000 ms, or Windows resets the driver and the screen goes black for a moment.

It ends with `All checks passed` or `N check(s) failed`. The report is `nvidia\results\probe.txt`.

**How to read it:**
- `KERNELS FAIL (normal)` with `KERNELS PASS (safe)`: a fast path is wrong on this GPU. Use **safe** mode for now
  (step 3), and bring the zip back so I can fix the fast path.
- `GATE FAIL` in both modes: a deeper problem. Bring the zip back.
- `longest GPU job` above ~1,000 ms: risk of the screen blanking. The **NVIDIA** profile halves the work per GPU job.
- `had to go to system memory (VRAM full)`: the model doesn't fit in VRAM. Use a smaller model.
- `!!! ... FAILED` with an exception: the GPU or driver refused something. The exception text says what; bring it back.

On the AMD dev machine for comparison (E4B): about 735 prompt tok/s at 1,024 tokens, 74 tok/s generation, longest job
76 ms. Safe mode runs prompts about 3 times slower.

---

## 3. Phoenix itself, with the NVIDIA profile (5 minutes)

1. Start Phoenix with `Phoenix_LAI.lnk` (in the Phoenix folder).
2. Type `/debug on` and press Enter. This logs every step and every slow GPU job.
3. Type `/settings`. Under **app**, set **dev: GPU** to **NVIDIA**.
   - Leave **dev: safe GPU kernels** off, unless step 2 showed `KERNELS FAIL (normal)` / `GATE FAIL ... (normal)`
     that passed in safe mode. Then turn it on.
   - Click **Apply**. The profile takes effect at the next model load.
4. Load the model: type `/model gemma-4-E4b_q4_0-it` (Tab completes the name) and press Enter. If it was already loaded,
   do this anyway: the profile applies at load.
5. Have a short chat. Use these, one at a time, and wait for each answer:
   - `Hello, what can you do?`
   - `Write a PowerShell 5.1 function that lists the 5 largest files in a folder.`
   - `Write an Oracle SQL query for the top 3 salaries per department.`
   - Something longer: paste a page of text and ask for a summary (tests a long prompt).
6. **Optional**, if you have a project folder in `%USERPROFILE%\Phoenix_Projects`: `/folder <project>` (Tab completes), then ask it to
   read and change a file. This also exercises the project index.
7. Type `/debug off`, then close Phoenix's window. The server stops by itself within a few seconds.

Watch for: garbage text or repeated characters (a kernel problem), very slow answers, the screen blanking, or Phoenix
saying "GPU unavailable … using the CPU". Note what you saw and roughly when.

> You can switch back any time: `/settings` → dev: GPU → **Auto**, Apply, reload the model.

---

## 4. Collect everything (1 minute)

```bash
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\3-collect.ps1
```

This packs into `nvidia\results\phoenix-nvidia-<computer>-<date>.zip`:
- `check.txt` and `probe.txt` from steps 1–2;
- the 5 newest Phoenix session logs (`logs\session-*.log`), including the GPU line Phoenix logs at each model load;
- Phoenix's small settings files (`%LOCALAPPDATA%\LocalLLMStudio\*.txt`, not the signed guard files);
- `dxdiag.txt` (Windows' own graphics report);
- display-driver resets/crashes and PowerShell crashes from the Windows event log (last 2 days).

> **Privacy:** the session logs contain the chat text from step 3 (and any earlier chats on that machine). Open the
> zip and check them before it leaves the machine, if that matters.

**Bring that one zip file back** to the dev machine and give it to Claude. With it I can tell which kernels, limits
or memory settings need changing, make the changes on the dev machine, verify them, and you copy the updated Phoenix
folder over.

---

## What the NVIDIA profile does (dev toggle)

`/settings` → **dev: GPU** writes `%LOCALAPPDATA%\LocalLLMStudio\gpu-profile.txt`, read at every model load:

| Setting | Effect |
|---|---|
| **Auto** (default) | The best GPU: discrete before integrated. |
| **AMD** | Prefers an AMD GPU when there are several. |
| **NVIDIA** | Prefers the NVIDIA GPU, so a two-GPU laptop never lands on the Intel chip, and **halves the layers per GPU job** (Gemma 12 → 6, Llama 8 → 4). Shorter jobs stay far from Windows' 2 s GPU timeout on a throttling laptop GPU. |
| **dev: safe GPU kernels** | Turns off subgroup arithmetic and the packed integer dot product, so every reduction uses plain shared memory. Slower (prompts about 3 times), but it avoids any driver bug in those fast paths. |

Every model load logs one line describing the GPU in the session log, for example:
`GPU NVIDIA RTX 2000 Ada (NVIDIA, discrete, driver 5xx.xx, Vulkan 1.3.x); profile nvidia; subgroup 32 (arithmetic); integer dot accelerated; shared memory 49152 B; VRAM 8188 MB (...); devices: Intel(R) UHD Graphics [Intel integrated], NVIDIA RTX 2000 Ada [NVIDIA discrete]`

Already handled, nothing to set:
- Uploads go through a staging buffer, so it doesn't matter that laptop NVIDIA GPUs can't map VRAM for the CPU.
- Reductions don't depend on subgroup width: 32 (NVIDIA) and 64 (AMD) both work.
- If VRAM runs out, buffers fall back to system memory, and the log says so.

---

## If something blocks you

| Symptom | Likely cause | What to note |
|---|---|---|
| "running scripts is disabled on this system" | Execution policy set by IT (`MachinePolicy`) | The exact message; step 1's policy list |
| "Cannot add type" / compiler errors in step 2 | `csc.exe` blocked, or files missing from the copy | The first lines of `probe.txt` |
| Step 2 stops with no summary | The driver reset or the process crashed | Run step 4 anyway: the event log and `probe.txt` (written line by line) show where |
| Phoenix window never opens | Chrome/Edge blocked or missing, or the port is taken | Step 1's browser and port sections |
| Phoenix says "using the CPU" | Vulkan not available, or the wrong GPU | The GPU line in the session log |
| The Graphics Settings page lists `powershell.exe` | Windows' per-app GPU preference | Optional and needs your OK: Settings → System → Display → Graphics → `powershell.exe` → High performance. The NVIDIA profile normally makes this unnecessary. |
