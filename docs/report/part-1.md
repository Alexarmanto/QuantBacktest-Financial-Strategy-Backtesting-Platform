# Part 1 - Data layer and reports

## Objective

## Design choices

### Alpha Vantage free plan: endpoint spike (P1-D5)

Tested on 2026-10-15 with a free API key, symbol `MSFT`, `datatype=csv`:

| Endpoint | Result | Bars | First -> last date |
|---|---|---|---|
| `TIME_SERIES_DAILY`, `outputsize=compact` | Accepted | 100 | 2026-05-22 -> 2026-10-14 |
| `TIME_SERIES_DAILY`, `outputsize=full` | Refused (premium feature) | - | - |
| `TIME_SERIES_DAILY_ADJUSTED` | Refused (premium endpoint) | - | - |
| `TIME_SERIES_WEEKLY` | Accepted | 1406 | 1999-11-12 -> 2026-10-14 |
| `TIME_SERIES_WEEKLY_ADJUSTED` | Accepted | 1406 | 1999-11-12 -> 2026-10-14 |

Refusals arrive with HTTP 200 as a JSON `Information` message, even when CSV is requested.

**Decisions**

- Daily data uses `TIME_SERIES_DAILY&outputsize=compact` (raw prices, last 100 bars).
- Weekly data uses `TIME_SERIES_WEEKLY_ADJUSTED`: it is free and gives 27 years of split- and dividend-adjusted prices.
  The adjustment factor `adjusted close / close` is applied to open, high, low and close; the volume stays raw (P1-D7).
  Adjusted prices are rounded to 6 decimals; rounding is monotonic, so the OHLC ordering of a bar is preserved.
- Long daily history: no third-party source is used, to avoid any licensing doubt. Daily backtests are limited to
  the 100 bars of the free plan and long demonstrations use the weekly timeframe.
  A daily series is therefore never adjusted, and the report shows the "not adjusted" warning for it.

### Parsing and error handling

- The CSV parser accepts exactly two headers: the raw one (`timestamp,open,high,low,close,volume`) and the one of
  the adjusted endpoints (`...,adjusted close,volume,dividend amount`). Anything else is an `InvalidResponse`.
- Numbers are always parsed with `CultureInfo.InvariantCulture`; thousands separators are rejected.
- A line that is malformed, has a non-positive price or a negative or fractional volume, or breaks the bar
  invariants (`High >= max(Open, Close)`, `Low <= min(Open, Close)`) is dropped with a logged warning. It is never
  repaired. When a date appears twice, the first line (the most recent in Alpha Vantage order) is kept.
- JSON errors received with HTTP 200 are classified as follows: `Error Message` gives `SymbolNotFound`; `Note`
  gives `RateLimitExceeded`; an `Information` text about a rate limit or call frequency gives `RateLimitExceeded`; an
  `Information` text about a premium feature gives `InvalidResponse`; anything else gives `InvalidResponse`.
  The rate limit check comes first because Alpha Vantage's rate limit notice also mentions premium plans.
- Error messages are written by QuantBacktest and never copied from Alpha Vantage: its rate limit notice repeats the
  API key. Inner exceptions are not attached, and the request URL (which contains the key) is never logged.
  The HTTP client's own request logging is removed with `RemoveAllLoggers()`.
- A timeout (30 s) or connection failure gives `NetworkError`; a cancellation requested by the caller is propagated
  as an `OperationCanceledException`, not reported as a network error.

### Local cache (SQLite)

- EF Core with SQLite, schema created with `EnsureCreated()` once per process behind a lock; no migrations.
  Four tables: `PriceBars` (key symbol, timeframe, date), `SeriesMetadata` (origin, last fetch), `ApiUsage`
  (requests per UTC day) and `CustomStrategies` (definitions stored as JSON).
- Decimals are stored as text by the SQLite provider, so prices are kept exactly; queries only sort by date and every
  computation runs in memory.
- A download is merged by date: new dates are inserted, existing dates are replaced, and no cached bar is ever deleted.
  The origin of a series becomes `Mixed` as soon as bars of another origin are added; a series is adjusted only if
  every one of its bars comes from an adjusted source.

### Market data service

- `GetSeriesAsync` without refresh returns the cache when it has data, without any API call, so a backtest gives the
  same result every time. An empty cache is filled from the seed file when there is one, otherwise from the API.
  With `forceRefresh`, the API is called and the bars are merged; a failure leaves the cache untouched.
- Seed files (`data/seed/`) are snapshots of the free Alpha Vantage endpoints taken on 2026-10-15: 100 daily bars and
  the weekly adjusted history since 1999 for `AAPL`, `MSFT`, `SPY` and `JPM`. They make the application usable without
  an API key, for example in Docker.
- Quota: each HTTP request is counted per UTC day before it is sent, so a failed request counts too. The quota is
  checked first, then the key; a missing key sends nothing and counts nothing.
- Concurrency: one lock per symbol and timeframe, and simultaneous calls share the download in progress, so ten
  simultaneous calls send one HTTP request. A caller that cancels stops waiting without cancelling the shared download.

### Custom strategy store

- Each no-code strategy is stored as one JSON document (camelCase, enums as text) in `CustomStrategies`, next to its
  name for sorting. It is read back member for member identical, rule lists included. The store validates nothing:
  Part 2 validates the rules and Part 4 calls that validation before saving.

### Data quality report

- Computed on the cached series only, never through the API (`NotCached` otherwise). It flags price jumps above
  25 %, zero volumes, runs of 5 identical closes (one anomaly per run, dated the 5th bar) and calendar gaps above
  5 days (daily) or 10 days (weekly), and counts the missing weekdays (daily) or missing weeks (weekly).
  Market holidays count as missing weekdays, as the contract specifies.

### PDF report

- QuestPDF (Community license) lays out an A4 portrait report in English; ScottPlot draws the charts as PNG images.
  Every number is printed as it comes in `BacktestResult` and `MonteCarloResult`: the report recomputes nothing.
- Display culture is fixed to `en-US`. Performance figures (returns) are signed percentages (`+12.34 %`), the
  maximum drawdown is shown as a loss (`-23.45 %`), non-negative figures (volatility, win rate, exposure) and
  settings (fees, slippage, stops, risk-free rate) are unsigned percentages, amounts use thousands separators and
  2 decimals, ratios 2 decimals, undefined ratios read `n/a`.
- The generation date in the footer comes from the injected clock, so tests are reproducible.
- Page 1: settings, the "not adjusted" warning when needed, the 13 metrics against buy & hold and the equity curve.
  Page 2: the strategy drawdown, and the closes with the price indicators and buy (up) and sell (down) triangles at
  the fill prices. Then the trade log: every field of every trade, the header repeated on each page.
- The trades CSV follows the contract format exactly (invariant culture, `
` line endings, header always present,
  RFC 4180 quoting if a field ever needed it).

## How it works

## Tests

## Screenshots

## Limitations
