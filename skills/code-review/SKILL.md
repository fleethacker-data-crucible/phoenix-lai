---
name: code-review
description: Reviews code for real bugs first, then duplication and needless complexity, and reports ranked findings with a concrete failure for each. Use when asked to review, audit, check, QA or find bugs in code, a file, a function or a change.
---

# Code review

Find what is actually wrong. A short list of real problems beats a long list of opinions.

## Steps

1. Read the whole code under review, and the code it calls when a finding depends on it.
2. Look for problems in this order:
   1. **Correctness:** wrong results, crashes, off-by-one, null or empty input, wrong conditions, unhandled errors, resource leaks, race conditions.
   2. **Security:** unchecked input reaching files, paths, queries or HTML; secrets in code.
   3. **Duplication:** the same logic in two places that can drift apart.
   4. **Dead code:** functions, variables or branches nothing uses.
   5. **Needless complexity:** code that can be much simpler with the same behaviour.
3. For every finding, prove it: name the input or situation and what goes wrong. Drop any finding you cannot prove.
4. Rank the findings, most severe first.

## Output format

For each finding:

**N. Short title** - `file:line`
What is wrong, in one sentence. **Fails when:** the concrete input or situation and the result. **Fix:** one sentence.

End with one line: how many findings and whether anything was not checked. If nothing is wrong, say so plainly.

## Rules

- Do not report style preferences as bugs.
- Do not pad the list. Zero findings is a valid result.
- Quote the exact line you mean; never guess line numbers.
