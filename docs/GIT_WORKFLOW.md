# Workflow Git — QuantBacktest

Règles **identiques pour les 4 membres**. La CI et la protection de `main` en appliquent une partie ; le reste est de la discipline.

## Principes

- Une seule branche longue : `main`. Elle compile et passe les tests **à tout moment**.
- Tout changement arrive par Pull Request avec une CI verte. La relecture par un autre membre est encouragée mais non bloquante (sauf PR `contract/`).
- Pas de calendrier imposé : on intègre dès qu'une brique fonctionne seule. Le **socle** d'une partie (section 5 de sa notice) est mergé avant ses fonctionnalités avancées (section 6).
- Chacun ne touche que son périmètre (notice, section 3). La CI le vérifie à partir du nom de branche.

## Branches

Format : `<préfixe>/<type>-<description-en-kebab-case>`

| Préfixe | Qui | Périmètre (vérifié par `scripts/check-scope.sh`) |
|---|---|---|
| `part1/` | Alexandre (@Alexarmanto) | Data, Reports, `data/seed/`, `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `.env.example` |
| `part2/` | Victor (@VictorHaegeman) | Strategies, Analytics, `docs/report/`, `docs/user-manual/` |
| `part3/` | Eloi (@d-elmus) | Engine |
| `part4/` | Alexis (@AlexisLS05) | Web |
| `contract/` | n'importe qui, après accord sur une issue « Contract change » | Core, Fakes et leurs tests, `docs/CONTRACT.md`, `docs/parts/` |
| `infra/` | propriétaire uniquement | CI, scripts, `CLAUDE.md`, ce fichier, racine |

Chaque partie possède aussi ses tests (`tests/QuantBacktest.<Projet>.Tests/`), sa section `docs/report/part-N.md` et ses images `docs/report/images/partN-*`. `Directory.Packages.props` est partagé : ajouts uniquement.

Types : `feat`, `fix`, `test`, `docs`, `refactor`, `perf`, `chore`, `ci`.
Exemples : `part3/feat-protective-exits`, `part1/fix-csv-culture`, `contract/fix-sweep-axis-rounding`.

## Commits et titres de PR

Format : `type(scope): summary`, en anglais, impératif, minuscule au début, 72 caractères maximum.

Scopes : `core`, `fakes`, `data`, `reports`, `docker`, `strategies`, `analytics`, `engine`, `web`, `docs`, `ci`, `repo`.
Exemples : `feat(engine): apply trailing stop on intrabar lows` · `fix(data): parse prices with invariant culture`.

Le **titre de la PR** devient le message du commit sur `main` (squash). La CI le vérifie.

## Cycle d'une tâche

```bash
git switch main && git pull
git switch -c part3/feat-protective-exits
# ... code + tests ...
dotnet build -warnaserror && dotnet test
bash scripts/check-scope.sh part3/feat-protective-exits origin/main
git add <fichiers du périmètre>
git commit -m "feat(engine): apply stop-loss and take-profit"
git fetch origin && git merge origin/main         # puis rebuild + retest
git push -u origin part3/feat-protective-exits
# ouvrir la PR sur GitHub, remplir le template
```

Dans VS Code, le panneau « Source Control » fait la même chose (stage, commit, sync) ; les règles restent identiques.

## Pull Request

1. Titre au bon format, template rempli, label de la partie, issue liée si elle existe.
2. CI verte (`build-and-test` et `pr-rules`) et branche à jour avec `main`.
3. Aucune approbation requise ; **3 approbations** (tous les autres) pour une PR `contract/`.
4. Toutes les conversations résolues.
5. L'**auteur** clique « Squash and merge » dès que la CI est verte (après les 3 approbations pour une PR `contract/`).
6. Relire les PR en attente des autres passe **avant** de commencer une nouvelle tâche.

### Ce que le relecteur vérifie

- Respect du contrat et de la notice (ids de règles cités dans le code ou les tests).
- Tests présents, pertinents, déterministes.
- Aucun secret, binaire ou fichier hors périmètre.
- Lisibilité : noms clairs, pas de code mort, pas de TODO sans issue.

## Synchronisation et conflits

- Avant de commencer et avant d'ouvrir la PR : `git fetch origin && git merge origin/main`.
- **Jamais de rebase ni de force-push** sur une branche déjà poussée.
- Conflit sur un fichier hors de ton périmètre : anormal. Ne le résous pas à la place de l'autre : préviens l'équipe.
- Conflit dans `Directory.Packages.props` : garde les deux lignes, rebuild.

## Modifier le contrat

1. Issue **Contract change** (template) : problème, proposition exacte, impact sur chaque partie.
2. Accord des 4 membres dans l'issue.
3. Une personne ouvre la PR `contract/...` qui met à jour **ensemble** `Core`, `Fakes`, `docs/CONTRACT.md` et les notices touchées ; les 3 autres approuvent.
4. Après le merge, chacun fusionne `main` dans sa branche avant de continuer.

Tant que l'issue n'est pas acceptée, on **ne contourne pas** le contrat.

## Interdits

- Pousser sur `main`, forcer un push, réécrire l'historique publié.
- Commiter une clé API, `.env`, `*.db`, `bin/`, `obj/`, un PDF généré.
- Merger une PR rouge, ou une PR `contract/` sans les 3 approbations.
- Désactiver la CI, un test ou un warning pour « faire passer ».

## Fin de projet

Quand tous les livrables sont mergés, le propriétaire pose le tag `v1.0.0` sur `main` : c'est la version rendue.
