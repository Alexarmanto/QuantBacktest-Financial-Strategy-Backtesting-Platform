# PARTIE 3 — Moteur de backtest, ordres avancés, métriques et heatmap

> Cette notice est ta spécification. Elle prime sur toute supposition. En cas de doute : demande à l'humain, ne devine pas.
> Contrat commun : `docs/CONTRACT.md` (règles citées par leur identifiant, par exemple X8, M7, O4). Règles Git : `docs/GIT_WORKFLOW.md`. Règles de code : `CLAUDE.md`.

## 1. Identité

| | |
|---|---|
| Responsable | Eloi Dumas (@d-elmus) |
| Projets | `src/QuantBacktest.Engine` et `tests/QuantBacktest.Engine.Tests` |
| Préfixe de branche | `part3/` |
| Label GitHub | `part-3` |
| Scopes de commit | `engine`, `docs` |

## 2. Mission

Simuler l'exécution d'une stratégie sur un historique (signaux, frais, slippage, stop-loss, take-profit, trailing stop), calculer toutes les métriques de performance, valider les requêtes et produire la heatmap d'optimisation, de façon **exacte, déterministe et vérifiée à la main**. Tu es le cœur quantitatif : les autres parties affichent tes chiffres sans les recalculer.

## 3. Périmètre

### Fichiers que tu possèdes

- `src/QuantBacktest.Engine/**`, `tests/QuantBacktest.Engine.Tests/**`
- `docs/report/part-3.md`, `docs/report/images/part3-*`
- `Directory.Packages.props` : ajout uniquement (normalement rien)

### Interdit de modifier

Tout le reste : `Core`, `Fakes`, `docs/CONTRACT.md`, `docs/parts/`, les projets des autres parties, `.github/`, `scripts/`, `CLAUDE.md`.

### Frontières avec les autres parties

- **Tu es la seule source des chiffres.** Équité, drawdowns, trades, métriques de la stratégie et du benchmark, indicateurs tranchés à la période : tout est dans `BacktestResult`. Personne ne recalcule.
- **Tu ne formates rien** : fractions et `decimal` bruts.
- **`BacktestEngine` est pur** : ni réseau, ni base, ni fichier, ni horloge. Seuls `BacktestService` et `ParameterSweepService` appellent `IMarketDataService` et `IStrategyCatalog`.
- **Tu n'écris aucune stratégie** : tes tests utilisent une `ScriptedStrategy` (dans ton projet de tests) qui renvoie des signaux donnés d'avance.
- **Ne t'inspire pas des `Fakes`** : `FakeBacktestService` est volontairement approximatif (stop-loss seulement, presque aucune validation). Ta référence est `CONTRACT.md` sections 4, 5 et 6.
- **Train/test et comparateur** sont orchestrés par la Partie 4 avec tes services ; tu n'as pas de service dédié à écrire.

## 4. Contrat

### Ce que tu exposes

| Interface (`Core`) | Ton implémentation (`internal`) | Enregistrement |
|---|---|---|
| `IBacktestEngine` | `BacktestEngine` (sans état) | `AddQuantBacktestEngine()` |
| `IBacktestService` | `BacktestService` | idem |
| `IParameterSweepService` | `ParameterSweepService` | idem |

Tout en `Singleton`. Signature de la méthode d'enregistrement déjà en place.

### Ce que tu consommes

`IMarketDataService` et `IStrategyCatalog` par injection. En développement et en tests : `FakeMarketDataService`, `FakeStrategyCatalog`, `SyntheticPrices` (projet `Fakes`) ou tes propres doublures.

## 5. Spécification du socle

| Id | Exigence |
|---|---|
| P3-E1 | **Arithmétique** : portefeuille en `decimal` ; rendements convertis en `double` pour les statistiques. |
| P3-E2 | **`BacktestEngine.Run`**, dans l'ordre : (1) valider X1 et les réglages d'exécution (§4 du contrat, dernier paragraphe) ; (2) trouver `first` et `last` ; (3) appeler `GenerateSignals` et `ComputeIndicators` avec `bars[0..last]` (X2) ; vérifier les longueurs, sinon `InvalidOperationException` (bug de stratégie, pas erreur utilisateur) ; (4) boucle X7 de `first` à `last`, **sans** sorties de protection dans le socle (X8 arrive en P3-X1) ; (5) benchmark X14 ; (6) métriques ; (7) indicateurs tranchés à `[first..last]`. Règles X1 à X7 et X9 à X15. |
| P3-E3 | **`PerformanceCalculator`** : classe statique indépendante du moteur (entrées : équités, dates, flags `InPosition`, capital initial, trades, timeframe, taux sans risque) ; implémente M1 à M14 ; sert aussi au benchmark (M13). |
| P3-E4 | **`BacktestService.RunAsync`** : (1) `FindAsync` de la stratégie (inconnue : `BacktestValidationException("Unknown strategy '<id>'.")`) ; (2) timeframe hors `SupportedTimeframes` : rejet nommant la stratégie et le timeframe ; (3) paramètres : clé inconnue, valeur hors `[Min, Max]`, valeur non entière pour `Integer` : rejet nommant le paramètre et sa plage ; clés absentes complétées par leurs défauts ; (4) réglages d'exécution : toutes les plages du contrat ; (5) `GetSeriesAsync(symbol, timeframe, forceRefresh: false)` ; (6) `IBacktestEngine.Run` ; (7) `MarketDataException` traverse sans être enveloppée. La validation (3) et (4) est une classe `internal RequestValidator` réutilisée par la heatmap. |
| P3-E5 | **Résultat** : `Bars` = barres de la période ; `Equity` et chaque `Indicators[k].Values` ont `Bars.Count` éléments ; `StrategyName = strategy.Name` ; `PricesAdjusted = series.IsAdjusted` ; trades par date d'entrée ; `Request` = la requête **complétée** des paramètres par défaut. |
| P3-E6 | **Déterminisme** : mêmes entrées, même sortie (égalité de record, listes comparées élément par élément). Pas d'horloge, pas d'aléa. |

## 6. Spécification des fonctionnalités avancées

À commencer **après** le merge du socle (section 9, PR 1 à 3).

| Id | Fonctionnalité | Exigence |
|---|---|---|
| P3-X1 | Ordres avancés (n°1) | Étape (b) de X7 : sorties de protection **exactement** X8 (gap puis intrabarre, stop avant cible, `peak` sur les `High` des barres `entrée..i-1`), raison X9, slippage X6 sur tous les fills (déjà dans le socle) et `HoldingBars` X12. |
| P3-X2 | Métriques avancées (n°5) | Sortino (M7), Calmar (M9), profit factor (M11), exposition (M12) ; si tu les as déjà mises dans le socle, cette ligne est cochée d'office. |
| P3-X3 | Heatmap (n°2) | `ParameterSweepService.RunAsync` : exactement O1 à O5. Chargement de la série **une fois** ; stratégie résolue une fois ; chaque cellule passe par `RequestValidator` puis `BacktestEngine.Run`. Une `BacktestValidationException` d'une cellule devient `Value = null`, `Error = message`. La logique O3/O4 est une fonction pure `internal static SweepAnalysis.Summarize(...)` testable seule. Exécution séquentielle (simple et déterministe) ; vérifier le `CancellationToken` entre les cellules. Objectif de performance : 625 cellules sur 1 250 barres en moins de 10 s sur un portable. |

## 7. Tests obligatoires

Aucun test n'utilise le réseau. Les valeurs attendues sont **recalculées avec les formules dans le test** (`decimal`, tolérance `1e-6`), la formule écrite en commentaire, **jamais** copiées de la sortie du moteur. Les valeurs « ≈ » ci-dessous, arrondies à 4 décimales, servent à vérifier ton calcul ; elles ont été obtenues indépendamment.

### Série de référence (6 barres, dates = 6 jours ouvrés consécutifs, capital 10 000, frais 0,1 %, slippage 0 sauf mention)

| Barre | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| Open | 100 | 102 | 104 | 106 | 103 | 109 |
| High | 102 | 104 | 106 | 107 | 108.5 | 111 |
| Low | 99 | 101 | 103 | 103.5 | 102 | 108 |
| Close | 101 | 103 | 105 | 104 | 108 | 110 |

Signaux notés B (Buy), S (Sell), H (Hold), barre 0 à 5.

| Scénario | Réglages | Signaux | Trade(s) attendu(s) | Équité aux clôtures ≈ | Exposition |
|---|---|---|---|---|---|
| S1 | — | B H S H H H | entrée Open b1 = 102, sortie Open b3 = 106, `Signal`, `HoldingBars` 2, `NetProfit` ≈ 371.3933, `Fees` ≈ 20.3718, `ReturnFraction` ≈ 0.037139 | 10000 ; 10087.9513 ; 10283.8338 ; 10371.3933 ; 10371.3933 ; 10371.3933 | 2/6 |
| S2 | — | B H H H H H | sortie Close b5 = 110, `EndOfData`, `HoldingBars` 5, `NetProfit` ≈ 762.7666 | … ; 10185.8925 ; 10577.6576 ; 10762.7666 | 5/6 |
| S3 | — | S B B S S B | Sell à plat ignoré ; entrée Open b2 = 104 ; Buy b2 ignoré ; sortie Open b4 = 103 ; Sell b4 ignoré ; Buy b5 ignoré (dernière barre) ; `NetProfit` ≈ -115.9418 | 10000 ; 10000 ; 10086.0678 ; 9990.0100 ; 9884.0582 ; 9884.0582 | 2/6 |
| S4 | `From` = date b2 | B B H B H H | signaux de b0, b1 ignorés ; entrée Open b4 = 103 ; `EndOfData` b5, `HoldingBars` 2, `NetProfit` ≈ 658.2738 ; `Bars` et `Equity` : 4 éléments | 10000 ; 10000 ; 10474.9619 ; 10658.2738 | 2/4 |
| S5 | — | H × 6 | aucun ; volatilité 0 ; Sharpe, Sortino, Calmar, profit factor `null` ; drawdown 0 ; `WinRate` 0 | 10000 × 6 | 0 |
| S6 | trailing 3 % | B H H H H H | gap : à b4, `peak` = 107, niveau 103.79, `Open` 103 ≤ niveau → sortie à l'Open 103, `TrailingStop`, `HoldingBars` 3 | … ; 10185.8925 ; 10077.8633 ; 10077.8633 | 3/6 |
| S7 | trailing 2 % | B H H H H H | intrabarre : à b3, `peak` = 106, niveau 103.88, `Low` 103.5 → sortie à 103.88, `TrailingStop`, `HoldingBars` 2 | … ; 10163.9654 ; 10163.9654 ; 10163.9654 | 2/6 |
| S8 | take-profit 3 % | B H H H H H | cible 105.06 ; à b2, `High` 106 → sortie à 105.06, `TakeProfit`, `HoldingBars` 1 | 10000 ; 10087.9513 ; 10279.4206 ; … | 1/6 |
| S9 | stop-loss 0.5 % et take-profit 1 % | B H H H H H | sur la barre d'entrée b1, `Low` 101 ≤ 101.49 et `High` 104 ≥ 103.02 : le **stop passe avant** → sortie à 101.49, `StopLoss`, `HoldingBars` 0, `NetProfit` ≈ -69.8801 | 10000 ; 9930.1199 ; … | 0 |
| S10 | stop-loss 5 % et trailing 3 % | B H H H H H | identique à S6, raison `TrailingStop` (niveau trailing 103.79 > niveau stop-loss 96.90) | comme S6 | 3/6 |
| S11 | slippage 1 % | B H S H H H | entrée 102 × 1.01 = 103.02, sortie 106 × 0.99 = 104.94, `NetProfit` ≈ 166.0192 | 10000 ; 9988.0706 ; 10182.0137 ; 10166.0192 ; … | 2/6 |

**Benchmark** (frais 0,1 %, slippage 0) : quantité ≈ 99.900100, équité finale ≈ 10989.0110, rendement cumulé ≈ 0.098901 ; `TradeCount` 1, `WinRate` 1, `ProfitFactor` `null`, `Exposure` 1.

### Métriques (`PerformanceCalculator`, sans le moteur)

- Équités `[100, 110, 99, 108.9]`, capital 100, daily, taux sans risque 0 : rendements `[+0.1, -0.1, +0.1]`, moyenne ≈ 0.033333, écart-type échantillon ≈ 0.115470, volatilité ≈ 1.83303, Sharpe ≈ 4.58258, Sortino ≈ 9.16515, drawdown maximal 0.10, rendement cumulé 0.089.
- Même cas en weekly : `sqrt(52)` à la place de `sqrt(252)`.
- 10 000 → 12 100 entre `2021-01-01` et `2023-01-01` : rendement annualisé = `1.21^(1 / (730 / 365.25)) - 1` ≈ 0.100072.
- Taux sans risque 2 % : Sharpe et Sortino diminuent conformément à M6 et M7.
- `NetProfit` `[+30, -10, +20, -5]` : profit factor = 50 / 15 ≈ 3.3333, `WinRate` 0.5 ; trades tous gagnants : profit factor `null`.
- Calmar = rendement annualisé / drawdown maximal ; `null` si drawdown nul.
- Cas dégénérés (1 point, équité constante, effondrement vers 0, aucun trade) : **aucune** valeur `NaN` ni infinie, assertion sur chaque champ.

### Propriétés (`SyntheticPrices`, 5 graines, signaux pseudo-aléatoires à graine fixe, stops activés ou non)

- Équité jamais négative ; `Equity.Count = Bars.Count`, mêmes dates.
- Après liquidation : `FinalEquity = capital + somme des NetProfit` (tolérance `1e-9`).
- Trades ordonnés, sans chevauchement, `EntryDate <= ExitDate`, `HoldingBars >= 0`.
- Pas de look-ahead : modifier l'`Open` de la barre `i+1` change le prix d'entrée déclenché par le signal de `i` ; modifier le `Close` de `i+1` ne le change pas.
- Frais et slippage nuls : `NetProfit = (prix de sortie - prix d'entrée) × quantité`.
- Exposition = part des points `InPosition`.

### Service et heatmap

- Un test par cause de rejet de P3-E4 (message vérifié) ; `MarketDataException` traverse ; deux appels identiques donnent le même résultat ; `Request` complétée des défauts.
- O1 : axe `5..25` pas 5 → 5 valeurs ; axe décimal `0.01..0.03` pas `0.01` → 3 valeurs (arrondi) ; pas non entier sur un paramètre `Integer`, clés identiques, stratégie à moins de 2 paramètres, plus de 625 cellules : rejet.
- O2 : une combinaison qui viole une relation de la stratégie de test donne `Value = null` et `Error` renseigné, sans arrêter le calcul.
- O3/O4 (`SweepAnalysis.Summarize` sur des grilles écrites à la main) : égalité départagée par `Y` puis `X` ; pic isolé détecté ; plateau non détecté ; voisins `null` ignorés ; meilleure valeur négative → `IsIsolatedPeak = false`.
- O5 : ordre des cellules ligne par ligne ; annulation respectée.

## 8. Paquets NuGet autorisés

Aucun.

## 9. Découpage en PR (dans cet ordre)

| # | Branche | Contenu | Titre de PR |
|---|---|---|---|
| 1 | `part3/feat-performance-calculator` | P3-E3 avec M1 à M14 + tests de métriques | `feat(engine): add performance metrics calculator` |
| 2 | `part3/feat-backtest-engine` | P3-E1, P3-E2, P3-E5, P3-E6, scénarios S1 à S5 et S11, benchmark, propriétés | `feat(engine): implement backtest simulation` |
| 3 | `part3/feat-backtest-service` | P3-E4, `RequestValidator`, DI + tests | `feat(engine): add backtest service and validation` |
| 4 | `part3/feat-protective-exits` | P3-X1, scénarios S6 à S10 | `feat(engine): add stop-loss, take-profit and trailing stop` |
| 5 | `part3/feat-parameter-sweep` | P3-X3 + tests | `feat(engine): add parameter sweep heatmap` |
| 6 | `part3/docs-part3-report` | `docs/report/part-3.md` + section 2 (Architecture) du rapport final | `docs(docs): write part 3 report section` |

## 10. Definition of Done

- [ ] X1 à X15, M1 à M14, O1 à O5 implémentées à l'identique ; P3-E1 à P3-E6, P3-X1 à P3-X3 cochées.
- [ ] Les 11 scénarios et le benchmark en tests, valeurs vérifiées une fois à la main ou au tableur.
- [ ] Aucune valeur `NaN` ni infinie possible ; moteur déterministe.
- [ ] `AddQuantBacktestEngine` enregistre les 3 services.
- [ ] Tous les tests de la section 7 passent ; `dotnet build -warnaserror` vert.
- [ ] `part-3.md` rempli (modèle d'exécution, chaque formule, choix prudent stop avant cible, limites : long uniquement, pas de dividendes si prix non ajustés, exécution à l'Open hors protections) ; section Architecture du rapport final écrite.

## 11. Rapport et captures d'écran

| 📸 | Fichier | Quand | Quoi |
|---|---|---|---|
| 1 | `part3-01-metrics-test.png` | Fin de la PR 1 | Le test du cas `[100, 110, 99, 108.9]` qui passe, formules visibles dans le code |
| 2 | `part3-02-hand-check-spreadsheet.png` | Pendant la PR 2 | **Ton tableur** recalculant S1 : quantité, frais, produit, équité par barre |
| 3 | `part3-03-engine-tests.png` | Fin de la PR 2 | Scénarios et propriétés qui passent (`dotnet test`) |
| 4 | `part3-04-lookahead-property.png` | Fin de la PR 2 | Le test « pas de look-ahead » qui passe |
| 5 | `part3-05-validation-tests.png` | Fin de la PR 3 | Les tests de validation du service |
| 6 | `part3-06-protective-exits.png` | Fin de la PR 4 | Tests S6 à S10 qui passent + tableur du cas S6 (gap) |
| 7 | `part3-07-sweep-tests.png` | Fin de la PR 5 | Tests O1 à O5 qui passent |
| 8 | `part3-08-results-real.png` | Après intégration du web | Métriques stratégie vs buy & hold d'un backtest réel avec stops |
| 9 | `part3-09-trades-exit-reasons.png` | Après intégration du web | Le journal des trades montrant plusieurs raisons de sortie |
| 10 | `part3-10-heatmap-real.png` | Après intégration du web | Une heatmap réelle avec l'avertissement d'overfitting |
| 11 | `part3-11-pr-<n>.png` | À chaque PR mergée | La page de la PR : checks verts et approbation |

## 12. Prompt de démarrage

À coller dans Claude Code, ouvert à la racine du dépôt, sur ta branche `part3/...` :

```
Tu es mon assistant de développement sur le projet QuantBacktest. Je suis Eloi, responsable de la PARTIE 3 (Moteur de backtest, ordres avancés, métriques et heatmap).

1. Lis en entier : CLAUDE.md, docs/CONTRACT.md, docs/GIT_WORKFLOW.md et docs/parts/PART-3.md.
2. Résume en 10 lignes maximum : mon périmètre, les fichiers autorisés et interdits, les interfaces à implémenter, les règles X1 à X15, M1 à M14 et O1 à O5 dans tes mots. Liste les points ambigus ou contradictoires que tu vois, sans les trancher toi-même.
3. N'écris aucun code avant que j'aie validé ce résumé.
4. Propose ensuite le plan détaillé de la PR 1 de la section 9 (fichiers, tests, identifiants de règles couverts) et attends mon accord.

Rappels : je ne modifie que mon périmètre ; le contrat est gelé ; les valeurs attendues des tests sont recalculées avec les formules, jamais copiées de la sortie du moteur ; je ne m'inspire pas des Fakes ; tu me signales chaque capture d'écran 📸 de la section 11 au moment où elle doit être prise, avec le nom de fichier.
```

## 13. Dépendances et synchronisation

- **Tu n'attends personne** : `ScriptedStrategy`, `SyntheticPrices` et les `Fakes` suffisent pour tout le socle et la heatmap.
- **Les autres t'attendent** : la Partie 4 bascule `UseFakes:Engine` à `false` après ta PR 3 (puis profite automatiquement des PR 4 et 5). Le Monte Carlo (Partie 2) et le PDF (Partie 1) consomment tes `BacktestResult`. **Préviens l'équipe à chaque merge.**
- Quand les stratégies réelles de la Partie 2 sont mergées, lance un backtest de chaque stratégie avec les défauts sur `SPY` et signale toute exception par une issue.
