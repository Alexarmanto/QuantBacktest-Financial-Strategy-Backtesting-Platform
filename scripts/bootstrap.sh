#!/usr/bin/env bash
# One-time setup, run by the repository owner from the repository root BEFORE the first commit.
# Creates QuantBacktest.sln with every project, then builds and tests the whole solution.
# Usage: bash scripts/bootstrap.sh
set -euo pipefail

command -v dotnet >/dev/null || { echo "The .NET 10 SDK is required (dotnet not found)."; exit 1; }
dotnet --list-sdks | grep -q '^10\.' || { echo "The .NET 10 SDK is required. Installed SDKs:"; dotnet --list-sdks; exit 1; }

if [[ -f QuantBacktest.sln ]]; then
  echo "QuantBacktest.sln already exists: nothing to create."
else
  # .NET 10 creates .slnx by default; the team uses the classic .sln format.
  dotnet new sln -n QuantBacktest --format sln
  # Portable loop (macOS ships bash 3.2, which has no mapfile).
  find src tests -name '*.csproj' | sort | while IFS= read -r project; do
    dotnet sln QuantBacktest.sln add "$project"
  done
fi

dotnet restore
dotnet build --no-restore -warnaserror
dotnet test --no-build

echo
echo "Bootstrap OK. Commit QuantBacktest.sln together with everything else."
