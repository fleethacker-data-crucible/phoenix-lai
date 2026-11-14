# GHAS alert triage (sprint 2026-11)

Open code-scanning alerts from the sprint list. These were not dismissed in GitHub.

| Alert | Rule | Path | Disposition | Reason |
| --- | --- | --- | --- | --- |
| code-scanning #2 | cs/path-injection | src/WebServer.OpenAi.cs:217 | fixed | `GET /v1/models/{id}` now accepts only a single file name (no `..`, slash, or drive) and calls `File.Exists` only after `Path.GetFullPath` stays inside the models folder; the JSON `model` field uses the same check. |
| code-scanning #1 | cs/redos | src/WebServer.Skills.cs:68 | fixed | Skill folder names are length-capped before matching, the pattern is an equivalent non-nested form, and the `Regex` is constructed with a one-second match timeout. |
