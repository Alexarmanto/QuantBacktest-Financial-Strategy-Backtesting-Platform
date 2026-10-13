#!/usr/bin/env bash
# Applies the repository rules described in docs/GIT_WORKFLOW.md. Run ONCE by the repository owner,
# after the first push of main. Requires the GitHub CLI, logged in as the owner (gh auth login).
# Usage: bash scripts/setup-github.sh [owner/repo]     (default: Alexarmanto/QuantBacktest-Financial-Strategy-Backtesting-Platform)
set -euo pipefail

repo="${1:-Alexarmanto/QuantBacktest-Financial-Strategy-Backtesting-Platform}"

command -v gh >/dev/null || { echo "GitHub CLI not found: https://cli.github.com"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Run 'gh auth login' first."; exit 1; }

echo "==> Merge settings: squash only, PR title as commit title, delete branch after merge"
gh repo edit "$repo" \
  --enable-squash-merge \
  --enable-merge-commit=false \
  --enable-rebase-merge=false \
  --delete-branch-on-merge \
  --enable-issues \
  --enable-wiki=false
gh api -X PATCH "repos/$repo" \
  -f squash_merge_commit_title=PR_TITLE \
  -f squash_merge_commit_message=BLANK >/dev/null

echo "==> Labels"
label() { gh label create "$1" --repo "$repo" --color "$2" --description "$3" --force >/dev/null; }
label part-1 1D76DB "Data, storage, reports, Docker"
label part-2 0E8A16 "Strategies, custom rules, Monte Carlo, report and manual"
label part-3 5319E7 "Engine, metrics, heatmap"
label part-4 FBCA04 "Web UI and demo video"
label contract-change D93F0B "Change to Core, Fakes or CONTRACT.md: all members must approve"
label infra BFD4F2 "Repository rules, CI, scripts (owner only)"

echo "==> Branch protection on main"
if gh api -X PUT "repos/$repo/branches/main/protection" --input - >/dev/null <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["build-and-test", "pr-rules"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1,
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false
  },
  "restrictions": null,
  "required_linear_history": true,
  "required_conversation_resolution": true,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
then
  echo "Branch protection enabled on main."
else
  echo
  echo "WARNING: GitHub refused to enable branch protection."
  echo "Check that the repository is public (or that the owner has GitHub Pro) and that main exists."
  exit 2
fi

echo
echo "Done. Verify with: gh api repos/$repo/branches/main/protection"
