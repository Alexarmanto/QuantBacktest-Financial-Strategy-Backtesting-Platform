# Independent reference values for the indicator tests (PART-2 section 7).
# Computed with pandas, outside of the C# code, then pasted into IndicatorReferenceData.cs.
#
# Method (docs/CONTRACT.md section 8):
#   Bollinger(20, 2): pandas rolling mean, rolling std with ddof=0 (population)   -> I3, I4
#   EMA(n):           seeded with the mean of the first n values, then ewm(alpha=2/(n+1), adjust=False) -> I2
#   MACD(12, 26, 9):  EMA(12) - EMA(26), signal = EMA(9) of the defined line values  -> I7
#   RSI(14):          Wilder, first averages = mean of the first 14 changes,
#                     then ewm(alpha=1/14, adjust=False)                            -> I5
#
# Run: python indicators_reference.py

import numpy as np
import pandas as pd

# 60 closing prices: a fixed random walk (seed 42), rounded to 2 decimals
rng = np.random.default_rng(42)
prices = [100.0]
for _ in range(59):
    prices.append(round(prices[-1] * (1 + rng.normal(0, 0.015)), 2))
close = pd.Series(prices)


def ema(series, n):
    """EMA seeded with the SMA of the first n defined values."""
    defined = series.dropna()
    result = pd.Series(np.nan, index=series.index)
    if len(defined) < n:
        return result
    seeded = defined.iloc[n - 1:].copy()
    seeded.iloc[0] = defined.iloc[:n].mean()
    result.loc[seeded.index] = seeded.ewm(alpha=2 / (n + 1), adjust=False).mean()
    return result


# Bollinger(20, 2)
middle = close.rolling(20).mean()
std = close.rolling(20).std(ddof=0)
upper = middle + 2 * std
lower = middle - 2 * std

# MACD(12, 26, 9)
line = ema(close, 12) - ema(close, 26)
signal = ema(line, 9)

# RSI(14), Wilder
n = 14
change = close.diff()
gain = change.clip(lower=0)
loss = -change.clip(upper=0)
avg_gain = pd.Series(np.nan, index=close.index)
avg_loss = pd.Series(np.nan, index=close.index)
seed_gain = gain.iloc[1:n + 1].mean()
seed_loss = loss.iloc[1:n + 1].mean()
g = pd.concat([pd.Series([seed_gain]), gain.iloc[n + 1:]], ignore_index=True)
l = pd.concat([pd.Series([seed_loss]), loss.iloc[n + 1:]], ignore_index=True)
avg_gain.iloc[n:] = g.ewm(alpha=1 / n, adjust=False).mean().values
avg_loss.iloc[n:] = l.ewm(alpha=1 / n, adjust=False).mean().values
rsi = 100 - 100 / (1 + avg_gain / avg_loss)


def to_csharp(name, series):
    items = ["double.NaN" if pd.isna(v) else repr(float(v)) for v in series]
    lines = []
    for start in range(0, len(items), 5):
        lines.append("        " + ", ".join(items[start:start + 5]) + ",")
    return f"    public static readonly double[] {name} = new double[]\n    {{\n" + "\n".join(lines) + "\n    };\n"


print(to_csharp("Close", close))
print(to_csharp("BollingerUpper", upper))
print(to_csharp("BollingerMiddle", middle))
print(to_csharp("BollingerLower", lower))
print(to_csharp("MacdLine", line))
print(to_csharp("MacdSignal", signal))
print(to_csharp("Rsi", rsi))
