# PARTIE 2 — Stratégies, règles sans code, Monte Carlo, rapport et manuel

> Cette notice est ta spécification. Elle prime sur toute supposition. En cas de doute : demande à l'humain, ne devine pas.
> Contrat commun : `docs/CONTRACT.md` (règles citées par leur identifiant, par exemple I5, R6, MC4). Règles Git : `docs/GIT_WORKFLOW.md`. Règles de code : `CLAUDE.md`.

## 1. Identité

| | |
|---|---|
| Responsable | Victor Haegeman (@VictorHaegeman) |
| Projets | `src/QuantBacktest.Strategies`, `src/QuantBacktest.Analytics` et leurs projets de tests |
| Préfixe de branche | `part2/` |
| Label GitHub | `part-2` |
| Scopes de commit | `strategies`, `analytics`, `docs` |

## 2. Mission

1. Implémenter les indicateurs, les **10 stratégies** plus le buy & hold de référence, le catalogue, le gabarit C# de stratégie personnalisée et le **moteur de règles sans code** (`ICustomStrategyFactory`).
2. Implémenter la **simulation Monte Carlo** (`IMonteCarloService`).
3. Assembler le **rapport final** et écrire le **manuel utilisateur** (livrables transverses).

## 3. Périmètre

### Fichiers que tu possèdes

- `src/QuantBacktest.Strategies/**`, `tests/QuantBacktest.Strategies.Tests/**`
- `src/QuantBacktest.Analytics/**`, `tests/QuantBacktest.Analytics.Tests/**`
- `docs/report/**` (assemblage) et `docs/user-manual/**`
- `Directory.Packages.props` : ajout uniquement (normalement rien)

### Interdit de modifier

Tout le reste : `Core`, `Fakes`, `docs/CONTRACT.md`, `docs/parts/`, les projets des autres parties, `.github/`, `scripts/`, `CLAUDE.md`. Dans `docs/report/`, tu assembles et tu relis, mais tu ne réécris pas le contenu technique d'une autre partie : tu lui demandes de corriger sa section.

### Frontières avec les autres parties

- **Une stratégie ne simule rien** (S1) : ni capital, ni frais, ni stops, ni exécution. Elle retourne des signaux et des indicateurs, c'est tout. Stop-loss, take-profit et trailing stop sont dans le moteur (Partie 3).
- **Plages de paramètres** : la Partie 3 vérifie `[Min, Max]` et le caractère entier **avant** d'appeler ta stratégie. Toi, tu vérifies seulement les **relations** entre paramètres (S5).
- **Règles sans code** : tu valides et tu exécutes les définitions ; tu ne les stockes pas (Partie 1, `ICustomStrategyStore`) et tu ne construis pas le formulaire (Partie 4).
- **Monte Carlo** : tu reçois un `BacktestResult` déjà calculé ; tu ne relances aucun backtest et tu ne recalcules aucune métrique du moteur.
- **Aucune dépendance** vers Data, Engine, Reports ou Web : uniquement `Core`. Tes tests utilisent `SyntheticPrices`, `FakeCustomStrategyStore` et `FakeBacktestService` du projet `Fakes`.

## 4. Contrat

### Ce que tu exposes

| Interface (`Core`) | Ton implémentation | Enregistrement |
|---|---|---|
| `IStrategy` | une classe `internal` par stratégie, toutes dérivées de `StrategyBase` | découverte par réflexion (P2-C1) |
| `IStrategyCatalog` | `StrategyCatalog` (`internal`) | `AddQuantBacktestStrategies()` |
| `ICustomStrategyFactory` | `RuleStrategyFactory` (`internal`) | idem |
| `IMonteCarloService` | `MonteCarloService` (`internal`) | `AddQuantBacktestAnalytics()` |

Signatures des méthodes d'enregistrement déjà en place : tu remplis le corps, tu ne les changes pas. Tout en `Singleton`.

### Ce que tu consommes

`Core` uniquement, dont `ICustomStrategyStore` (injecté dans `StrategyCatalog` ; en tests : `FakeCustomStrategyStore`).

## 5. Spécification du socle

### Indicateurs

| Id | Exigence |
|---|---|
| P2-I1 | Classe `internal static Indicators` : `Sma`, `Ema`, `StdDev`, `Bollinger`, `Rsi`, `Roc`, `Macd`, `HighestHigh`, `LowestLow`, `CrossesAbove`, `CrossesBelow`, **exactement** selon I1 à I9. Entrées `double[]`, sorties de même longueur, `double.NaN` pendant le warm-up. Une seule implémentation partagée par les stratégies intégrées et par le moteur de règles. |
| P2-I2 | Conversion d'une liste `double?[]` pour `IndicatorSeries` : `NaN` devient `null`. |

### Stratégies intégrées

Règles communes : S1 à S6. « Croise au-dessus » et « croise en dessous » = I9. Une valeur indéfinie rend la condition fausse.

| Id | Règles exactes | Relation (sinon `BacktestValidationException`) | Indicateurs affichés (`ComputeIndicators`) |
|---|---|---|---|
| P2-S1 `sma-crossover` | Buy : `SMA(fast)` croise au-dessus de `SMA(slow)`. Sell : croise en dessous. | `fast < slow` | `SMA(fast)`, `SMA(slow)` (Price) |
| P2-S2 `bollinger-reversion` | Buy : `close[i] < lower[i]` et `close[i-1] >= lower[i-1]`. Sell : la clôture croise au-dessus de la bande médiane si `exitLevel = 1`, de la bande haute si `exitLevel = 0` (même forme de croisement). | — | 3 bandes (Price) |
| P2-S3 `rsi-reversion` | Buy : `RSI < oversold`. Sell : `RSI > overbought`. | `oversold < overbought` | `RSI(period)` (Oscillator, lignes `oversold`, `overbought`) |
| P2-S4 `momentum-roc` | Buy : `ROC(lookback)` croise au-dessus de `threshold`. Sell : croise en dessous de `threshold`. | — | `ROC(lookback)` (Oscillator, ligne `threshold`) |
| P2-S5 `macd-crossover` | Buy : ligne MACD croise au-dessus du signal. Sell : en dessous. | `fast < slow` | ligne et signal (Oscillator, ligne 0) |
| P2-S6 `donchian-breakout` | Buy : `close[i] > HighestHigh(entryLookback)[i] × (1 + threshold)`. Sell : `close[i] < LowestLow(exitLookback)[i]`. | — | les deux canaux (Price) |
| P2-S7 `bollinger-rsi-confirmation` | Buy : `close < lower` **et** `RSI(rsiPeriod) < oversold`. Sell : `close > middle` **ou** `RSI > overbought`. | `oversold < overbought` | 3 bandes (Price), RSI (Oscillator) |
| P2-S8 `connors-rsi2` | Buy : `close > SMA(trendPeriod)` **et** `RSI(rsiPeriod) < entryThreshold`. Sell : `close > SMA(exitPeriod)`. | `exitPeriod < trendPeriod` | `SMA(trendPeriod)`, `SMA(exitPeriod)` (Price), RSI (Oscillator, ligne `entryThreshold`) |
| P2-S9 `consecutive-down-days` | Buy à la barre `i >= downDays` si `close[i-k] < close[i-k-1]` pour `k = 0..downDays-1` **et** `close[i] / close[i-downDays] - 1 <= -minDrop`. Sell : `close[i] > close[i-1]`. | — | aucun |
| P2-S10 `turn-of-month` | Buy à la barre `i` si le nombre de jours ouvrés (lundi à vendredi) **strictement après** `bars[i].Date` et jusqu'au dernier jour du mois est égal à `daysBeforeEnd` (les jours fériés ne sont pas connus : approximation documentée). Sell à la barre `i` si c'est la `daysAfterStart`-ième barre de son mois parmi `bars[0..i]`. Timeframe Daily uniquement. | — | aucun |
| P2-S11 `buy-and-hold` | Buy sur toutes les barres. | — | aucun |

Identifiants, noms, timeframes et paramètres : **exactement** le tableau de `CONTRACT.md` section 8 (la Partie 4 et le PDF les affichent).

| Id | Exigence |
|---|---|
| P2-S12 | **`Description`** : 1 à 2 phrases anglaises. **`Documentation`** : `Idea` (2 à 3 phrases, intuition économique), `EntryRule` et `ExitRule` (la règle exacte, avec les valeurs par défaut), `Risks` (quand la stratégie perd : tendance forte, faux signaux, coûts de transaction...). Texte brut, sans Markdown. |
| P2-S13 | **`Category`** : `TrendFollowing` (S1, S5), `MeanReversion` (S2, S3, S7, S8, S9), `Momentum` (S4), `Breakout` (S6), `Calendar` (S10), `Baseline` (S11). |

### Catalogue, base commune, gabarit

| Id | Exigence |
|---|---|
| P2-C1 | **`StrategyCatalog`** : découvre par réflexion les classes non abstraites de l'assembly qui implémentent `IStrategy`, ont un constructeur public sans paramètre et ne portent pas `[HiddenStrategy]` (attribut défini dans ton projet). Doublon d'identifiant → `InvalidOperationException` au démarrage, nommant les deux classes. `GetAllAsync` : intégrées triées par `Name`, puis personnalisées (`ICustomStrategyStore.GetAllAsync` → `ICustomStrategyFactory.Create`) triées par `Name` ; une définition stockée devenue invalide est **ignorée** avec un warning journalisé. `FindAsync` : correspondance exacte ; pour un id `custom-...` connu mais invalide, lève `BacktestValidationException` avec les problèmes. |
| P2-C2 | **`StrategyBase`** (publique, abstraite) : lecture d'un paramètre avec défaut, aide à la validation des relations, extraction des tableaux `Open`, `High`, `Low`, `Close`, `Volume` en `double[]`. Les 11 stratégies en héritent. |
| P2-C3 | **Gabarit** `src/QuantBacktest.Strategies/Custom/TemplateStrategy.cs` : `[HiddenStrategy]`, entièrement commenté (où mettre l'id, la documentation, les paramètres, la logique), exemple minimal qui compile. Copier le fichier, retirer l'attribut et changer l'id suffit pour voir la stratégie dans l'application. Pas de stratégie d'exemple non masquée : le manuel explique la procédure. |

### Stratégies sans code (exigence de l'énoncé : « users can define simple trading strategies »)

| Id | Exigence |
|---|---|
| P2-N1 | **Validation** : `RuleStrategyFactory.Validate` vérifie R1 à R4 et retourne **tous** les problèmes, chacun avec sa position (« Entry condition 2: the right operand needs a period between 1 and 500. »). `Create` lève `BacktestValidationException` (problèmes joints par un espace) ou retourne une `RuleBasedStrategy` qui applique R5 à R8. |
| P2-N2 | **Documentation générée** (R8) : `Idea` = `Description` (ou « Custom no-code strategy. » si vide) ; `EntryRule` = « Buy when ALL of: Close > SMA(200); RSI(2) < 10 » (« ANY of » pour `Any`) ; `ExitRule` = « Sell when ANY of: ... » ; `Risks` = « User-defined rules. Check the train/test page before trusting the results. ». Notation : CONTRACT section 8. |
| P2-N3 | **Indicateurs générés** (R8) : une `IndicatorSeries` par opérande distinct (hors `Constant`, `Close`, `Open`, `High`, `Low`, `Volume`) ; clé en minuscules (`sma-200`, `bb-lower-20-2`, `rsi-2`, `macd-line`, `highest-high-20`), libellé = notation textuelle ; `Price` pour SMA, EMA, Bollinger, Highest/Lowest, `Oscillator` pour RSI, ROC, MACD. |

## 6. Spécification des fonctionnalités avancées

À commencer **après** le merge du socle (section 9, PR 1 à 7).

| Id | Fonctionnalité | Exigence |
|---|---|---|
| P2-X1 | Monte Carlo (n°7) | `MonteCarloService.Run` : exactement MC1 à MC7. Fonctions `Percentile` et `Histogram` séparées et testables seules. |

### Livrables transverses

| Id | Exigence |
|---|---|
| P2-M1 | **Rapport final** (plan figé dans `docs/report/README.md`) : tu rédiges les sections 1, 4, 6 (partie Monte Carlo), 9 ; tu assembles `docs/report/REPORT.md`, harmonises les termes et les légendes, puis exportes `docs/report/QuantBacktest-Report.pdf`. Signale par issue toute section manquante. |
| P2-M2 | **Manuel utilisateur** (`docs/user-manual/MANUAL.md`, anglais) : Getting started (local et Docker), Fetching and refreshing data, Running a backtest, Reading the results, Strategies reference (une fiche par stratégie), Building a strategy without code, Writing a strategy in C# (depuis le gabarit), Comparing strategies, Optimising parameters and the overfitting warning, Train/test validation, Monte Carlo, Data quality and API quota, Exporting reports, Troubleshooting (limite API, symbole introuvable, prix non ajustés, pas assez de trades pour le Monte Carlo). Illustré par des captures de l'application réelle (section 11). |

## 7. Tests obligatoires

- **Indicateurs, valeurs calculées à la main** : `Sma([1,2,3,4,5], 3) = [NaN, NaN, 2, 3, 4]` ; `Ema([1,2,3,4,5], 3) = [NaN, NaN, 2, 3, 4]` ; `Roc([100, 110, 121], 1) = [NaN, 0.1, 0.1]` ; RSI d'une série strictement croissante = 100, décroissante = 0, constante = 50 ; Bollinger d'une série constante = 3 bandes confondues ; `HighestHigh` exclut la barre courante ; croisements I9 (égalité à `i-1`, valeur indéfinie).
- **Indicateurs, référence indépendante** : pour `Rsi`, `Macd`, `Bollinger` sur 60 valeurs, attendus calculés **hors de ton code** (tableur ou Python/pandas), collés en dur, méthode notée en commentaire ; tolérance `1e-9`.
- **Chaque stratégie (test paramétré sur les 11)** : longueur = nombre de barres ; `Hold` pendant le warm-up ; id, nom, timeframes et paramètres conformes au tableau du contrat ; `Min <= Default <= Max`, `Step > 0` ; aucune exception avec les défauts sur 500, 5 et 1 barres ; chaque `IndicatorSeries` a une valeur par barre.
- **Pas de look-ahead (test paramétré sur les 11, `SyntheticPrices`, 3 graines)** : pour plusieurs `k`, les signaux et les indicateurs calculés sur `bars[0..k]` sont **identiques** au préfixe de ceux calculés sur toute la série.
- **Cas construits à la main, un par stratégie** : une série fabriquée qui produit un Buy et un Sell à des indices connus, écrits en dur. Pour `turn-of-month` : un mois réel (octobre 2026) avec les dates exactes du Buy et du Sell attendus. Pour `consecutive-down-days` : `minDrop` qui bloque un Buy.
- **Relations (S5)** : chaque relation violée lève `BacktestValidationException`.
- **S4** : une série où achat et vente sont vrais en même temps donne `Sell` (`bollinger-rsi-confirmation`).
- **Catalogue** : exactement les 11 ids intégrés + les personnalisés du store factice ; tri ; `[HiddenStrategy]` exclut le gabarit ; doublon artificiel → exception ; définition invalide dans le store ignorée par `GetAllAsync`.
- **Règles sans code** : un test par règle R1 à R4 (message attendu) ; R6 sur chaque opérateur ; R7 avec entrée et sortie vraies → `Sell` ; un opérande en warm-up rend la condition fausse ; documentation générée pour `FakeCustomStrategyStore.Example` identique à la chaîne attendue ; sur une série `SyntheticPrices`, la définition `Example` émet `Buy` exactement aux barres où `Close > SMA(200)`, `RSI(2) < 10` et où la sortie n'est pas vraie (vérifié barre par barre avec tes indicateurs).
- **Monte Carlo** : `Percentile` sur `[1, 2, 3, 4, 5]` donne `P25 = 2`, `P50 = 3`, `P5 = 1.2` ; trades de rendements `[0.1, -0.1]` répétés : trajectoire réelle `V = 1.1, 0.99` → `ActualFinalReturn = -0.01`, `ActualMaxDrawdown = 0.1` ; tous les trades gagnants → `ProbabilityOfLoss = 0` ; même graine → résultat identique, graine différente → différent ; somme des histogrammes = `Simulations` ; `min = max` → un seul intervalle ; moins de 5 trades ou `Simulations` hors plage → exception.

## 8. Paquets NuGet autorisés

Aucun.

## 9. Découpage en PR (dans cet ordre)

| # | Branche | Contenu | Titre de PR |
|---|---|---|---|
| 1 | `part2/feat-indicators` | P2-I1, P2-I2 + tests | `feat(strategies): add technical indicators` |
| 2 | `part2/feat-strategy-base-and-catalog` | P2-C1, P2-C2, `buy-and-hold`, `sma-crossover`, DI | `feat(strategies): add strategy base, catalog and first strategies` |
| 3 | `part2/feat-reversion-strategies` | S2, S3, S7, S8 | `feat(strategies): add mean reversion strategies` |
| 4 | `part2/feat-trend-strategies` | S4, S5, S6 | `feat(strategies): add momentum and breakout strategies` |
| 5 | `part2/feat-pattern-strategies` | S9, S10 | `feat(strategies): add down-days and turn-of-month strategies` |
| 6 | `part2/feat-rule-based-strategies` | P2-N1 à P2-N3, enregistrement de `ICustomStrategyFactory` | `feat(strategies): add no-code rule strategies` |
| 7 | `part2/feat-csharp-template` | P2-C3 | `feat(strategies): add custom strategy template` |
| 8 | `part2/feat-monte-carlo` | P2-X1, DI Analytics | `feat(analytics): add Monte Carlo simulation` |
| 9 | `part2/docs-part2-report` | `docs/report/part-2.md` | `docs(docs): write part 2 report section` |
| 10 | `part2/docs-user-manual` | P2-M2 (après intégration du web) | `docs(docs): write user manual` |
| 11 | `part2/docs-final-report` | P2-M1 (après les sections de tous) | `docs(docs): assemble final report` |

Les PR 1 à 6 sont ton chemin critique : la Partie 3 en a besoin pour tester avec de vraies stratégies et la Partie 4 pour brancher le vrai catalogue (qui exige aussi `ICustomStrategyFactory`). Enchaîne-les sans attendre les PR suivantes. Jusqu'à la PR 6, `AddQuantBacktestStrategies` n'enregistre que le catalogue : c'est normal, la Partie 4 reste en fakes pour les stratégies.

## 10. Definition of Done

- [ ] Les 11 stratégies respectent exactement le tableau du contrat et les règles P2-S1 à P2-S13.
- [ ] Les 11 passent le test anti look-ahead.
- [ ] `AddQuantBacktestStrategies` et `AddQuantBacktestAnalytics` enregistrent les vraies implémentations.
- [ ] Le gabarit existe, est masqué, compile et est documenté ; une stratégie copiée depuis lui apparaît dans l'application.
- [ ] Règles sans code et Monte Carlo conformes à R1 à R8 et MC1 à MC7.
- [ ] Tous les tests de la section 7 passent ; `dotnet build -warnaserror` vert.
- [ ] Manuel écrit avec captures réelles ; rapport assemblé et exporté en PDF ; `part-2.md` rempli.

## 11. Rapport et captures d'écran

| 📸 | Fichier | Quand | Quoi |
|---|---|---|---|
| 0 | `part2-00-first-pr.png` | Première PR mergée de l'équipe | La PR avec checks verts, approbation et « Squash and merge » (reçue de celui qui merge en premier) |
| 1 | `part2-01-indicator-tests.png` | Fin de la PR 1 | Les tests d'indicateurs qui passent |
| 2 | `part2-02-reference-values.png` | Fin de la PR 1 | Le tableur ou le script Python de référence pour RSI/MACD/Bollinger |
| 3 | `part2-03-lookahead-test.png` | Fin de la PR 2, puis à refaire fin de la PR 5 | Le test anti look-ahead qui passe sur toutes les stratégies |
| 4 | `part2-04-catalog.png` | Fin de la PR 5 | La sortie de test listant les 11 stratégies et leurs paramètres |
| 5 | `part2-05-template-copy.png` | Fin de la PR 7 | Le code d'une stratégie copiée depuis le gabarit |
| 6 | `part2-06-rule-validation.png` | Fin de la PR 6 | Les tests de validation des règles qui passent |
| 7 | `part2-07-montecarlo-tests.png` | Fin de la PR 8 | Les tests Monte Carlo (percentiles, trajectoire réelle) qui passent |
| 8 | `part2-08-strategies-page.png` | Après intégration du web | La page Strategies avec les 11 fiches |
| 9 | `part2-09-template-in-ui.png` | Après intégration du web | La stratégie de la capture 5 présente dans la liste du formulaire |
| 10 | `part2-10-rule-builder.png` | Après intégration du web | Le constructeur sans code avec une règle d'entrée et une de sortie |
| 11 | `part2-11-montecarlo-page.png` | Après intégration du web | La page Monte Carlo d'un backtest réel |
| 12 | `part2-12-manual-<sujet>.png` | Pendant la PR 10 | Une capture par chapitre du manuel (formulaire, résultats, comparateur, heatmap, train/test, données, erreur API) |
| 13 | `part2-13-pr-<n>.png` | À chaque PR mergée | La page de la PR : checks verts et approbation |

Tu **collectes** aussi les captures GitHub des autres (Partie 1 : `part1-00` à `part1-03`) pour la section « Quality and teamwork ».

## 12. Prompt de démarrage

À coller dans Claude Code, ouvert à la racine du dépôt, sur ta branche `part2/...` :

```
Tu es mon assistant de développement sur le projet QuantBacktest. Je suis Victor, responsable de la PARTIE 2 (Stratégies, règles sans code, Monte Carlo, rapport et manuel).

1. Lis en entier : CLAUDE.md, docs/CONTRACT.md, docs/GIT_WORKFLOW.md et docs/parts/PART-2.md.
2. Résume en 10 lignes maximum : mon périmètre, les fichiers autorisés et interdits, les interfaces à implémenter, les frontières avec les autres parties. Liste les points ambigus ou contradictoires que tu vois, sans les trancher toi-même.
3. N'écris aucun code avant que j'aie validé ce résumé.
4. Propose ensuite le plan détaillé de la PR 1 de la section 9 (fichiers, tests, identifiants de règles couverts) et attends mon accord.

Rappels : je ne modifie que mon périmètre ; le contrat est gelé ; une stratégie ne simule rien et ne regarde jamais le futur ; chaque test cite la règle qu'il vérifie ; tu me signales chaque capture d'écran 📸 de la section 11 au moment où elle doit être prise, avec le nom de fichier.
```

## 13. Dépendances et synchronisation

- **Tu n'attends personne** pour le code : `FakeCustomStrategyStore` remplace le store de la Partie 1, `FakeBacktestService` fournit des `BacktestResult` pour le Monte Carlo.
- **Les autres t'attendent** : la Partie 4 bascule `UseFakes:Strategies` à `false` après ta PR 6, `UseFakes:Analytics` après ta PR 8. La Partie 3 teste ses scénarios réels avec tes stratégies après la PR 2. **Préviens l'équipe à chaque merge.**
- Le manuel et le rapport dépendent de l'interface intégrée : commence la structure tôt, les captures à la fin.
