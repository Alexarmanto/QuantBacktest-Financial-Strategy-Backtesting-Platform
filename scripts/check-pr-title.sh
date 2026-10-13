#!/usr/bin/env bash
# Verifies that a PR title follows "type(scope): summary" (Conventional Commits, docs/GIT_WORKFLOW.md).
# Usage: bash scripts/check-pr-title.sh "feat(engine): execute signals at next open"
set -euo pipefail

title="${1:-}"
types='feat|fix|test|docs|refactor|perf|chore|ci'
scopes='core|fakes|data|reports|docker|strategies|analytics|engine|web|docs|ci|repo'
regex="^(${types})\((${scopes})\): [a-z0-9].{3,70}$"

if [[ ! "$title" =~ $regex ]]; then
  echo "::error::Invalid PR title: '$title'"
  echo "Expected: type(scope): summary"
  echo "  type   : ${types//|/ | }"
  echo "  scope  : ${scopes//|/ | }"
  echo "  summary: imperative English, starts with a lowercase letter or digit, 4 to 71 characters"
  echo "Example  : feat(engine): execute signals at next open"
  exit 1
fi

echo "PR title OK: $title"
