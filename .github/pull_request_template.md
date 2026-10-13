## What and why
<!-- One or two sentences. Link the issue: "Closes #12". Cite the requirement ids (e.g. P3-E4, X8). -->

## Part and scope
- [ ] Part 1 (data, reports, Docker)
- [ ] Part 2 (strategies, custom rules, Monte Carlo, report, manual)
- [ ] Part 3 (engine, metrics, heatmap)
- [ ] Part 4 (web, video)
- [ ] `contract/` change (label `contract-change`, approved by the 3 other members)
- [ ] `infra/` change (owner only)

## Checklist (all boxes ticked before requesting review)
- [ ] The PR title follows `type(scope): summary` (it becomes the squash commit message)
- [ ] `dotnet build -warnaserror` and `dotnet test` pass locally on the whole solution
- [ ] Only files inside my part's scope are modified (CI checks this)
- [ ] New behavior is covered by xUnit tests
- [ ] No secret, database file, `bin/`, `obj/` or generated report is committed
- [ ] I merged the latest `main` into my branch
- [ ] Screenshots marked 📸 for this PR in my notice are taken and saved

## How to verify
<!-- Commands to run or pages to open. For UI changes, attach a screenshot. -->

## Notes for the reviewer
<!-- Trade-offs, doubts, follow-up work. -->
