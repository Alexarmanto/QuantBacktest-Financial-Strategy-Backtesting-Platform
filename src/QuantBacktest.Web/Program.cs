using QuantBacktest.Analytics;
using QuantBacktest.Data;
using QuantBacktest.Engine;
using QuantBacktest.Fakes;
using QuantBacktest.Reports;
using QuantBacktest.Strategies;
using QuantBacktest.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var services = builder.Services;

services.AddControllersWithViews();
services.AddRequestLocalization(RequestCultureSetup.Configure);

// Composition root (docs/CONTRACT.md section 2). Each part is real or fake on its own, so a part can be
// plugged in as soon as it is merged. Keys: QuantBacktest:UseFakes:{Data|Reports|Strategies|Analytics|Engine}.
// The same object drives the "Demo data" banner of the layout (P4-U13).
var fakeServices = new FakeServicesStatus(configuration);
services.AddSingleton(fakeServices);
bool UseFake(string part) => fakeServices.IsFake(part);

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

// In Development the developer exception page stays active (P4-U12: no stack trace outside Development).
app.UseRequestLocalization();
app.UseStaticFiles();
app.UseRouting();

// GET / is HomeController.Index, which redirects to /Backtest.
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program
{
}
