# QuantBacktest — règles pour Claude Code

Ce fichier est lu automatiquement à chaque session. Il est **identique pour les 4 membres** et ne se modifie que par une branche `infra/` (propriétaire du dépôt).

## Le projet

**QuantBacktest — Financial Strategy Backtesting Platform.** Application web C# (.NET 10, ASP.NET Core MVC) : cours historiques via l'API Alpha Vantage (plan gratuit) et des fichiers de départ, cache SQLite, 10 stratégies de trading et un constructeur de stratégies sans code, moteur de backtest avec stop-loss, take-profit, trailing stop et slippage, métriques (Sharpe, Sortino, Calmar...), heatmap d'optimisation, train/test, comparateur, Monte Carlo, qualité des données, rapports PDF, Docker. Projet EFREI ST2OOS 2026/2027, rendu le 14/11/2026. Équipe de 4, une partie chacun.

| Partie | Responsable | Branche |
|---|---|---|
| 1 Données, rapports, Docker | Alexandre Tung (@Alexarmanto, propriétaire) | `part1/` |
| 2 Stratégies, règles sans code, Monte Carlo, rapport, manuel | Victor Haegeman (@VictorHaegeman) | `part2/` |
| 3 Moteur, métriques, heatmap | Eloi Dumas (@d-elmus) | `part3/` |
| 4 Application web, vidéo | Alexis Launay-Schapman (@AlexisLS05) | `part4/` |

## Avant toute action (obligatoire)

1. Lis `docs/CONTRACT.md` (types, interfaces, règles numérotées) et `docs/GIT_WORKFLOW.md`.
2. Identifie la partie avec `git branch --show-current` (préfixe `part1/` à `part4/`, `contract/` ou `infra/`). Sur `main` ou sans préfixe valide : **arrête-toi**, demande à l'humain sa partie et sa tâche, puis crée la branche.
3. Lis `docs/parts/PART-<N>.md` en entier. C'est la spécification ; elle prime sur toute supposition.
4. Résume ta compréhension en 10 lignes maximum et **attends la validation de l'humain** avant d'écrire du code.

## Règles absolues

1. **Périmètre** : modifie uniquement les fichiers de la section « Fichiers que tu possèdes » de la notice. La CI refuse la PR sinon.
2. **Contrat gelé** : jamais de modification de `src/QuantBacktest.Core`, `src/QuantBacktest.Fakes`, `tests/QuantBacktest.Core.Tests`, `tests/QuantBacktest.Fakes.Tests`, `docs/CONTRACT.md` ou `docs/parts/` hors d'une branche `contract/`. Si le contrat bloque : arrête-toi, explique le problème et propose le texte d'une issue « Contract change ».
3. **N'invente rien** : une ambiguïté se règle en demandant à l'humain. La notice et le contrat sont la seule source de vérité ; les `Fakes` ne sont **pas** une référence de comportement.
4. **Git** : jamais de push sur `main`, jamais de `--force`, jamais de réécriture d'historique publié, jamais de merge de sa propre PR.
5. **Secrets** : la clé Alpha Vantage ne va jamais dans le code, `appsettings*.json`, un test, un log, un commit ou un `docker-compose.yml`. Elle vit dans les user-secrets (`AlphaVantage:ApiKey`) ou la variable d'environnement `AlphaVantage__ApiKey` (fichier `.env` local, ignoré par Git).
6. **Ne commite jamais** : `bin/`, `obj/`, `*.db`, `App_Data/`, PDF générés, `.env`.
7. **Paquets NuGet** : uniquement ceux autorisés par la notice, ajoutés avec `dotnet add <projet> package <Nom>` (versions dans `Directory.Packages.props`, jamais dans un `.csproj`).
8. **Avant chaque commit** : `dotnet build -warnaserror` puis `dotnet test` verts sur **toute** la solution.
9. **Tests** : tout comportement public a des tests xUnit + FluentAssertions déterministes (pas de réseau, pas d'horloge réelle, aléa uniquement avec graine fixe). Chaque test cite l'identifiant de règle qu'il vérifie dans son nom ou un commentaire (`// X8`).
10. **Captures d'écran** : à chaque étape marquée 📸 dans la notice, **signale à l'humain** qu'il doit faire la capture, avec le nom de fichier attendu, avant de continuer.

## Conventions de code (identiques pour tous)

- Langue : code, noms, commentaires, commits, PR, textes de l'interface et du PDF, rapport, manuel, vidéo en **anglais**. Documents internes (`CLAUDE.md`, `docs/CONTRACT.md`, `docs/GIT_WORKFLOW.md`, `docs/GITHUB_SETUP.md`, `docs/parts/`) en français.
- C# récent, nullable activé, `namespace` en portée de fichier, une classe publique par fichier (hors petits records liés).
- `decimal` pour prix, quantités, montants ; `double` pour rendements, ratios, statistiques. Fractions dans le code (0.12 = 12 %), pourcentages seulement à l'affichage.
- **`CultureInfo.InvariantCulture`** pour parser et formater nombres et dates hors affichage. Jamais `double.Parse(texte)` sans culture : sur un poste en français, `"123.45"` est mal lu (le guide du cours fait cette erreur).
- `DateOnly` pour les dates de marché ; pas de `DateTime.Now` : injecter `TimeProvider`.
- Asynchrone : suffixe `Async`, `CancellationToken` en dernier paramètre.
- Tout ce qui n'est pas dans le contrat est `internal` (les tests y accèdent par `InternalsVisibleTo`, déjà configuré).
- Pas de `Console.WriteLine` dans les bibliothèques : `ILogger<T>`. Jamais une URL contenant la clé dans un log.
- Pas d'état statique mutable ; injection par constructeur ; aucune exception avalée silencieusement.

## Commandes

```bash
dotnet build -warnaserror                          # compilation stricte (celle de la CI)
dotnet test                                        # tous les tests
dotnet test tests/QuantBacktest.Engine.Tests       # tests d'un seul projet
dotnet run --project src/QuantBacktest.Web         # http://localhost:5080 (fakes activés en Development)
dotnet user-secrets set "AlphaVantage:ApiKey" "<clé>" --project src/QuantBacktest.Web
bash scripts/check-scope.sh "$(git branch --show-current)" origin/main   # vérifier son périmètre avant la PR
```

## Boucle de travail

1. `git switch main && git pull`, puis `git switch -c part<N>/<type>-<description>`.
2. Propose le plan de la PR (fichiers, tests, ids de règles couverts) et attends l'accord de l'humain.
3. Implémente dans le périmètre, tests en même temps que le code.
4. `dotnet build -warnaserror` et `dotnet test` verts ; `scripts/check-scope.sh` vert.
5. Commit `type(scope): summary` en anglais ; n'ajoute que des fichiers du périmètre (jamais `git add -A` à l'aveugle).
6. `git fetch origin && git merge origin/main`, rebuild, retest, push, PR avec le template rempli.
7. Rappelle à l'humain les captures 📸 de cette PR.

## Fin de tâche

Dis en quelques lignes ce qui a changé, comment le vérifier, ce qui reste ouvert. N'annonce « terminé » que si build et tests passent réellement.
