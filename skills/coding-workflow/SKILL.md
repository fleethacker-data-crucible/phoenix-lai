---
name: coding-workflow
description: Careful workflow for changing code in a project - read before editing, make the smallest correct change, match the surrounding style, and check the result. Use when asked to fix a bug, implement or add a feature, refactor, rename, update or change code or files in the project.
---

# Coding workflow

Work like a careful senior engineer: understand first, change little, verify, report briefly.

## Steps

1. **Read before you write.** Open the files the request touches (and the callers of any function you change) before editing. Never edit a file you have not read in this conversation.
2. **Find the real cause.** For a bug, name the exact line and why it is wrong before fixing it. Fix the cause, not the symptom.
3. **Make the smallest correct change.** Change only what the request needs. No drive-by renames, reformatting or "improvements" nobody asked for.
4. **Match the code around it.** Same naming, indentation, comment density, error handling and idioms as the file you are in. If the project uses tabs, use tabs.
5. **Keep everything consistent.** When you rename or change a signature, update every caller. Search the project for other uses first.
6. **Check your work.** Re-read the edited region: brackets balance, names exist, types match, nothing half-done. If the change needs a build or test, give the user the exact command to run.
7. **Report briefly.** One or two sentences: what changed and anything not done or not verified. No recap of the diff.

## Rules

- Never invent APIs, files, functions or settings. If you are not sure something exists, read or search for it.
- Never leave placeholders (`TODO`, `...`, "rest of the code here") in code you write.
- Prefer editing an existing file over creating a new one.
- If the request is ambiguous and a wrong guess would be costly, ask one short question instead of guessing.
- Do not delete code or files unless the request clearly asks for it.

## Checklist before answering

- [ ] I read every file I changed.
- [ ] The change is complete: no placeholders, all callers updated.
- [ ] Style matches the surrounding code.
- [ ] I said what was not verified.
