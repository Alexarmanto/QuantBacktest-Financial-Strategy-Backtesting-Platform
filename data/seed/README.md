# Fichiers de départ (seed)

Le plan gratuit d'Alpha Vantage ne fournit que les 100 dernières barres en daily. Ces fichiers portent les longues historiques nécessaires à la heatmap, au train/test et au Monte Carlo (règle D1 du contrat).

- Noms : `<SYMBOL>_daily.csv` et `<SYMBOL>_weekly.csv`.
- Format : exactement celui du CSV d'Alpha Vantage (`timestamp,open,high,low,close,volume`, ordre décroissant, point décimal).
- Symboles : `AAPL`, `MSFT`, `SPY`, `JPM`.
- Responsable : Partie 1, qui documente la source et la méthode de constitution dans `docs/report/part-1.md`.
