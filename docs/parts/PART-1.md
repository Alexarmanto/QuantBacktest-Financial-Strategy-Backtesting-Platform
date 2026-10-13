# PARTIE 1 — Données, stockage, rapports et Docker

> Cette notice est ta spécification. Elle prime sur toute supposition. En cas de doute : demande à l'humain, ne devine pas.
> Contrat commun : `docs/CONTRACT.md` (règles citées par leur identifiant, par exemple D4). Règles Git : `docs/GIT_WORKFLOW.md`. Règles de code : `CLAUDE.md`.

## 1. Identité

| | |
|---|---|
| Responsable | Alexandre Tung (@Alexarmanto), aussi propriétaire du dépôt |
| Projets | `src/QuantBacktest.Data`, `src/QuantBacktest.Reports` et leurs projets de tests |
| Préfixe de branche | `part1/` |
| Label GitHub | `part-1` |
| Scopes de commit | `data`, `reports`, `docker`, `docs` |

## 2. Mission

Fournir des séries de prix propres et reproductibles (API Alpha Vantage gratuite, fichiers de départ, cache SQLite) derrière `IMarketDataService`, persister les stratégies sans code derrière `ICustomStrategyStore`, produire les rapports PDF et CSV derrière `IReportGenerator`, et rendre l'application lançable avec `docker compose up`. Tu es **le seul** à parler à Alpha Vantage et **le seul** à écrire dans la base.

## 3. Périmètre

### Fichiers que tu possèdes

- `src/QuantBacktest.Data/**`, `tests/QuantBacktest.Data.Tests/**`
- `src/QuantBacktest.Reports/**`, `tests/QuantBacktest.Reports.Tests/**`
- `data/seed/**`
- `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `.env.example` (racine)
- `docs/report/part-1.md`, `docs/report/images/part1-*`
- `Directory.Packages.props` : **ajout** des paquets autorisés en section 8 uniquement

### Interdit de modifier

Tout le reste, dont `src/QuantBacktest.Core`, `src/QuantBacktest.Fakes`, `docs/CONTRACT.md`, `docs/parts/`, les projets des autres parties, `.github/`, `scripts/`, `CLAUDE.md`. En tant que propriétaire, tu ne modifies les fichiers d'infrastructure que par une branche `infra/`, jamais dans une branche `part1/`.

### Frontières avec les autres parties (conflits réglés d'avance)

- **Aucune métrique n'est recalculée dans le PDF.** Il affiche `BacktestResult.Metrics`, `BenchmarkMetrics`, `Trades`, `Equity`, `Indicators` et, si fourni, `MonteCarloResult`, tels quels. Un chiffre faux est un bug de la Partie 3 (ou 2 pour le Monte Carlo) : ouvre une issue `bug`.
- **Le stockage des stratégies sans code ne valide rien.** La validation des règles (R1 à R8) appartient à la Partie 2 (`ICustomStrategyFactory`) et elle est appelée par la Partie 4 **avant** `SaveAsync`. Toi, tu stockes et relis fidèlement.
- **Personne d'autre ne touche à la base ni à l'API.** Tu n'exposes ni `DbContext`, ni entité, ni client HTTP : uniquement les interfaces de `Core`.
- **Les formats d'échange sont figés** (`CONTRACT.md` section 11) : colonnes du CSV, noms des fichiers téléchargés. La Partie 4 compte dessus à l'identique.
- **Docker** : tu écris le `Dockerfile` et le `docker-compose.yml`, mais tu ne modifies pas le code de `Web`. Les réglages passent par variables d'environnement (`QuantBacktest__DatabasePath`, etc.).

## 4. Contrat

### Ce que tu exposes

| Interface (`Core`) | Ton implémentation (`internal`) | Enregistrement |
|---|---|---|
| `IMarketDataService` | `MarketDataService` | `AddQuantBacktestData(IConfiguration)` dans `QuantBacktest.Data` |
| `ICustomStrategyStore` | `SqliteCustomStrategyStore` | idem |
| `IReportGenerator` | `PdfReportGenerator` | `AddQuantBacktestReports()` dans `QuantBacktest.Reports` |

Les méthodes d'enregistrement existent déjà avec un corps vide : tu remplis le corps, **tu ne changes pas la signature**. Tout en `Singleton` (CONTRACT section 2).

### Ce que tu consommes

Uniquement `Core`. Pour tester les rapports, fabrique des `BacktestResult` avec les `Fakes` (`FakeBacktestService`, `FakeMonteCarloService`) ou à la main.

## 5. Spécification du socle

### Données

| Id | Exigence |
|---|---|
| P1-D1 | **Options.** `AlphaVantageOptions` (section `AlphaVantage` : `ApiKey`, `BaseUrl`, `DailyRequestLimit`) et `DataOptions` (section `QuantBacktest` : `DatabasePath`, `SeedDirectory`). Chemins relatifs résolus depuis `IHostEnvironment.ContentRootPath`, chemins absolus acceptés tels quels. Dossier de la base créé s'il manque. |
| P1-D2 | **Client HTTP.** `HttpClient` typé (`AddHttpClient`), timeout 30 s, **`.RemoveAllLoggers()`** sur le builder (sinon .NET journalise l'URL avec la clé). Clé absente : `MissingApiKey` **avant** tout appel (D4). Erreur réseau, timeout ou statut non 200 : `NetworkError`, message sans URL. |
| P1-D3 | **Parseur CSV** (D5). En-tête exact sinon `InvalidResponse` ; tolère `\r\n` ; `InvariantCulture` partout ; ligne invalide (champ manquant, prix <= 0, `High < Low`) ignorée avec un warning journalisé ; aucune ligne valide : `InvalidResponse` ; sortie triée croissante, sans doublon. |
| P1-D4 | **Erreurs JSON en HTTP 200** : exactement le tableau de D4. Message affichable, sans clé. |
| P1-D5 | **Spike du premier jour (avant tout le reste).** Avec ta clé, teste et note (accepté ou refusé, nombre de barres) : `TIME_SERIES_DAILY` compact, `TIME_SERIES_DAILY` full, `TIME_SERIES_DAILY_ADJUSTED`, `TIME_SERIES_WEEKLY`, `TIME_SERIES_WEEKLY_ADJUSTED`. Puis choisis la **source des longues historiques daily** pour `data/seed/` : une source publique gratuite convertie au format CSV d'Alpha Vantage, après avoir vérifié qu'elle autorise cet usage. Si aucune ne convient, le daily reste limité aux 100 barres de l'API et les démonstrations longues se font en weekly. Consigne le tableau et la décision dans `docs/report/part-1.md` (Design choices) et dans ta première PR. **Les interfaces ne changent dans aucun cas.** 📸 |
| P1-D6 | **Endpoints utilisés** (D1) : daily = `TIME_SERIES_DAILY&outputsize=compact` ; weekly = `TIME_SERIES_WEEKLY_ADJUSTED` si le spike prouve qu'il est gratuit, sinon `TIME_SERIES_WEEKLY`. Pas de rééchantillonnage daily → weekly. URL : `?function=<F>&symbol=<S>&datatype=csv&apikey=<K>` (+ `outputsize` en daily). |
| P1-D7 | **Ajustement** (D6). Source ajustée : facteur `adjusted_close / close` appliqué à `open`, `high`, `low`, `close` (volume brut). Une série mélangeant barres ajustées et non ajustées a `IsAdjusted = false`. |
| P1-D8 | **Stockage.** EF Core + SQLite, `EnsureCreated()` une seule fois (thread-safe), pas de migrations. Tables : `PriceBars` (clé `Symbol`, `Timeframe`, `Date` ; OHLC `decimal`, `Volume` `long`, `IsAdjusted`), `SeriesMetadata` (clé `Symbol`, `Timeframe` ; `Origin`, `LastFetchedUtc`), `ApiUsage` (clé `DateUtc` ; `Count`), `CustomStrategies` (clé `Id` ; `Name`, `Json`, `UpdatedUtc`). Le provider SQLite ne trie ni n'agrège les `decimal` : ne trier que par `Date`, calculer en mémoire. |
| P1-D9 | **`GetSeriesAsync`** : exactement D2 et D3. Normalisation du symbole puis contrôle `Conventions.SymbolPattern` (sinon `SymbolNotFound`). Import d'un fichier de départ : `Origin = Seed` ; ajout de barres API à une série seed : `Origin = Mixed`. |
| P1-D10 | **Quota** (D4) : `ApiUsage` compte chaque requête HTTP réellement émise, jour UTC, horloge = `TimeProvider` injecté. |
| P1-D11 | **Concurrence** : un verrou par couple (symbole, timeframe) ; 10 appels simultanés = 1 requête HTTP. |
| P1-D12 | **`GetCachedSymbolsAsync`** : symboles distincts en base, triés. |
| P1-D13 | **Fichiers de départ** : `data/seed/<SYMBOL>_daily.csv` et `_weekly.csv` pour `AAPL`, `MSFT`, `SPY`, `JPM` (D1), au format CSV d'Alpha Vantage, commités. |
| P1-D14 | **`SqliteCustomStrategyStore`** : la définition est sérialisée en JSON (`System.Text.Json`, camelCase, énumérations en texte via `JsonStringEnumConverter`) ; relecture **identique** (égalité de record, listes comprises) ; `GetAllAsync` trié par `Name` (ordinal, sans casse) ; `SaveAsync` insère ou remplace ; `DeleteAsync` retourne `false` si l'id est inconnu. Aucune validation des règles. |
| P1-D15 | **Qualité des données** (fonctionnalité n°8) : `GetQualityReportAsync` exactement selon D7, uniquement sur le cache (jamais d'appel API), `NotCached` sinon. |
| P1-D16 | **Quota** (fonctionnalité n°8) : `GetApiQuotaAsync` selon D8, mêmes données que P1-D10. |

> Les méthodes P1-D15 et P1-D16 font partie de l'interface `IMarketDataService` : elles sont dans le socle pour que la Partie 4 puisse basculer sur tes vrais services sans page cassée.

### Rapports

| Id | Exigence |
|---|---|
| P1-R1 | **PDF** avec QuestPDF (`QuestPDF.Settings.License = LicenseType.Community`, défini une fois dans `AddQuantBacktestReports`), graphiques en PNG avec ScottPlot. A4 portrait. Anglais. Culture d'affichage `en-US` fixée explicitement. |
| P1-R2 | **Page 1** : titre `QuantBacktest report`, symbole, stratégie (nom et id), paramètres (clé = valeur), période réelle (première et dernière date de `Bars`), timeframe, capital, frais, slippage, stops actifs, taux sans risque ; avertissement « Prices are not adjusted for splits or dividends. » si `PricesAdjusted` est faux ; tableau **Strategy vs Buy & hold** des 13 champs de `PerformanceMetrics` (`null` affiché « n/a ») ; courbe d'équité stratégie vs benchmark. |
| P1-R3 | **Page 2** : drawdown de la stratégie ; cours de clôture avec les indicateurs `Pane = Price` et des triangles d'achat (haut) et de vente (bas) aux dates et prix des trades. |
| P1-R4 | **Journal des trades** : toutes les colonnes de `Trade` (raison de sortie comprise), lignes alternées, en-tête répété à chaque page, **aucune troncature** ; zéro trade : « No trades were executed in this period. » |
| P1-R5 | **Pied de page** : « Generated on <date UTC> by QuantBacktest — Market data: Alpha Vantage », numéro de page ; date via `TimeProvider`. |
| P1-R6 | **Formats** : pourcentages à 2 décimales avec signe (`+12.34 %`), montants avec séparateur de milliers et 2 décimales, ratios à 2 décimales. |
| P1-R7 | **CSV des trades** : exactement CONTRACT section 11 ; échappement RFC 4180 si un champ contenait une virgule. |
| P1-R8 | **Robustesse** : 0 trade, 1 000 trades, 2 barres, indicateurs vides : jamais d'exception. Si ScottPlot échoue sous Linux (CI ou Docker) faute de bibliothèques natives, ajoute `SkiaSharp.NativeAssets.Linux.NoDependencies` à `QuantBacktest.Reports`. |

## 6. Spécification des fonctionnalités avancées

À commencer **après** le merge du socle (section 9, PR 1 à 7).

| Id | Fonctionnalité | Exigence |
|---|---|---|
| P1-X1 | Rapport enrichi (n°1, 5, 6, 7) | Si `monteCarlo` est fourni : page « Monte Carlo analysis » avec les deux histogrammes, le tableau des percentiles (P5 à P95 des rendements finaux et des drawdowns), la probabilité de perte, les valeurs réelles, et la phrase « Trade-level drawdowns are not comparable with the bar-level maximum drawdown. » (MC7). Pour une stratégie sans code, imprimer ses règles (`Documentation.EntryRule` et `ExitRule`). Indicateurs `Pane = Oscillator` sur un graphique sous le cours. |
| P1-X2 | Docker (n°10) | `Dockerfile` multi-étapes (`mcr.microsoft.com/dotnet/sdk:10.0` pour compiler et lancer les tests, `mcr.microsoft.com/dotnet/aspnet:10.0` pour l'exécution, paquets de polices nécessaires au PDF installés). `docker-compose.yml` : service `web`, port `8080:8080`, volume nommé pour la base, `data/seed` copié dans l'image, variables `ASPNETCORE_ENVIRONMENT=Production`, `QuantBacktest__DatabasePath=/data/quantbacktest.db`, `QuantBacktest__SeedDirectory=/app/seed`, `AlphaVantage__ApiKey=${ALPHAVANTAGE_API_KEY}` lue depuis un `.env` local. `.env.example` (commité) contient `ALPHAVANTAGE_API_KEY=` vide. `docker compose up --build` doit donner une application fonctionnelle sur `http://localhost:8080`, sans fakes. |

## 7. Tests obligatoires

Aucun test n'utilise le réseau ni l'horloge réelle : HTTP via un `HttpMessageHandler` factice qui **compte les appels**, base dans un fichier SQLite temporaire supprimé en fin de test, `FakeTimeProvider` ou équivalent écrit à la main.

- **Parseur (P1-D3)** : CSV décroissant → croissant ; `\r\n` ; ligne invalide ignorée ; en-tête invalide ; **test sous `fr-FR`** (fixer `CultureInfo.CurrentCulture`) prouvant que `"123.45"` donne `123.45m`.
- **Erreurs (D4)** : un test par cas (`Error Message`, `Note`, `Information` limite, `Information` premium, statut 500, timeout) ; clé absente → `MissingApiKey` et **0 appel** ; le message d'exception et les journaux ne contiennent jamais la clé.
- **Algorithme (D2, D3)** : cache non vide sans rafraîchissement → 0 appel ; cache vide + seed → import, 0 appel ; cache vide sans seed → 1 appel ; `forceRefresh` → 1 appel et fusion sans suppression d'anciennes barres ; échec en `forceRefresh` → exception et cache intact ; `Origin` correct dans chaque cas.
- **Quota (D4, D8)** : au `DailyRequestLimit`, `RateLimitExceeded` sans appel ; une requête qui échoue compte ; remise à zéro le jour UTC suivant (horloge injectée) ; `ResetsAtUtc` correct.
- **Concurrence (P1-D11)** : 10 appels simultanés → 1 appel HTTP.
- **Ajustement (P1-D7)** : facteur appliqué aux 4 prix ; série mixte → `IsAdjusted = false`.
- **Invariants** : toute `PriceSeries` retournée est triée, sans doublon, prix > 0, `High >= max(Open, Close)`, `Low <= min(Open, Close)`.
- **Stockage des stratégies (P1-D14)** : aller-retour de `FakeCustomStrategyStore.Example` identique (égalité de record après relecture d'une **nouvelle** instance du store) ; remplacement ; suppression ; tri.
- **Qualité (D7)** : une série construite à la main contenant un saut de 30 %, un volume nul, 5 clôtures identiques, un trou de 8 jours : les 4 anomalies attendues, aux bonnes dates, et `MissingPeriods`, `LargestGapDays` exacts (valeurs calculées à la main dans un commentaire).
- **Rapports** : le PDF commence par `%PDF` ; pas d'exception avec 0, 1, 1 000 trades, avec et sans Monte Carlo ; le CSV d'un résultat fabriqué à la main est identique caractère pour caractère à la chaîne attendue écrite dans le test ; la date du pied de page vient de l'horloge factice.

## 8. Paquets NuGet autorisés

`Microsoft.EntityFrameworkCore.Sqlite` (déjà déclaré) pour Data ; `QuestPDF` et `ScottPlot` (pas les variantes WinForms/WPF) pour Reports, ajoutés par `dotnet add src/QuantBacktest.Reports package <Nom>` ; `SkiaSharp.NativeAssets.Linux.NoDependencies` seulement si P1-R8 l'exige. Rien d'autre.

## 9. Découpage en PR (dans cet ordre)

| # | Branche | Contenu | Titre de PR |
|---|---|---|---|
| 1 | `part1/feat-alphavantage-client` | Spike P1-D5, options, client, parseur, erreurs JSON + tests | `feat(data): add Alpha Vantage client and CSV parser` |
| 2 | `part1/feat-sqlite-cache` | DbContext, 4 tables, fusion par date + tests | `feat(data): add SQLite price cache` |
| 3 | `part1/feat-market-data-service` | D2/D3, quota, concurrence, ajustement, import seed, DI + fichiers `data/seed/*.csv` | `feat(data): implement market data service` |
| 4 | `part1/feat-custom-strategy-store` | P1-D14 + tests | `feat(data): persist custom strategies` |
| 5 | `part1/feat-data-quality-and-quota` | P1-D15, P1-D16 + tests | `feat(data): add data quality report and API quota` |
| 6 | `part1/feat-pdf-report` | P1-R1, P1-R2, P1-R5, P1-R6, DI des rapports | `feat(reports): generate PDF summary page` |
| 7 | `part1/feat-report-charts-and-csv` | P1-R3, P1-R4, P1-R7, P1-R8 | `feat(reports): add charts, trade log and CSV export` |
| 8 | `part1/feat-report-advanced` | P1-X1 | `feat(reports): add Monte Carlo and rules to the PDF` |
| 9 | `part1/feat-docker` | P1-X2 | `feat(docker): run the application with docker compose` |
| 10 | `part1/docs-part1-report` | `docs/report/part-1.md` complet + captures | `docs(docs): write part 1 report section` |

## 10. Definition of Done

- [ ] P1-D1 à P1-D16, P1-R1 à P1-R8, P1-X1 et P1-X2 implémentés, ou écartés par écrit dans `part-1.md` avec la raison.
- [ ] `AddQuantBacktestData` et `AddQuantBacktestReports` enregistrent les vraies implémentations.
- [ ] Tous les tests de la section 7 passent ; `dotnet build -warnaserror` vert sur toute la solution.
- [ ] Les 8 fichiers de départ sont commités, ou leur absence est justifiée.
- [ ] Aucune clé, aucun `.db`, aucun `.env`, aucun PDF dans l'historique Git ; la clé n'apparaît jamais dans les journaux (vérifié à la main une fois).
- [ ] `docker compose up --build` lance l'application réelle sur une machine propre.
- [ ] `part-1.md` rempli, toutes les captures de la section 11 présentes et légendées.

## 11. Rapport et captures d'écran

Section `docs/report/part-1.md` (titres imposés). Elle couvre aussi les sections 3 et 8 du rapport final (données, rapports et Docker). Captures dans `docs/report/images/`, prises **au moment indiqué** :

| 📸 | Fichier | Quand | Quoi |
|---|---|---|---|
| 0 | `part1-00-bootstrap.png` | Mise en place GitHub, étape A2 | Le terminal avec `Bootstrap OK` |
| 1 | `part1-01-ci-green.png` | Mise en place GitHub, étape A5 | L'onglet Actions avec la CI verte |
| 2 | `part1-02-branch-protection.png` | Mise en place GitHub, étape A6 | La règle de protection de `main` |
| 3 | `part1-03-scope-check.png` | Mise en place GitHub, étape A7 | La PR de test refusée « Out of scope » |
| 4 | `part1-04-spike.png` | Fin du spike P1-D5 | Le résultat de chaque endpoint (accepté, refusé, nombre de barres) |
| 5 | `part1-05-sqlite-tables.png` | Fin de la PR 2 | La base ouverte (extension SQLite de VS Code ou `sqlite3`) : 4 tables et des lignes de `PriceBars` |
| 6 | `part1-06-cache-tests.png` | Fin de la PR 3 | Les tests de l'algorithme D2/D3 et du quota qui passent |
| 7 | `part1-07-no-key-in-logs.png` | Fin de la PR 3 | Le journal de l'application pendant un appel API : la clé n'apparaît pas |
| 8 | `part1-08-pdf-page1.png` | Fin de la PR 7 | Page 1 du PDF |
| 9 | `part1-09-pdf-page2.png` | Fin de la PR 7 | Page 2 du PDF (drawdown, cours, indicateurs, marqueurs) |
| 10 | `part1-10-pdf-trades.png` | Fin de la PR 7 | Une page du journal des trades |
| 11 | `part1-11-quality-tests.png` | Fin de la PR 5 | Le test des 4 anomalies qui passe |
| 12 | `part1-12-pdf-montecarlo.png` | Fin de la PR 8 | La page Monte Carlo du PDF |
| 13 | `part1-13-docker-up.png` | Fin de la PR 9 | Le terminal `docker compose up --build` et l'application ouverte sur le port 8080 |
| 14 | `part1-14-pr-<n>.png` | À chaque PR mergée | La page de la PR : checks verts et approbation |

## 12. Prompt de démarrage

À coller dans Claude Code, ouvert à la racine du dépôt, sur ta branche `part1/...` :

```
Tu es mon assistant de développement sur le projet QuantBacktest. Je suis Alexandre, responsable de la PARTIE 1 (Données, stockage, rapports et Docker).

1. Lis en entier : CLAUDE.md, docs/CONTRACT.md, docs/GIT_WORKFLOW.md et docs/parts/PART-1.md.
2. Résume en 10 lignes maximum : mon périmètre, les fichiers autorisés et interdits, les interfaces à implémenter, les frontières avec les autres parties. Liste les points ambigus ou contradictoires que tu vois, sans les trancher toi-même.
3. N'écris aucun code avant que j'aie validé ce résumé.
4. Propose ensuite le plan détaillé de la PR 1 de la section 9 (fichiers, tests, identifiants de règles couverts) et attends mon accord.

Rappels : je ne modifie que mon périmètre ; le contrat est gelé ; chaque test cite la règle qu'il vérifie ; tu me signales chaque capture d'écran 📸 de la section 11 au moment où elle doit être prise, avec le nom de fichier.
```

## 13. Dépendances et synchronisation

- **Tu n'attends personne** : tout se teste avec les `Fakes` et des données construites à la main.
- **Les autres t'attendent** : la Partie 4 bascule `UseFakes:Data` à `false` après ta PR 5 (interface `IMarketDataService` et store complets), `UseFakes:Reports` après ta PR 7. **Préviens l'équipe dès que ces PR sont mergées** ; vise-les en priorité.
- La Partie 4 appelle `ICustomStrategyFactory.Validate` (Partie 2) avant ton `SaveAsync` : ne duplique pas cette validation.
