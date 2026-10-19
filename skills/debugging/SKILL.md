---
name: debugging
description: Systematic debugging - read the exact error, form one hypothesis at a time, narrow down to the root cause, then fix it. Use when something fails, crashes, throws an error or exception, gives a wrong result, or the user pastes an error message or stack trace and asks why or how to fix it.
---

# Debugging

## Steps

1. **Read the error exactly.** Quote the key line of the message or stack trace. Note the file, line and the innermost frame that is in the user's own code.
2. **Read that code.** Open the file at that line. Read enough around it to know what each variable holds there.
3. **State one hypothesis.** "X is null here because Y never sets it when Z." One cause at a time, the most likely first.
4. **Confirm it from the code**, not from a guess: follow the value back to where it is set. If the code cannot settle it, give the user one short command or log line that would, and say what each outcome means.
5. **Fix the root cause.** Change the code where the bad value or state starts, not where it finally crashes. Keep the fix small.
6. **Say what to run** to confirm the fix (the exact command or the steps to repeat), since you cannot run it yourself.

## Rules

- Never "fix" an error by catching and ignoring it.
- Never change several unrelated things at once hoping one works.
- If the error message and the code disagree, trust the error message and re-read the code.
- If you need information you do not have (a file, the full error, a version), ask for exactly that.

## Output format

**Cause:** one or two sentences, with `file:line`.
**Fix:** the edit.
**Check:** the command or steps that confirm it.
