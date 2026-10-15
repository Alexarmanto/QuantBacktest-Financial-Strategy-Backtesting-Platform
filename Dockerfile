# QuantBacktest: build and test with the SDK image, run with the ASP.NET image (P1-X2).
# Usage: docker compose up --build   (see docker-compose.yml)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Fonts and fontconfig: the report tests render PDF pages and charts (QuestPDF, ScottPlot/SkiaSharp).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Restore first, from the project files only, so the layer is cached while the code changes.
COPY global.json .editorconfig Directory.Build.props Directory.Packages.props QuantBacktest.sln ./
COPY src/QuantBacktest.Core/QuantBacktest.Core.csproj src/QuantBacktest.Core/
COPY src/QuantBacktest.Fakes/QuantBacktest.Fakes.csproj src/QuantBacktest.Fakes/
COPY src/QuantBacktest.Data/QuantBacktest.Data.csproj src/QuantBacktest.Data/
COPY src/QuantBacktest.Reports/QuantBacktest.Reports.csproj src/QuantBacktest.Reports/
COPY src/QuantBacktest.Strategies/QuantBacktest.Strategies.csproj src/QuantBacktest.Strategies/
COPY src/QuantBacktest.Analytics/QuantBacktest.Analytics.csproj src/QuantBacktest.Analytics/
COPY src/QuantBacktest.Engine/QuantBacktest.Engine.csproj src/QuantBacktest.Engine/
COPY src/QuantBacktest.Web/QuantBacktest.Web.csproj src/QuantBacktest.Web/
COPY tests/Directory.Build.props tests/
COPY tests/QuantBacktest.Core.Tests/QuantBacktest.Core.Tests.csproj tests/QuantBacktest.Core.Tests/
COPY tests/QuantBacktest.Fakes.Tests/QuantBacktest.Fakes.Tests.csproj tests/QuantBacktest.Fakes.Tests/
COPY tests/QuantBacktest.Data.Tests/QuantBacktest.Data.Tests.csproj tests/QuantBacktest.Data.Tests/
COPY tests/QuantBacktest.Reports.Tests/QuantBacktest.Reports.Tests.csproj tests/QuantBacktest.Reports.Tests/
COPY tests/QuantBacktest.Strategies.Tests/QuantBacktest.Strategies.Tests.csproj tests/QuantBacktest.Strategies.Tests/
COPY tests/QuantBacktest.Analytics.Tests/QuantBacktest.Analytics.Tests.csproj tests/QuantBacktest.Analytics.Tests/
COPY tests/QuantBacktest.Engine.Tests/QuantBacktest.Engine.Tests.csproj tests/QuantBacktest.Engine.Tests/
COPY tests/QuantBacktest.Web.Tests/QuantBacktest.Web.Tests.csproj tests/QuantBacktest.Web.Tests/
RUN dotnet restore QuantBacktest.sln

COPY src/ src/
COPY tests/ tests/
COPY data/seed/ data/seed/

# The image is only built when the whole solution compiles strictly and every test passes.
RUN dotnet build QuantBacktest.sln --no-restore --configuration Release -warnaserror \
    && dotnet test QuantBacktest.sln --no-build --configuration Release \
    && dotnet publish src/QuantBacktest.Web/QuantBacktest.Web.csproj --no-build --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Fonts and fontconfig needed to render the PDF report and its charts.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
COPY data/seed/ /app/seed/

# The SQLite database lives in /data (a named volume in docker-compose.yml), owned by the non-root user.
RUN mkdir -p /data && chown app:app /data
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    QuantBacktest__DatabasePath=/data/quantbacktest.db \
    QuantBacktest__SeedDirectory=/app/seed
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuantBacktest.Web.dll"]
