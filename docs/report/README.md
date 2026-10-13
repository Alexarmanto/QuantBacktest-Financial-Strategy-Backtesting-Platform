# Rapport final : plan et règles

Langue du rapport : **anglais**. Chaque partie rédige sa section dans `docs/report/part-N.md` (gabarit déjà créé). La Partie 2 assemble `docs/report/REPORT.md`, relit la cohérence et exporte `docs/report/QuantBacktest-Report.pdf`.

## Plan du rapport final (figé)

| # | Section | Rédigée par |
|---|---|---|
| 1 | Introduction : objectives, scope, summary of features | Partie 2 |
| 2 | Architecture : layers, contract, request flow, fakes and parallel work | Partie 3 |
| 3 | Data layer : Alpha Vantage free plan, cache, seed files, data quality, API quota | Partie 1 (part-1.md) |
| 4 | Strategies : indicators, the 10 strategies, C# template, no-code rule builder | Partie 2 (part-2.md) |
| 5 | Backtesting engine : execution model, advanced orders, metrics, optimisation heatmap | Partie 3 (part-3.md) |
| 6 | Robustness analysis : Monte Carlo, train/test methodology | Partie 2 et Partie 4 |
| 7 | Web application : pages, user journey, comparator, permalinks | Partie 4 (part-4.md) |
| 8 | Reports and deployment : PDF report, Docker | Partie 1 (part-1.md) |
| 9 | Quality and teamwork : tests, CI, Git workflow, use of AI assistants | Partie 2, à partir des captures GitHub de tous |
| 10 | Limitations and future work | un paragraphe par partie |
| A | Appendix : user manual (`docs/user-manual/MANUAL.md`) | Partie 2 |

## Gabarit d'une section `part-N.md`

Titres imposés, dans cet ordre : `Objective`, `Design choices`, `How it works`, `Tests`, `Screenshots`, `Limitations`.

## Captures d'écran

- Images dans `docs/report/images/`, nommées `part<N>-<nn>-<sujet>.png` (exemple : `part3-02-hand-check-spreadsheet.png`).
- Le moment exact de chaque capture est indiqué par 📸 dans la notice de la partie (section 11). **Prendre la capture au moment où l'étape réussit** : elle se refait difficilement après coup.
- Chaque capture est listée dans la section `Screenshots` de `part-N.md` avec une légende d'une ligne.
