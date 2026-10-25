#!/bin/sh
# Dev: prepares the running server (index the docs it serves, load a model) and runs RAG-eval arms against it.
#   sh .claude/claude_tests/rag-eval/arm.sh <model.gguf> <arms> <out-dir> [langs]
# The server must already be started (preview tool: phoenix-server for off/new, phoenix-oldrag for old).
B=http://127.0.0.1:8780
MODEL=$1; ARMS=$2; OUT=$3; LANGS=${4:-powershell,csharp,oracle,sql,duckdb,javascript}
until curl -s $B/api/info >/dev/null; do sleep 2; done
curl -s -X POST $B/api/index -H "Content-Type: application/json" -d '{}' >/dev/null
sleep 2
until curl -s $B/api/info | grep -q '"indexing":null'; do sleep 3; done
curl -s -X POST $B/api/load -H "Content-Type: application/json" -d "{\"name\":\"$MODEL\",\"context\":32768,\"kv\":\"q8\"}" >/dev/null
until curl -s $B/api/info | grep -q "\"file\":\"$MODEL\"" && ! curl -s $B/api/info | grep -q '"loading":true'; do sleep 3; done
echo "ready: $(curl -s $B/api/info | grep -o '"rag":{[^}]*}')"
python -X utf8 "$(dirname "$0")/rag-eval.py" --arms "$ARMS" --out "$OUT" --langs "$LANGS"
