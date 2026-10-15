# Fichiers de départ (seed)

Le plan gratuit d'Alpha Vantage ne fournit que les 100 dernières barres en daily. Ces fichiers portent les longues historiques nécessaires à la heatmap, au train/test et au Monte Carlo (règle D1 du contrat).

- Noms : `<SYMBOL>_daily.csv` et `<SYMBOL>_weekly.csv`.
- Format : exactement celui du CSV d'Alpha Vantage, ordre décroissant, point décimal. Daily : `timestamp,open,high,low,close,volume` (`TIME_SERIES_DAILY`, compact, prix bruts). Weekly : `timestamp,open,high,low,close,adjusted close,volume,dividend amount` (`TIME_SERIES_WEEKLY_ADJUSTED`, prix ajustés à l'import).
- Source : instantanés de l'API Alpha Vantage gratuite pris le 2026-10-15 (100 barres daily, weekly depuis 1999-11-12). Aucune source tierce (voir `docs/report/part-1.md`, Design choices).
- Symboles : `AAPL`, `MSFT`, `SPY`, `JPM`.
- Responsable : Partie 1, qui documente la source et la méthode de constitution dans `docs/report/part-1.md`.
