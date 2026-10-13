# PARTIE 4 — Application web et vidéo de démonstration

> Cette notice est ta spécification. Elle prime sur toute supposition. En cas de doute : demande à l'humain, ne devine pas.
> Contrat commun : `docs/CONTRACT.md` (règles citées par leur identifiant, par exemple T1, C1, O4). Règles Git : `docs/GIT_WORKFLOW.md`. Règles de code : `CLAUDE.md`.

## 1. Identité

| | |
|---|---|
| Responsable | Alexis Launay-Schapman (@AlexisLS05) |
| Projets | `src/QuantBacktest.Web` et `tests/QuantBacktest.Web.Tests` |
| Préfixe de branche | `part4/` |
| Label GitHub | `part-4` |
| Scopes de commit | `web`, `docs` |

## 2. Mission

Construire l'application ASP.NET Core MVC : configuration et résultats d'un backtest avec liens permanents, graphiques et indicateurs, catalogue de stratégies et constructeur sans code, comparateur, heatmap d'optimisation, validation train/test, Monte Carlo, données et quota, téléchargements. Tu es la **racine de composition** : tu branches les services de tous, mais tu ne contiens **aucune logique financière**. Tu coordonnes aussi la **vidéo de démonstration**.

## 3. Périmètre

### Fichiers que tu possèdes

- `src/QuantBacktest.Web/**` (dont `Program.cs`, `appsettings*.json`, `wwwroot/`), `tests/QuantBacktest.Web.Tests/**`
- `docs/report/part-4.md`, `docs/report/images/part4-*`
- `Directory.Packages.props` : ajout uniquement (normalement rien)

### Interdit de modifier

Tout le reste : `Core`, `Fakes`, `docs/CONTRACT.md`, `docs/parts/`, les projets des autres parties, `.github/`, `scripts/`, `CLAUDE.md`, les fichiers Docker (Partie 1).

### Frontières avec les autres parties

- **Aucune logique financière dans le web.** Tu n'additionnes, ne moyennes, ne ré-annualises, ne recalcules rien. Seuls calculs tolérés : présentation (fraction → pourcentage, tri, couleurs), extraction des marqueurs d'achat et de vente depuis `Trades`, découpage train/test selon T1 et T2, conversion des unités d'URL (pour cent → fraction).
- **Aucun accès direct** à la base, à l'API Alpha Vantage, aux fichiers de départ : tout passe par les interfaces de `Core`.
- **Pas de PDF ni de CSV fabriqués ici** : `IReportGenerator` (Partie 1) ; tu sers les octets avec les noms de CONTRACT section 11.
- **Rien de codé en dur sur les stratégies** : formulaires, listes, fiches et heatmap se construisent depuis `IStrategyCatalog` (`Parameters`, `SupportedTimeframes`, `Documentation`). Une stratégie copiée depuis le gabarit ou créée sans code apparaît toute seule.
- **Validation des stratégies sans code** : tu appelles `ICustomStrategyFactory.Validate` (Partie 2) avant `ICustomStrategyStore.SaveAsync` (Partie 1). Tu ne réécris aucune règle R1 à R8.
- **Classes `internal` des autres parties** : jamais. Uniquement `Core` et les méthodes `AddQuantBacktest*` / `AddFake*`.

## 4. Contrat

### Ce que tu consommes (injection de dépendances)

| Interface | Usage |
|---|---|
| `IBacktestService` | Résultats, comparateur, train/test |
| `IParameterSweepService` | Heatmap, train/test |
| `IStrategyCatalog` | Listes, formulaires, fiches, `/api/strategies` |
| `ICustomStrategyFactory`, `ICustomStrategyStore` | Constructeur sans code |
| `IMonteCarloService` | Page Monte Carlo, PDF enrichi |
| `IMarketDataService` | Page Données, qualité, rafraîchissement, quota, découpage train/test |
| `IReportGenerator` | PDF et CSV |

### Branchement (`Program.cs`, déjà écrit)

Une clé par partie : `QuantBacktest:UseFakes:Data`, `:Reports`, `:Strategies`, `:Analytics`, `:Engine`. Toutes à `true` dans `appsettings.Development.json` au départ ; tu les passes à `false` **une par une** (P4-I1).

## 5. Spécification du socle

### Liens permanents : paramètres d'URL (figés)

Tous les résultats s'obtiennent par une requête **GET** dont l'URL contient l'intégralité des réglages : copier l'URL redonne le même résultat tant que les données en cache ne sont pas rafraîchies (D2).

| Paramètre | Sens | Format, défaut |
|---|---|---|
| `symbol` | symbole | obligatoire, normalisé en majuscules |
| `tf` | timeframe | `daily` (défaut) ou `weekly` |
| `strategy` | id de stratégie | obligatoire |
| `from`, `to` | période | `yyyy-MM-dd`, optionnels |
| `capital` | capital initial | décimal, défaut 10000 |
| `fee`, `slip` | frais, slippage | **en pour cent**, défauts 0.1 et 0 |
| `sl`, `tp`, `ts` | stop-loss, take-profit, trailing stop | **en pour cent**, absents = désactivés |
| `rf` | taux sans risque annuel | **en pour cent**, défaut 0 |
| `p.<clé>` | paramètre de stratégie | décimal, absent = défaut (par exemple `p.fast=20`) |

Nombres avec point décimal (culture invariante). Conversion pour cent → fraction côté serveur uniquement : `fraction = valeur / 100`.

### Routes (figées)

| Route | Contenu |
|---|---|
| `GET /` | Redirection vers `/Backtest` |
| `GET /Backtest` | Formulaire, pré-rempli depuis les paramètres d'URL présents |
| `GET /Backtest/Result?…` | Lance le backtest et affiche le résultat (lien permanent) |
| `GET /Backtest/Report.pdf?…` | PDF (`&mc=true` ajoute le Monte Carlo, avec `sims` et `seed`) |
| `GET /Backtest/Trades.csv?…` | CSV des trades |
| `GET /Strategies` | Catalogue : une fiche par stratégie |
| `GET /Strategies/Custom/New`, `GET /Strategies/Custom/{id}/Edit` | Constructeur sans code |
| `POST /Strategies/Custom/Save`, `POST /Strategies/Custom/{id}/Delete` | Enregistrement, suppression |
| `GET /Data` | Symboles en cache, résumé qualité, quota, formulaire de rafraîchissement |
| `GET /Data/Quality?symbol=&tf=` | Rapport de qualité détaillé |
| `POST /Data/Refresh` | Rafraîchit (`forceRefresh: true`) puis redirige vers la page qualité |
| `GET /api/strategies` | JSON pour le formulaire dynamique |
| `GET /Home/Error` | Page d'erreur générique |

Fonctionnalités avancées (section 6) : `GET /Compare`, `GET /Compare/Result?…`, `GET /Optimize`, `GET /Optimize/Result?…`, `GET /Validate`, `GET /Validate/Result?…`, `GET /Backtest/MonteCarlo?…`.

Forme de `/api/strategies` (camelCase) : `[{ "id", "name", "description", "category", "supportedTimeframes": ["daily", "weekly"], "parameters": [{ "key", "label", "type": "integer" | "decimal", "default", "min", "max", "step", "description" }], "documentation": { "idea", "entryRule", "exitRule", "risks" } }]`.

### Exigences

| Id | Exigence |
|---|---|
| P4-U1 | **Formulaire de backtest.** Symbole (texte + `datalist` des symboles en cache, validé par `Conventions.SymbolPattern`), timeframe, période optionnelle (début <= fin), stratégie (liste groupée : intégrées puis personnalisées), paramètres générés depuis `Parameters` (`min`, `max`, `step`, défaut, description en infobulle), capital (> 0), frais, slippage, stop-loss, take-profit, trailing stop, taux sans risque, chacun avec sa plage du contrat. Le formulaire est en **GET** vers `/Backtest/Result`. Validation côté client **et** côté serveur ; saisie conservée en cas d'erreur. Une stratégie qui ne supporte pas le timeframe choisi est désactivée dans la liste, avec la raison. |
| P4-U2 | **Pendant le calcul** : bouton désactivé et indicateur de chargement ; pas de double envoi. |
| P4-U3 | **Résultat : en-tête.** Symbole, stratégie, période réelle (première et dernière date de `Bars`), timeframe, réglages d'exécution ; bandeau si `PricesAdjusted` est faux (« Prices are not adjusted for splits or dividends: long periods may be distorted. ») ; bouton « Copy link » (copie l'URL courante) ; fiche de la stratégie (`Documentation`) repliable. |
| P4-U4 | **Résultat : métriques.** Tableau **Strategy vs Buy & hold** des 13 champs de `PerformanceMetrics` ; `null` affiché « n/a » ; `AverageReturnPerPeriod` intitulé « Average daily return » ou « Average weekly return » ; couleurs vert/rouge sur les rendements. |
| P4-U5 | **Résultat : graphiques (Chart.js).** (1) équité stratégie vs benchmark, échelle linéaire ou logarithmique ; (2) drawdowns stratégie et benchmark (aires, en %) ; (3) cours de clôture + indicateurs `Pane = Price` + triangles d'achat (vert) et de vente (rouge) aux dates et prix des trades ; (4) si des indicateurs `Pane = Oscillator` existent, un graphique dessous avec leurs `ReferenceLines`. Axe des dates en catégories. Les données sont injectées dans la page par un `<script type="application/json">`. |
| P4-U6 | **Résultat : trades.** Toutes les colonnes de `Trade`, raison de sortie en badge, tri par en-tête côté client, lignes gain/perte colorées ; zéro trade : « No trades were executed in this period. » |
| P4-U7 | **Résultat : actions.** « Download PDF », « Download trades CSV », « Run Monte Carlo », « Optimize this strategy » (si au moins 2 paramètres), « Edit settings » (retour au formulaire pré-rempli). |
| P4-U8 | **Catalogue.** Une fiche par stratégie : nom, catégorie, description, `Documentation` (Idea, Entry rule, Exit rule, Risks), timeframes, tableau des paramètres, bouton « Use this strategy » ; pour les personnalisées : « Edit » et « Delete » (confirmation). |
| P4-U9 | **Constructeur sans code.** Nom, description ; règle d'entrée et règle de sortie, chacune avec son combinateur (`All` = « all conditions », `Any` = « any condition ») et 1 à 5 lignes ajoutables et supprimables. Une ligne = opérande gauche, opérateur (`>`, `<`, `crosses above`, `crosses below`), opérande droit ; un opérande = type (`OperandKind`) + champs affichés selon le type (`Period`, `Value`, `StdDevs`, R3). À l'enregistrement : `Id = Conventions.CreateCustomStrategyId(Name)` ; création d'un nom dont l'id existe déjà → erreur « A strategy with this name already exists. » ; puis `ICustomStrategyFactory.Validate` ; erreurs affichées, saisie conservée ; sinon `SaveAsync` et redirection vers `/Strategies` avec un message. En édition, le nom est en lecture seule (l'id ne change jamais). |
| P4-U10 | **Données.** Tableau des symboles en cache × timeframes : nombre de barres, première et dernière date, origine, ajusté ou non, nombre d'anomalies (via `GetQualityReportAsync` ; `NotCached` → « not cached »). Formulaire de rafraîchissement (symbole, timeframe) avec confirmation JavaScript « This uses one of your daily API calls (N left today). Continue? ». Page qualité : tous les champs de `DataQualityReport` et le tableau des anomalies. |
| P4-U11 | **Quota.** Badge dans la barre de navigation « API 17/25 today » (`GetApiQuotaAsync`, via un view component) ; masqué si l'appel échoue. |
| P4-U12 | **Erreurs.** `MarketDataException` et `BacktestValidationException` : alerte en haut du formulaire, saisie conservée ; `RateLimitExceeded` : ajouter « Try a symbol that is already cached, or retry tomorrow. » ; `MissingApiKey` : ajouter la commande `dotnet user-secrets set …`. Toute autre exception : `/Home/Error`, détail journalisé, jamais de pile d'appels affichée hors Development. |
| P4-U13 | **Layout.** Navigation : Backtest, Compare, Optimize, Validate, Strategies, Data ; pied de page « Market data: Alpha Vantage » ; bandeau jaune « Demo data: fake services active (<liste des parties>) » sur toutes les pages tant qu'une clé `UseFakes:*` est vraie. Lisible de 360 à 1 920 px. |
| P4-U14 | **Culture et formats.** `UseRequestLocalization` avec `en-US` uniquement ; montants `$10,371.39` ; pourcentages et ratios à 2 décimales. |
| P4-U15 | **Ressources locales.** Bootstrap 5.3, Chart.js 4 et `chartjs-chart-matrix` copiés dans `wwwroot/lib/` (pas de CDN : démo hors ligne). Versions exactes notées dans `part-4.md`. |
| P4-U16 | **Organisation du code.** Contrôleurs fins ; un modèle de requête `BacktestQuery` (parse et construit l'URL, seul endroit qui connaît les paramètres d'URL) ; modèles de vue qui portent le formatage ; JavaScript en modules dans `wwwroot/js/` (`backtest-form.js`, `charts.js`, `table-sort.js`, `rule-builder.js`, `heatmap.js`) ; aucun script en ligne hors données JSON ; jetons antiforgery sur tous les POST. |

## 6. Spécification des fonctionnalités avancées

À commencer **après** le merge du socle (section 9, PR 1 à 6). Tout se développe d'abord avec les `Fakes`.

| Id | Fonctionnalité | Exigence |
|---|---|---|
| P4-X1 | Comparateur (n°4, C1) | Formulaire : symbole, timeframe, période, réglages d'exécution, 2 à 5 stratégies cochées (paramètres par défaut). URL : mêmes paramètres + `strategies=id1,id2,...`. Résultat : un graphique avec une courbe d'équité par stratégie et le benchmark en pointillés ; tableau des métriques principales (rendement cumulé, annualisé, volatilité, Sharpe, Sortino, Calmar, drawdown, trades, profit factor, exposition), triable ; une stratégie en erreur affiche son message sans bloquer les autres ; lien vers le résultat détaillé de chacune. |
| P4-X2 | Heatmap (n°2, O1 à O5) | Formulaire : réglages du backtest + stratégie à au moins 2 paramètres + paramètres X et Y (distincts) + `min`, `max`, `step` de chacun (défaut : bornes de la définition et `step = Step × ceil((Max - Min) / (24 × Step))`, soit au plus 25 valeurs) + métrique. URL : réglages du backtest + `x`, `xmin`, `xmax`, `xstep`, `y`, `ymin`, `ymax`, `ystep`, `metric`. Affichage `chartjs-chart-matrix` : couleur divergente, cellule `null` grise avec `Error` en infobulle, cellule avec `TradeCount < 5` atténuée (infobulle « fewer than 5 trades »), meilleure cellule entourée, clic sur une cellule → résultat détaillé avec ces paramètres. Note permanente : « The best cell is optimistic by construction: validate it on unseen data (Validate page). » ; bandeau rouge si `IsIsolatedPeak` : « Isolated peak: the best result is much better than its neighbours, a classic sign of overfitting. » |
| P4-X3 | Train / test (n°3, T1 à T3) | Formulaire : heatmap + part d'entraînement (`train`, en pour cent, 50 à 90, défaut 70). Calcul : série via `GetSeriesAsync` (sans rafraîchissement) → barres de la période → découpage T1/T2 → heatmap sur le train → meilleure cellule → backtest sur le test. Affichage : dates de coupure, meilleurs paramètres, tableau des métriques **train vs test** côte à côte, les deux courbes d'équité, la mini-heatmap du train, le texte explicatif T2 (warm-up). Aucune meilleure cellule → message clair. |
| P4-X4 | Monte Carlo (n°7, MC1 à MC7) | `GET /Backtest/MonteCarlo?…&sims=1000&seed=42` : relance le backtest, appelle `IMonteCarloService`, affiche les deux histogrammes, le tableau des percentiles, la probabilité de perte, les valeurs réelles en repère sur les histogrammes, l'avertissement MC7, et le lien PDF avec `mc=true`. Moins de 5 trades : message de la `BacktestValidationException`. |
| P4-X5 | Qualité et quota (n°8) | P4-U10 et P4-U11 complets avec les vrais services. |
| P4-X6 | Indicateurs et fiches (n°6) | P4-U5 (3) et (4) et P4-U8 vérifiés sur les 11 stratégies réelles et une stratégie sans code. |

### Intégration et vidéo

| Id | Exigence |
|---|---|
| P4-I1 | **Bascule vers les vrais services, partie par partie** : `UseFakes:Data` après la PR 5 de la Partie 1, `:Reports` après sa PR 7, `:Strategies` après la PR 6 de la Partie 2, `:Analytics` après sa PR 8, `:Engine` après la PR 3 de la Partie 3. Les bascules sont indépendantes : n'importe quel mélange de vrais services et de fakes fonctionne, puisque tout passe par les interfaces. Vérifie dans le code de `main` que la méthode `AddQuantBacktest*` enregistre réellement les services avant de basculer. Une PR par bascule, avec un backtest réel complet testé à la main. 📸 |
| P4-I2 | **Pas de contournement** : si le contrat empêche d'afficher quelque chose, issue « Contract change », jamais un calcul de ton côté. |
| P4-V1 | **Vidéo de 5 minutes** (architecture + démonstration, exigée par l'énoncé). Déroulé : 0:00–0:30 problème et objectifs (Victor) ; 0:30–1:30 architecture : couches, contrat, fakes, CI (Eloi) ; 1:30–2:15 données, cache, qualité, quota, Docker (Alexandre) ; 2:15–4:30 démonstration en direct (toi) : backtest et résultats, constructeur sans code, comparateur, heatmap puis train/test, Monte Carlo, PDF ; 4:30–5:00 limites et conclusion (Victor). Chacun enregistre sa voix sur sa séquence ; tu montes l'ensemble. Le fichier vidéo n'est **pas** commité : lien dans le README par une PR `infra/` du propriétaire. |

## 7. Tests obligatoires

Tests de contrôleurs et de modèles avec les **Fakes**, sans réseau.

- **`BacktestQuery`** : aller-retour URL → modèle → URL identique ; défauts appliqués ; conversions pour cent → fraction (`fee=0.1` → `0.001m`, `sl=5` → `0.05m`) ; `p.fast=20` → paramètre `fast` ; paramètres d'URL invalides → erreurs de validation, pas d'exception.
- **Formulaire** : stratégies du catalogue et leurs paramètres présents ; pré-remplissage depuis l'URL ; stratégie quotidienne seulement désactivée en weekly (`fake-alternating`).
- **Résultat** : `GET /Backtest/Result` valide → 200 avec les métriques ; invalide (symbole mal formé, début > fin, capital <= 0, frais hors plage, paramètre hors plage) → formulaire réaffiché avec l'erreur et la saisie.
- **Erreurs métier** : `UNKNOWN`, `NOKEY`, `LIMIT` (Fakes) → alertes avec les compléments de P4-U12, pas de page d'erreur.
- **Téléchargements** : PDF `application/pdf`, CSV `text/csv`, noms de CONTRACT section 11.
- **Modèles de vue** : `null` → « n/a » ; formats ; libellé « Average weekly return » en weekly ; un marqueur d'achat et un de vente par trade aux bons prix ; message « No trades… » ; bandeau prix non ajustés.
- **`/api/strategies`** : forme JSON exacte ; une stratégie ajoutée au catalogue factice apparaît sans changement du web.
- **Constructeur** : formulaire → `CustomStrategyDefinition` correcte (types, champs `null` selon R3) ; nom en double → erreur ; erreurs de `Validate` affichées ; succès → `SaveAsync` appelé et redirection ; suppression.
- **Avancé** : comparateur avec une stratégie en erreur ; heatmap (`IsIsolatedPeak` → bandeau ; cellule `null` rendue) ; train/test (découpage T1 sur 100 barres à 70 % → 70 / 30, dates de coupure) ; Monte Carlo avec moins de 5 trades → message.
- **Bandeau fakes** : présent si une clé est vraie, absent sinon.

## 8. Paquets NuGet autorisés

Aucun. Bootstrap, Chart.js et `chartjs-chart-matrix` sont des fichiers statiques dans `wwwroot/lib/`.

## 9. Découpage en PR (dans cet ordre)

| # | Branche | Contenu | Titre de PR |
|---|---|---|---|
| 1 | `part4/feat-layout-and-assets` | P4-U13, P4-U14, P4-U15, page d'erreur, badge quota (P4-U11) | `feat(web): add layout, navigation and local assets` |
| 2 | `part4/feat-backtest-form-and-permalinks` | `BacktestQuery`, `/api/strategies`, P4-U1, P4-U2, P4-U12, résultat minimal | `feat(web): add backtest form and permalinks` |
| 3 | `part4/feat-results-page` | P4-U3 à P4-U7 | `feat(web): add results page with charts and trades` |
| 4 | `part4/feat-strategies-and-rule-builder` | P4-U8, P4-U9 | `feat(web): add strategy catalog and rule builder` |
| 5 | `part4/feat-data-pages` | P4-U10 | `feat(web): add data and quality pages` |
| 6 | `part4/feat-downloads` | PDF, CSV | `feat(web): add report downloads` |
| 7 | `part4/feat-compare` | P4-X1 | `feat(web): add strategy comparator` |
| 8 | `part4/feat-optimize-heatmap` | P4-X2 | `feat(web): add optimisation heatmap` |
| 9 | `part4/feat-train-test` | P4-X3 | `feat(web): add train and test validation` |
| 10 | `part4/feat-monte-carlo-page` | P4-X4 | `feat(web): add Monte Carlo page` |
| 11+ | `part4/chore-use-real-<partie>` | P4-I1, une PR par bascule | `chore(web): use real <data|reports|strategies|analytics|engine> services` |
| fin | `part4/docs-part4-report` | `docs/report/part-4.md` + captures | `docs(docs): write part 4 report section` |

Les PR 1 à 10 se font **sans attendre personne** grâce aux Fakes.

## 10. Definition of Done

- [ ] Toutes les routes et P4-U1 à P4-U16, P4-X1 à P4-X6, P4-I1, P4-I2 sont faits.
- [ ] L'application fonctionne avec les Fakes **et** avec tous les vrais services (toutes les clés `UseFakes:*` à `false`, plus de bandeau).
- [ ] Aucune logique financière, aucune référence à une classe `internal` d'une autre partie, aucune stratégie codée en dur.
- [ ] Chaque page de résultat a un lien permanent qui redonne le même résultat.
- [ ] Tous les tests de la section 7 passent ; `dotnet build -warnaserror` vert.
- [ ] Interface utilisable de 360 à 1 920 px ; démo possible hors ligne.
- [ ] Vidéo de 5 minutes montée (P4-V1) ; `part-4.md` rempli, captures présentes et légendées.

## 11. Rapport et captures d'écran

Partie la plus visuelle : ces captures servent aussi au manuel (Partie 2) et à la vidéo.

| 📸 | Fichier | Quand | Quoi |
|---|---|---|---|
| 1 | `part4-01-layout-fakes.png` | Fin de la PR 1 | Page d'accueil, navigation, badge quota, bandeau « Demo data » |
| 2 | `part4-02-form-strategy-a.png` | Fin de la PR 2 | Formulaire avec une stratégie et ses paramètres |
| 3 | `part4-03-form-strategy-b.png` | Fin de la PR 2 | Le même formulaire avec une autre stratégie : les champs ont changé |
| 4 | `part4-04-form-validation.png` | Fin de la PR 2 | Erreur de validation (capital négatif) avec saisie conservée |
| 5 | `part4-05-error-limit.png` | Fin de la PR 2 | Message pour le symbole `LIMIT` (quota atteint) |
| 6 | `part4-06-results-metrics.png` | Fin de la PR 3 | En-tête, tableau Strategy vs Buy & hold |
| 7 | `part4-07-results-charts.png` | Fin de la PR 3 | Les graphiques : équité, drawdown, cours avec indicateurs et marqueurs, oscillateur |
| 8 | `part4-08-results-trades.png` | Fin de la PR 3 | Le tableau des trades trié, raisons de sortie visibles |
| 9 | `part4-09-permalink.png` | Fin de la PR 3 | L'URL complète dans la barre d'adresse et le bouton « Copy link » |
| 10 | `part4-10-catalog.png` | Fin de la PR 4 | Le catalogue avec les fiches |
| 11 | `part4-11-rule-builder.png` | Fin de la PR 4 | Le constructeur avec 2 conditions d'entrée et 1 de sortie |
| 12 | `part4-12-data-quality.png` | Fin de la PR 5 | Page Données et page qualité avec anomalies |
| 13 | `part4-13-compare.png` | Fin de la PR 7 | Le comparateur avec 3 stratégies |
| 14 | `part4-14-heatmap.png` | Fin de la PR 8 | Une heatmap avec meilleure cellule et bandeau d'overfitting |
| 15 | `part4-15-train-test.png` | Fin de la PR 9 | Le tableau train vs test et les deux courbes |
| 16 | `part4-16-monte-carlo.png` | Fin de la PR 10 | Les histogrammes et le tableau des percentiles |
| 17 | `part4-17-real-services.png` | Dernière PR P4-I1 | Un backtest réel, **sans** bandeau jaune |
| 18 | `part4-18-mobile.png` | Fin de la PR 3 | La page de résultats à 360 px de large |
| 19 | `part4-19-pr-<n>.png` | À chaque PR mergée | La page de la PR : checks verts et approbation |

## 12. Prompt de démarrage

À coller dans Claude Code, ouvert à la racine du dépôt, sur ta branche `part4/...` :

```
Tu es mon assistant de développement sur le projet QuantBacktest. Je suis Alexis, responsable de la PARTIE 4 (Application web et vidéo de démonstration).

1. Lis en entier : CLAUDE.md, docs/CONTRACT.md, docs/GIT_WORKFLOW.md et docs/parts/PART-4.md.
2. Résume en 10 lignes maximum : mon périmètre, les fichiers autorisés et interdits, les interfaces consommées, les routes et les paramètres d'URL. Liste les points ambigus ou contradictoires que tu vois, sans les trancher toi-même.
3. N'écris aucun code avant que j'aie validé ce résumé.
4. Propose ensuite le plan détaillé de la PR 1 de la section 9 (fichiers, tests, identifiants d'exigences couverts) et attends mon accord.

Rappels : je ne modifie que mon périmètre ; le contrat est gelé ; aucune logique financière dans le web ; je développe d'abord avec les Fakes ; tu me signales chaque capture d'écran 📸 de la section 11 au moment où elle doit être prise, avec le nom de fichier.
```

## 13. Dépendances et synchronisation

- **Tu n'attends personne** pour les PR 1 à 10 : les Fakes couvrent toutes les interfaces, y compris les cas d'erreur (`UNKNOWN`, `NOKEY`, `LIMIT`, `UNCACHED`).
- **Tu attends** les merges listés en P4-I1 pour chaque bascule. Les autres te préviennent ; vérifie quand même le code.
- **Les autres t'attendent** : le manuel de la Partie 2 et les captures « après intégration » de chacun dépendent de tes pages. Préviens l'équipe dès qu'une page est mergée.
