# Part 4 - Web application

## Objective

Part 4 is the ASP.NET Core MVC application in `src/QuantBacktest.Web`. A user configures a backtest and reads its
result: permanent link, charts, indicators and trade log. The other pages are the strategy catalog and the no-code
rule builder, the comparator, the optimisation heatmap, the train/test validation, the Monte Carlo simulation, the
data and quota pages, and the PDF and CSV downloads.

The web project is the **composition root**. It plugs in the services of every part through the `Core` interfaces,
and it contains **no financial logic**. Its only calculations are:

- presentation (fraction to percentage, sorting, colours);
- the trade markers, taken from `Trades`;
- the train/test split of rules T1 and T2;
- the conversion of URL percentages to fractions.

| Route | Page |
|---|---|
| `GET /Backtest`, `GET /Backtest/Result?…` | Backtest form and result (permanent link) |
| `GET /Backtest/Report.pdf?…`, `GET /Backtest/Trades.csv?…` | Downloads (`&mc=true` adds the Monte Carlo section) |
| `GET /Backtest/MonteCarlo?…` | Monte Carlo simulation |
| `GET /Strategies`, `/Strategies/Custom/New`, `/Strategies/Custom/{id}/Edit` | Catalog and rule builder (`POST` Save and Delete) |
| `GET /Compare`, `GET /Compare/Result?…` | Comparator |
| `GET /Optimize`, `GET /Optimize/Result?…` | Optimisation heatmap |
| `GET /Validate`, `GET /Validate/Result?…` | Train/test validation |
| `GET /Data`, `GET /Data/Quality?…`, `POST /Data/Refresh` | Cached data, quality report, refresh |
| `GET /api/strategies` | JSON for the dynamic form |
| `GET /Home/Error` | Generic error page |

## Design choices

- **One place knows the URL.** Only `BacktestQuery` reads and writes the URL parameters (P4-U16). It reads them
  tolerantly with the invariant culture and reports an error per field instead of throwing. It writes one canonical
  URL, and a page that receives another form of it redirects there once (#22). The comparator, heatmap, train/test
  and Monte Carlo pages add their own parameters to the same model.
- **View models carry the formatting.** Controllers are thin. View models format every value through
  `DisplayFormat`: en-US only, `$10,371.39`, two decimals, `n/a` for `null` (P4-U14). It uses explicit invariant
  patterns (#20). Razor views only print the result.
- **Nothing hard-coded about strategies.** Forms, lists, catalog cards, heatmap axes and `/api/strategies` all come
  from `IStrategyCatalog`. A new or no-code strategy appears without any change in the web project.
- **Only the contract exceptions are caught.** `BacktestValidationException` and `MarketDataException` become an
  alert above the form, with the input kept and the `RateLimitExceeded` or `MissingApiKey` hint (P4-U12). Any other
  exception reaches `/Home/Error`, which shows no stack trace outside Development.
- **JavaScript modules.** The server renders forms that work without JavaScript; the modules only refine them, using
  `data-*` attributes and JSON blocks. `wwwroot/js/` holds thirteen modules: the five listed in P4-U16, plus
  `compare-charts.js`, `compare-form.js`, `confirm.js`, `monte-carlo-charts.js`, `optimize-form.js`,
  `refresh-form.js`, `validate-charts.js` and `validate-form.js`. `confirm.js` is a generic confirmation dialog
  loaded by the layout (#31).
- **Local libraries.** There is no CDN (P4-U15). The files come from the official npm tarballs, with their MIT
  licences (#20): **Bootstrap 5.3.8**, **Chart.js 4.5.1** (UMD build only) and **chartjs-chart-matrix 3.1.0**.
- **Colours.** Green and red mean gains and losses only. Exit-reason badges and comparator series use other hues
  (#23, #35). PART-4 imposes two exceptions: the green and red trade markers (P4-U5) and the red isolated-peak
  banner (P4-X2). The heatmap colours cells by rank, because a linear scale was unreadable on real grids (#36).
- **Real services or fakes.** A single `FakeServicesStatus` reads the five `QuantBacktest:UseFakes:*` keys. It
  decides both which services are registered and whether the "Demo data" banner shows (#20). All five keys are now
  `false` (#39).
- **Web constants.** `MinComparedStrategies` (2, C1) and `MinMonteCarloSimulations` (100, MC1) appear in the
  contract text but not in `Conventions`. The web checks them before calling a service (#33, #35).

## How it works

A user fills in `/Backtest`: symbol (with the cached symbols suggested), timeframe, optional period, strategy and its
generated parameters, and execution settings. The browser checks the form, sends it as a `GET` to
`/Backtest/Result`, and the server checks it again.

The result page shows:

- a header with the actual period and the unadjusted-prices banner;
- the Strategy vs Buy & hold table of the 13 metrics;
- four charts: equity (linear or log scale), drawdowns, price with indicators and trade markers, and oscillators;
- the sortable trade log.

From there the user can copy the link, download the PDF or CSV, run Monte Carlo, optimise the strategy (2 parameters
or more) or edit the settings. On the heatmap, a click on a cell opens its result, and the permanent note leads to
`/Validate`.

| Id | Status | PR |
|---|---|---|
| P4-U1 Backtest form | Done with deviation (daily-only options not disabled) | #22 |
| P4-U2 Loading state, single submission | Done | #22 |
| P4-U3 Result header | Done | #23 |
| P4-U4 Metrics table | Done | #23 |
| P4-U5 Charts | Done | #30 |
| P4-U6 Trade log | Done | #23, #30 |
| P4-U7 Result actions | Done (each button enabled by the PR of its page) | #23, #33, #34, #36 |
| P4-U8 Catalog | Done | #31 |
| P4-U9 Rule builder | Done | #32 |
| P4-U10 Data and quality pages | Done | #38 |
| P4-U11 Quota badge | Done | #20 |
| P4-U12 Errors | Done with deviation (`user-secrets` command shown in every environment) | #20, #22 |
| P4-U13 Layout, 360 to 1,920 px | Done | #20, #30, #40 |
| P4-U14 Culture and formats | Done | #20 |
| P4-U15 Local libraries | Done | #20 |
| P4-U16 Code organisation | Done with deviation (thirteen modules instead of five) | #21 and later |
| P4-X1 Comparator | Done | #35 |
| P4-X2 Heatmap | Done | #36 |
| P4-X3 Train/test | Done with deviation (one extra backtest on the training period) | #37 |
| P4-X4 Monte Carlo | Done | #33, #34 |
| P4-X5 Quality and quota with real services | Done | #38, #39 |
| P4-X6 Indicators and cards on real strategies | Done (checked by hand) | #39 |
| P4-I1 Switch to real services | Done with deviation (one PR for the five switches) | #39 |
| P4-I2 No workaround of the contract | Done: no "Contract change" issue was needed | - |
| P4-V1 Video | Reassigned: editing reassigned to Alexandre, team decision of 30 October | - |

P4-X6 was checked by hand on the 11 real strategies and on the no-code strategy "RSI dip": price and indicator
chart, oscillator chart and catalog card.

## Tests

The solution has **1,494** tests, all green, and `dotnet build -warnaserror` passes. By project: Web 923, Engine 184,
Strategies 168, Data 124, Reports 55, Analytics 14, Fakes 14, Core 12.

**Method.**

- The Web tests create the controllers and view components directly, with the Fakes and with test doubles. There is
  no network and no `Mvc.Testing`, so Razor views are never rendered.
- Each test names the rule it checks. The tests required by PART-4 section 7 are all present.
- Tests check that an unexpected exception is not swallowed in seven controllers: backtest result, comparator,
  heatmap, train/test, Monte Carlo, downloads and data pages.

**Manual checks.** JavaScript and CSS have no automated tests. Every PR carries a browser checklist, run with the
Fakes and in Production (for example F1 to F17 and P1 to P11 in #33). #37 compared the rendered HTML of existing
pages between `main` and the branch with `fc.exe`.

For each switch to a real service, #39 recorded a check that the fake could not pass:

- Data: `/Data` lists IBM with origin API.
- Reports: the PDF is the multi-page report, not the one-page placeholder.
- Strategies: the catalog shows the 11 built-in strategies and "RSI dip".
- Analytics: seeds 42 and 43 give different percentiles.
- Engine: a full real backtest matching the Production figures (12 trades, +14.22 %).

**Measurements** (Production, warm, from the PR descriptions):

| Request | Time | Size | PR |
|---|---|---|---|
| PDF, AAPL weekly `consecutive-down-days` (64 trades) | ≈ 0.17 s | - | #34 |
| Same PDF with Monte Carlo, 10,000 simulations | ≈ 0.23 s | - | #34 |
| Heatmap, `rsi-reversion`, 625 cells | 0.70 s | 765 KB | #36 |
| Heatmap, `sma-crossover`, default ranges (552 cells) | 1.37 s | 603 KB | #36 |
| Train/test, 625-cell grid | 1.05 s | 609 KB | #37 |
| Train/test, default grid | 0.93 s | 554 KB | #37 |
| `GET /Data` (10 rows) | 0.033 s | 11.6 KB | #38 |
| `GET /Data/Quality`, JPM weekly | 0.009 s | 8.3 KB | #38 |

## Screenshots

Images go in `docs/report/images/`.

| File | Caption |
|---|---|
| `part4-01-layout-fakes.png` | Layout with navigation, quota badge and the yellow "Demo data" banner (fake services, PR 1). |
| `part4-02-form-strategy-a.png` | Backtest form with a fake strategy (Threshold on past return) and its two generated parameters (fake services). |
| `part4-03-form-strategy-b.png` | Same form after switching to another strategy (Alternating trades, daily only): the parameter fields changed. |
| `part4-04-form-validation.png` | Server-side validation: an initial capital of -5 is rejected ("Initial capital must be greater than 0."), input kept. |
| `part4-05-error-limit.png` | Symbol LIMIT (fake data): rate-limit alert with the "Try a symbol that is already cached, or retry tomorrow." hint, input kept. |
| `part4-06-results-metrics.png` | Result page with fake services: header, actions and Strategy vs Buy & hold table (AAPL daily, Alternating trades, 2 % stop-loss). |
| `part4-07-results-charts.png` | Charts for Bollinger + RSI double confirmation on AAPL weekly: equity, drawdowns, price with Bollinger bands and trade markers, RSI oscillator. |
| `part4-08-results-trades.png` | Trades table sorted by exit reason, with Signal, Stop-loss and End of data badges (fake services). |
| `part4-09-permalink.png` | Full permalink in the address bar and the "Copy link" button (fake services). |
| `part4-10-catalog.png` | Strategy catalog: one card per strategy with documentation, timeframes and parameter table. |
| `part4-11-rule-builder.png` | No-code rule builder editing the custom strategy "RSI dip": two entry conditions, one exit condition. |
| `part4-12-data-quality.png` | Data page (Production): cached series with origin, adjustment and anomaly counts, IBM weekly "not cached", quota line and refresh form. |
| `part4-12-data-quality-detail.png` | Quality report for JPM weekly: all DataQualityReport fields and 6 anomalies (1 gap, 5 price jumps). |
| `part4-13-compare.png` | Comparator on AAPL weekly since 2015 with three strategies and buy & hold (dashed). |
| `part4-14-heatmap.png` | RSI mean reversion heatmap (profit factor) with the best cell outlined, the Isolated peak banner and the permanent note. |
| `part4-15-train-test.png` | Train/test validation of the moving average crossover: split, best parameters, train vs test table and the two equity curves. |
| `part4-16-monte-carlo.png` | Monte Carlo (1,000 simulations, seed 42): summary, percentiles and the two histograms with the actual result highlighted. |
| `part4-17-real-services.png` | Real backtest with all real services and no demo banner (AAPL weekly, moving average crossover with stop-loss, take-profit and trailing stop). |
| `part4-18-mobile.png` | Result page at 360 px width. |
| `part4-19-pr-1.png` | Checks tab of PR #20 (§9 row 1): build-and-test and pr-rules green. |
| `part4-19-pr-2a.png` | Checks tab of PR #21 (§9 row 2a): build-and-test and pr-rules green. |
| `part4-19-pr-2b.png` | Checks tab of PR #22 (§9 row 2b): build-and-test and pr-rules green. |
| `part4-19-pr-3a.png` | Checks tab of PR #23 (§9 row 3a): build-and-test and pr-rules green. |
| `part4-19-pr-3b.png` | Checks tab of PR #30 (§9 row 3b): build-and-test and pr-rules green. |
| `part4-19-pr-4a.png` | Checks tab of PR #31 (§9 row 4a): build-and-test and pr-rules green. |
| `part4-19-pr-4b.png` | Checks tab of PR #32 (§9 row 4b): build-and-test and pr-rules green. |
| `part4-19-pr-5.png` | Checks tab of PR #38 (§9 row 5): build-and-test and pr-rules green. |
| `part4-19-pr-6.png` | Checks tab of PR #34 (§9 row 6): build-and-test and pr-rules green. |
| `part4-19-pr-7.png` | Checks tab of PR #35 (§9 row 7): build-and-test and pr-rules green. |
| `part4-19-pr-8.png` | Checks tab of PR #36 (§9 row 8): build-and-test and pr-rules green. |
| `part4-19-pr-9.png` | Checks tab of PR #37 (§9 row 9): build-and-test and pr-rules green. |
| `part4-19-pr-10.png` | Checks tab of PR #33 (§9 row 10): build-and-test and pr-rules green. |
| `part4-19-pr-11-15.png` | Checks tab of PR #39 (§9 row 11+): build-and-test and pr-rules green. |
| `part4-19-pr-fix.png` | Checks tab of PR #40 (outside §9): build-and-test and pr-rules green. |

The `part4-19` files are numbered by §9 row, not by GitHub number. `part4-19-pr-fin.png` is taken after this report
is merged. No PR shows an approval, because the repository requires none (`docs/GIT_WORKFLOW.md`).

## Limitations

### Deviations from the plan

- **Split PRs:** PR 2 became #21 and #22, PR 3 became #23 and #30, PR 4 became #31 (catalog) and #32 (rule builder).
- **Branches and titles:** #21, #23, #30, #31, #32, #33 and #39 have a branch or a title other than in PART-4 §9.
- **Order:** PRs 10, 6, 7, 8 and 9 (#33 to #37) were merged before PR 5 (#38), against §6. The data pages came last,
  to be checked against the real Data service and API quota. The advanced features do not depend on them: they go
  through the interfaces, with the Fakes first.
- **P4-I1, one PR:** the five switches are the five commits of #39 (messages of §9, listed in the squash body). They
  were grouped because the all-real combination had run in Production since 29 October.
- **P4-I1, a real backtest after each switch:** this was impossible before the Engine switch, while the engine was
  still the fake one. Each state was checked by a test the fake could not pass. After the Engine switch, a full real
  backtest was compared with the Production figures.
- **PR outside the plan:** #40 fixes the Grid card of `/Optimize/Result` at 360 px. The defect had been present since
  the Optimize page (#36).
- **P4-U1:** daily-only strategies are disabled in weekly only on `/Compare`. On `/Backtest`, `/Optimize` and
  `/Validate` they are labelled "(daily only)" but stay enabled; on `/Backtest` the label also shows in daily. The
  reason: a browser does not send a disabled selected option, so a permanent link would lose the strategy.
  `setCustomValidity` blocks the submission and the server refuses it. Disabling only the non-selected options was
  not done, for lack of time.
- **§7 test:** on `/Backtest` the test checks the "(daily only)" label, not a disabled state. The `/Compare` test
  does check the disabled state.
- **P4-U16:** there are thirteen modules instead of five, and `confirm.js` is loaded by the layout.
- **`data-restriction=""`:** Razor renders `data-*` attributes even when the value is null (#32). Options without a
  restriction on `/Backtest` therefore carry an empty attribute, which the script never reads.
- **P4-X3 and T3:** the page runs one extra backtest on the training period. Without a contract change, this is the
  only way to show the train metrics and the train curve. It gives exactly `Best.Value` (#37).
- **P4-V1:** video editing was reassigned to Alexandre, team decision of 30 October.
- **Screenshot 12:** two files instead of one.
- **Screenshots 19:** no approval is visible, since none is required.
- **Merge of #20:** merged by VictorHaegeman, not by its author.
- **`MissingApiKey` hint:** the `dotnet user-secrets set …` command of P4-U12 is shown in every environment. In
  Production or Docker the key goes in the `AlphaVantage__ApiKey` environment variable instead.
- **Out-of-date text:** `CLAUDE.md` and CONTRACT §13 (line 230) still say the fakes are active in Development, which
  has been false since #39. These files do not belong to Part 4.

### Known limitations

- **Price chart:** linear scale only (only the equity chart has a log scale). Observed by hand on AAPL weekly
  1999-2026: the years before about 2012 are squashed near zero and the trade markers overlap.
- **Oscillator chart:** a single Y axis, so a rule mixing RSI (0 to 100) and ROC (a fraction) flattens one curve
  (#30).
- **Comparator:** linear scale, default parameters only, strategies run one after the other. Results with different
  dates throw an `InvalidOperationException` (error page); the dates are not merged (#35).
- **Heatmap colour:** shows the rank of a cell, not the size of the gap (#36).
- **Best cell:** not filtered on the number of trades (O3), so it can rest on fewer than 5 trades. It is optimistic
  by construction.
- **Train/test:** a single split and no degradation threshold. The test period has few trades (6 in screenshot 15),
  and a 100-bar daily seed leaves only 70 training and 30 test bars.
- **Monte Carlo:** resamples the trades of a single backtest, with at least 5 trades (MC1). A seed and its opposite
  give the same result, because Analytics uses `new Random(Seed)`. Equal-width bins crowd most simulations into one
  or two bars (#33).
- **Rule builder:** an edited custom strategy keeps its id, so its permanent links run the new rules (41 trades
  became 79 on AAPL weekly, #32). An invalid stored definition is hidden from the catalog (P2-C1), but it can still
  be opened, fixed or deleted from its Edit page.
- **Trades CSV:** comma separator and decimal point (CONTRACT section 11), so Excel in French puts every value in
  column A (#34). `quantity`, `fees` and `net_profit` are written with full decimal precision.
- **360 px:** cramped charts and no sticky first column. Observed by hand: an empty space under "Train vs test" on
  `/Validate`, and the Bars and First date columns stuck together on `/Data`. The Monte Carlo summary uses
  `text-nowrap`.
- **992 to 1,035 px (estimated, not measured):** a long value can overflow its column by up to 11 px, inside the
  card margin.
- **Tests:** no `Mvc.Testing` and no rendered Razor views; JavaScript and CSS are covered by manual checklists only.

### Part 1 limitations seen from the web

- Refreshing a symbol before its seed file is imported bypasses the seed for good (D2, D3) (#38).
- Refreshing a weekly series merges unadjusted API bars into the adjusted seed, so the whole series becomes
  unadjusted (D6) (#38).
- The daily seeds have only 100 bars and are not adjusted (`data/seed/README.md`).

### Methodological limitations

The demonstration symbols were chosen with hindsight (selection and survivorship bias). On AAPL weekly since 2015,
buy & hold returns 1,261.88 %, against 149.16 % (`momentum-roc`, 31 trades), 273.89 % (`donchian-breakout`, 12) and
288.68 % (`macd-crossover`, 26) in screenshot 13. Over the whole weekly history, buy & hold returns 52,117.12 %,
against +14.22 % for the protected moving average crossover of screenshot 17. On such a symbol, buy & hold is hard
to beat, which says more about the symbol than about the strategies.

---

# Final report, section 7 - Web application

**Pages.** The navigation has six entries: Backtest, Compare, Optimize, Validate, Strategies and Data. Every page
shows a quota badge (calls used today out of 25) and the footer "Market data: Alpha Vantage". The Backtest page leads
to the result, which leads to Monte Carlo and the PDF and CSV downloads. The catalog has one card per strategy (idea,
entry and exit rules, risks, parameters) with a "Use this strategy" button. Custom strategies are created and edited
in the no-code rule builder: two rules, each with 1 to 5 conditions. The Data page lists the cached series with
their quality summary. It can refresh one series, after a confirmation that gives the number of API calls left.

**User journey.** A typical session starts on `/Backtest`. The user chooses AAPL weekly and the moving average
crossover, then reads the result against buy & hold. "Optimize this strategy" opens a heatmap of two parameters. The
note under the heatmap leads to `/Validate`, which checks the best cell on unseen data. "Run Monte Carlo" shows how
much of the result depends on the order of the trades.

**Comparator.** `/Compare` runs 2 to 5 strategies with their default parameters on the same symbol, period and
settings. It draws one equity curve per strategy, plus the buy & hold benchmark (dashed). It also shows a sortable
table of the main metrics, with a link to each detailed result. A strategy that fails shows its message without
blocking the others.

**Permanent links.** Every result has a `GET` URL that holds every setting (`symbol`, `tf`, `strategy`, `from`,
`to`, `capital`, `fee`, `slip`, `sl`, `tp`, `ts`, `rf`, `p.<key>`). Percentages stay in the URL; the server converts
them to fractions. `BacktestQuery` writes one canonical form and redirects to it once, so two equivalent links
become the same link. Copying the URL gives the same result as long as the cached data is not refreshed.

# Final report, section 6 - Robustness analysis (web part)

**Train/test.** `/Validate` applies rules T1 to T3. The first `floor(N × train)` bars of the selected period form
the training period, with `train` between 50 % and 90 % (default 70 %); the remaining bars form the test period. The
page runs the heatmap on the training period only, takes its best cell, and backtests those parameters on the test
period with the same execution settings. The test backtest may use earlier bars to warm up its indicators (T2).

In screenshot 15 (AAPL weekly since 2010, moving average crossover, Sharpe ratio, 70 % train), the 876 bars split
into 613 training bars (2010-01-08 to 2021-10-01) and 263 test bars (2021-10-08 to 2026-10-14). The best parameters
are fast 26 and slow 35. The Sharpe ratio falls from 1.02 in training (5 trades) to 0.75 in test (6 trades). With so
few trades, neither figure is statistically solid.

**Monte Carlo.** `/Backtest/MonteCarlo?…&sims=1000&seed=42` reruns the backtest of the permanent link and calls
`IMonteCarloService` (P4-X4). The page shows:

- the probability of loss and the actual result;
- the percentile table;
- two histograms (final return, trade-by-trade max drawdown), each with the range of the actual result highlighted;
- the MC7 warning that this drawdown cannot be compared with the result page's.

"Download PDF with Monte Carlo" adds `mc=true` to the PDF URL. With fewer than 5 trades, the page shows the
service's message. The resampling method belongs to Part 2.

# Final report, section 10 - Limitations and future work (Part 4)

The web application only displays what the services return, so its limits concern display and method. Only the
equity chart has a log scale, and the comparator uses default parameters. The heatmap colours by rank, and its best
cell can rest on few trades. Train/test uses one split, and Monte Carlo resamples the trades of a single backtest.
Small screens are usable but cramped. Views, JavaScript and CSS are checked by hand rather than by automated tests.
The demonstration symbols were chosen with hindsight, so buy & hold is hard to beat on them.

Future work follows from these limits. The price chart and the comparator could get a log scale, and tables that
scroll could keep their first column fixed. On `/Backtest`, `/Optimize` and `/Validate`, the daily-only options that
are not selected could be disabled. Finally, the trades CSV could be made readable by Excel with a French locale.
