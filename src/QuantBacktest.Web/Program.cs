using QuantBacktest.Analytics;
using QuantBacktest.Data;
using QuantBacktest.Engine;
using QuantBacktest.Fakes;
using QuantBacktest.Reports;
using QuantBacktest.Strategies;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var services = builder.Services;

services.AddControllersWithViews();

// Composition root (docs/CONTRACT.md section 2). Each part is real or fake on its own, so a part can be
// plugged in as soon as it is merged. Keys: QuantBacktest:UseFakes:{Data|Reports|Strategies|Analytics|Engine}.
bool UseFake(string part) => configuration.GetValue<bool>($"QuantBacktest:UseFakes:{part}");

if (UseFake("Data"))
{
    services.AddFakeData();
}
else
{
    services.AddQuantBacktestData(configuration);
}

if (UseFake("Reports"))
{
    services.AddFakeReports();
}
else
{
    services.AddQuantBacktestReports();
}

if (UseFake("Strategies"))
{
    services.AddFakeStrategies();
}
else
{
    services.AddQuantBacktestStrategies();
}

if (UseFake("Analytics"))
{
    services.AddFakeAnalytics();
}
else
{
    services.AddQuantBacktestAnalytics();
}

if (UseFake("Engine"))
{
    services.AddFakeEngine();
}
else
{
    services.AddQuantBacktestEngine();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

// TODO(part4): replace this placeholder with the real controllers and views (docs/parts/PART-4.md).
app.MapGet("/", () => "QuantBacktest is running. Part 4 replaces this page.");

app.Run();

public partial class Program
{
}
