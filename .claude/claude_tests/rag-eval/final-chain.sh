#!/bin/sh
# Dev: the final uniform runs on one server (frozen code): for each model, the given arms over all 16 languages.
#   sh .claude/claude_tests/rag-eval/final-chain.sh <arms> <out-prefix> <model.gguf>...
ARMS=$1; PREFIX=$2; shift 2
ALL=powershell,csharp,oracle,sql,duckdb,javascript,powershell7,vbscript,vba,cmd,dax,powerquery,qlik,excel,html,css
for M in "$@"; do
  TAG=$(echo "$M" | sed 's/gemma-4-//; s/_q4_0.*//; s/-q4_0.*//' | tr 'A-Z' 'a-z')
  echo "=== $M -> ${PREFIX}-${TAG}"
  sh "$(dirname "$0")/arm.sh" "$M" "$ARMS" "${PREFIX}-${TAG}" "$ALL"
done
