# Mise en place de GitHub (une seule fois)

Le dépôt est **public** : la protection de `main` fonctionne sur un compte gratuit. Rien de confidentiel ne doit y entrer (la clé API n'y va jamais).

## Prérequis (pour les 4)

- Git, **.NET 10 SDK** (`dotnet --list-sdks` doit afficher une ligne `10.x`), VS Code avec l'extension **C# Dev Kit**, Claude Code.
- GitHub CLI (`gh`) pour le propriétaire.
- Une clé gratuite Alpha Vantage chacun : https://www.alphavantage.co/support/#api-key
- Sous Windows, les scripts `.sh` se lancent dans **Git Bash**.

## A. Propriétaire (Alexandre, @Alexarmanto)

Le dépôt existe déjà sur GitHub. Son nom est `QuantBacktest-Financial-Strategy-Backtesting-Platform`.

1. **Récupérer le dépôt et y copier le socle**
   ```bash
   git clone https://github.com/Alexarmanto/QuantBacktest-Financial-Strategy-Backtesting-Platform.git
   cd QuantBacktest-Financial-Strategy-Backtesting-Platform
   # copier ici TOUT le contenu du zip (y compris les fichiers cachés .github, .gitignore, .editorconfig...)
   ```
   Si GitHub a créé un `README.md`, un `.gitignore` ou une licence, remplace le `README.md` et le `.gitignore` par ceux du zip. Garde la licence si elle existe.
2. **Générer la solution et vérifier que tout compile** : `bash scripts/bootstrap.sh`. Doit finir par `Bootstrap OK`.
   > Le socle n'a pas pu être compilé avant la livraison : si le build échoue ici, envoie l'erreur complète avant d'aller plus loin. Elle se corrige dans une branche `contract/`, jamais en contournant.

   > 📸 **Capture `docs/report/images/part1-00-bootstrap.png`** : le terminal avec `Bootstrap OK` et le nombre de tests réussis.
3. **Premier commit sur `main`** (le seul push direct autorisé) :
   ```bash
   git add .
   git status            # vérifier : aucun bin/, obj/, .db, .env
   git commit -m "chore(repo): add project skeleton and team rules"
   git push origin main
   ```
4. **Inviter les 3 autres** : Settings → Collaborators → Add people (@VictorHaegeman, @d-elmus, @AlexisLS05, rôle *Write*).
5. **Vérifier la CI** : l'onglet Actions montre le workflow `CI` vert sur ce commit. La protection de `main` exige que les checks aient tourné au moins une fois.
   > 📸 **Capture `docs/report/images/part1-01-ci-green.png`** : l'onglet Actions avec le workflow vert.
6. **Appliquer les règles** : `gh auth login`, puis `bash scripts/setup-github.sh`.
   > 📸 **Capture `docs/report/images/part1-02-branch-protection.png`** : Settings → Branches, règle de protection de `main`.
7. **Tester les règles** (toutes doivent échouer) :
   - `git commit --allow-empty -m "test" && git push origin main` → refusé (puis `git reset --hard origin/main`).
   - Une PR intitulée `ma modif` → check `pr-rules` rouge.
   - Une branche `part1/test-scope` qui modifie `src/QuantBacktest.Engine/` → check `pr-rules` rouge.
   > 📸 **Capture `docs/report/images/part1-03-scope-check.png`** : la PR de test avec `pr-rules` en rouge et le message « Out of scope ». Fermer ensuite cette PR sans la merger.

## B. Chaque membre (propriétaire compris)

```bash
git clone https://github.com/Alexarmanto/QuantBacktest-Financial-Strategy-Backtesting-Platform.git
cd QuantBacktest-Financial-Strategy-Backtesting-Platform
dotnet build -warnaserror && dotnet test        # doit être vert avant de toucher à quoi que ce soit
dotnet user-secrets set "AlphaVantage:ApiKey" "<ta clé>" --project src/QuantBacktest.Web
dotnet run --project src/QuantBacktest.Web      # http://localhost:5080
```

Puis : ouvrir le dossier dans VS Code, lancer Claude Code **à la racine du dépôt**, créer sa première branche (`git switch -c part<N>/chore-start`) et coller le **prompt de démarrage** de sa notice (section 12 de `docs/parts/PART-<N>.md`).

> 📸 **Capture (par le premier qui merge une PR) `docs/report/images/part2-00-first-pr.png`** : la page de la PR avec les checks verts, l'approbation et le bouton « Squash and merge ». À envoyer à Victor pour la section « Quality and teamwork ».
