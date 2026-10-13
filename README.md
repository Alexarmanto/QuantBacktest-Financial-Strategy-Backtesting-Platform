# QuantBacktest — Financial Strategy Backtesting Platform

Application web C# (.NET 10, ASP.NET Core MVC) pour tester des stratégies de trading sur des données historiques Alpha Vantage, mesurer leur performance et produire des rapports. Projet EFREI ST2OOS 2026/2027 — rendu le **14/11/2026**.

## Équipe

| Partie | Responsable | GitHub | Périmètre | Notice |
|---|---|---|---|---|
| 1 | Alexandre Tung (propriétaire) | @Alexarmanto | Données (API, cache SQLite, seed), qualité des données, quota API, stockage des stratégies sans code, rapports PDF/CSV, Docker | [PART-1](docs/parts/PART-1.md) |
| 2 | Victor Haegeman | @VictorHaegeman | Indicateurs, 10 stratégies, gabarit C#, moteur de règles sans code, Monte Carlo, rapport final, manuel | [PART-2](docs/parts/PART-2.md) |
| 3 | Eloi Dumas | @d-elmus | Moteur de backtest, ordres avancés, métriques, heatmap d'optimisation | [PART-3](docs/parts/PART-3.md) |
| 4 | Alexis Launay-Schapman | @AlexisLS05 | Application web, comparateur, train/test, liens permanents, vidéo de démo | [PART-4](docs/parts/PART-4.md) |

## Fonctionnalités

**Exigées par l'énoncé** : récupération Alpha Vantage avec cache local ; au moins 6 stratégies et des stratégies personnalisées ; moteur de backtest daily et weekly ; rendement cumulé, rendement moyen, volatilité, drawdown maximal, Sharpe ; interface web de configuration avec graphiques et tableaux ; rapports avec graphiques et journal des trades.

**Nos 10 fonctionnalités de différenciation** :
1. Ordres avancés : stop-loss, take-profit, trailing stop, slippage
2. Heatmap d'optimisation de deux paramètres, avec avertissement d'overfitting
3. Validation train / test (in-sample / out-of-sample)
4. Comparateur multi-stratégies
5. Métriques avancées : Sortino, Calmar, profit factor, exposition
6. Indicateurs tracés sur les graphiques et fiche explicative par stratégie
7. Simulation Monte Carlo sur les trades
8. Panneau de qualité des données et compteur de quota API
9. Lien permanent reproductible pour chaque résultat
10. Docker et `docker compose up`

En plus : un **constructeur de stratégies sans code** (règles d'entrée et de sortie définies dans l'interface) et un gabarit C# pour les stratégies codées.

**Stratégies intégrées** : moving average crossover, Bollinger mean reversion, RSI mean reversion, momentum (ROC), MACD crossover, Donchian breakout, Bollinger + RSI double confirmation, trend-filtered RSI-2 (Connors), consecutive down days, turn-of-the-month, et buy & hold comme référence.

## Démarrage rapide

Prérequis : Git, .NET 10 SDK, VS Code + C# Dev Kit, une clé gratuite Alpha Vantage.

```bash
git clone https://github.com/Alexarmanto/QuantBacktest-Financial-Strategy-Backtesting-Platform.git
cd QuantBacktest-Financial-Strategy-Backtesting-Platform
dotnet build -warnaserror && dotnet test
dotnet user-secrets set "AlphaVantage:ApiKey" "<ta clé>" --project src/QuantBacktest.Web
dotnet run --project src/QuantBacktest.Web        # http://localhost:5080
```

Mise en place initiale de GitHub (propriétaire) : [docs/GITHUB_SETUP.md](docs/GITHUB_SETUP.md).

## Où lire quoi

| Document | Contenu |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Règles lues automatiquement par Claude Code (identiques pour tous) |
| [docs/CONTRACT.md](docs/CONTRACT.md) | Contrat v1.0 : interfaces, règles d'exécution, formules, données, règles sans code |
| [docs/GIT_WORKFLOW.md](docs/GIT_WORKFLOW.md) | Branches, commits, PR, reviews, conflits |
| [docs/GITHUB_SETUP.md](docs/GITHUB_SETUP.md) | Mise en place du dépôt (une fois) |
| [docs/parts/](docs/parts/) | Notice détaillée de chaque partie |
| [docs/report/](docs/report/) | Plan et sections du rapport final |

## Arborescence

```
QuantBacktest/
├── CLAUDE.md                       règles pour Claude Code
├── Directory.Build.props           .NET 10, nullable, warnings en erreurs
├── Directory.Packages.props        toutes les versions NuGet
├── global.json                     version du SDK
├── QuantBacktest.sln               (créé par scripts/bootstrap.sh)
├── Dockerfile, docker-compose.yml  (Partie 1)
├── .github/                        CI, templates PR et issues, CODEOWNERS
├── docs/                           contrat, workflow, notices, rapport, manuel
├── data/seed/                      CSV de départ (longues historiques)
├── scripts/                        bootstrap, setup-github, contrôles CI
├── src/
│   ├── QuantBacktest.Core/         contrat : modèles et interfaces (gelé)
│   ├── QuantBacktest.Fakes/        implémentations factices pour travailler en parallèle
│   ├── QuantBacktest.Data/         Partie 1
│   ├── QuantBacktest.Reports/      Partie 1
│   ├── QuantBacktest.Strategies/   Partie 2
│   ├── QuantBacktest.Analytics/    Partie 2
│   ├── QuantBacktest.Engine/       Partie 3
│   └── QuantBacktest.Web/          Partie 4
└── tests/                          un projet de tests par projet de src
```

## Livrables

1. Code fonctionnel (backend et frontend) : tag `v1.0.0` sur `main`.
2. Rapport (`docs/report/QuantBacktest-Report.pdf`) avec le manuel utilisateur en annexe.
3. Vidéo de démonstration de 5 minutes : architecture et démonstration de l'application.
