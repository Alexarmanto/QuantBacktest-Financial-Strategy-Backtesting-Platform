#!/usr/bin/env bash
# Verifies that a branch only changes files inside the scope of its part (docs/GIT_WORKFLOW.md).
# Usage: bash scripts/check-scope.sh <branch-name> [base-ref]   (base-ref defaults to origin/main)
set -euo pipefail

branch="${1:?branch name required}"
base="${2:-origin/main}"

# Files every part may touch (additive package versions only, enforced in review).
shared=("Directory.Packages.props")

case "$branch" in
  part1/*)
    own=("src/QuantBacktest.Data/" "src/QuantBacktest.Reports/"
         "tests/QuantBacktest.Data.Tests/" "tests/QuantBacktest.Reports.Tests/"
         "data/seed/" "Dockerfile" "docker-compose.yml" ".dockerignore" ".env.example"
         "docs/report/part-1.md" "docs/report/images/part1-")
    ;;
  part2/*)
    own=("src/QuantBacktest.Strategies/" "src/QuantBacktest.Analytics/"
         "tests/QuantBacktest.Strategies.Tests/" "tests/QuantBacktest.Analytics.Tests/"
         "docs/report/" "docs/user-manual/")
    ;;
  part3/*)
    own=("src/QuantBacktest.Engine/" "tests/QuantBacktest.Engine.Tests/"
         "docs/report/part-3.md" "docs/report/images/part3-")
    ;;
  part4/*)
    own=("src/QuantBacktest.Web/" "tests/QuantBacktest.Web.Tests/"
         "docs/report/part-4.md" "docs/report/images/part4-")
    ;;
  contract/*)
    own=("src/QuantBacktest.Core/" "src/QuantBacktest.Fakes/"
         "tests/QuantBacktest.Core.Tests/" "tests/QuantBacktest.Fakes.Tests/"
         "docs/CONTRACT.md" "docs/parts/")
    ;;
  infra/*)
    echo "infra/ branch: scope check skipped (owner only, reviewed by the team)."
    exit 0
    ;;
  *)
    echo "::error::Invalid branch name '$branch'."
    echo "Use part1/..., part2/..., part3/..., part4/..., contract/... or infra/... (see docs/GIT_WORKFLOW.md)."
    exit 1
    ;;
esac

violations=0
while IFS= read -r file; do
  [[ -z "$file" ]] && continue
  allowed=0
  for prefix in "${own[@]}" "${shared[@]}"; do
    if [[ "$file" == "$prefix"* ]]; then
      allowed=1
      break
    fi
  done
  if [[ $allowed -eq 0 ]]; then
    echo "::error file=$file::Out of scope for branch '$branch'"
    violations=$((violations + 1))
  fi
done < <(git diff --name-only "$base"...HEAD)

if [[ $violations -gt 0 ]]; then
  echo "$violations file(s) outside the scope of '$branch'."
  echo "Move that change to the right branch, or open a 'Contract change' issue if it is a contract problem."
  exit 1
fi

echo "Scope OK for '$branch'."
