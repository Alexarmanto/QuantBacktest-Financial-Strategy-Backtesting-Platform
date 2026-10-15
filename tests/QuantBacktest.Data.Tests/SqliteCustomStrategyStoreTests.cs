using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public sealed class SqliteCustomStrategyStoreTests : IDisposable
{
    private readonly TemporaryDatabase _temporary = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero));

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task SaveThenGet_FromANewStoreInstance_ReturnsAnIdenticalDefinition() // P1-D14
    {
        await NewStore().SaveAsync(FakeCustomStrategyStore.Example);

        var reread = await NewStore().GetAsync(FakeCustomStrategyStore.Example.Id);

        // Records compare lists by reference: equality "lists included" is checked member by member, in order.
        reread.Should().BeEquivalentTo(FakeCustomStrategyStore.Example, options => options.WithStrictOrdering());
        reread!.Entry.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Save_AllOperandFields_SurviveTheRoundTrip() // P1-D14
    {
        var definition = new CustomStrategyDefinition(
            "custom-all-fields",
            "All fields",
            string.Empty,
            new RuleSet(RuleCombinator.Any,
            [
                new RuleCondition(new Operand(OperandKind.BollingerLower, Period: 20, StdDevs: 2.5), ComparisonOperator.CrossesBelow, new Operand(OperandKind.Close)),
                new RuleCondition(new Operand(OperandKind.MacdLine), ComparisonOperator.CrossesAbove, new Operand(OperandKind.MacdSignal)),
                new RuleCondition(new Operand(OperandKind.RateOfChange, Period: 10), ComparisonOperator.GreaterThan, new Operand(OperandKind.Constant, Value: 0.015)),
            ]),
            new RuleSet(RuleCombinator.All,
            [
                new RuleCondition(new Operand(OperandKind.Volume), ComparisonOperator.LessThan, new Operand(OperandKind.Constant, Value: -1e-7)),
            ]));
        await NewStore().SaveAsync(definition);

        var reread = await NewStore().GetAsync(definition.Id);

        reread.Should().BeEquivalentTo(definition, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Save_StoresCamelCaseJsonWithEnumsAsText() // P1-D14
    {
        await NewStore().SaveAsync(FakeCustomStrategyStore.Example);

        await using var context = await _temporary.Open().CreateContextAsync(CancellationToken.None);
        var row = await context.CustomStrategies.SingleAsync();

        row.Name.Should().Be("Trend dip");
        row.UpdatedUtc.Should().Be(_time.UtcNow);
        using var json = JsonDocument.Parse(row.Json);
        json.RootElement.GetProperty("entry").GetProperty("combinator").GetString().Should().Be("All");
        json.RootElement.GetProperty("entry").GetProperty("conditions")[0].GetProperty("operator").GetString().Should().Be("GreaterThan");
        json.RootElement.GetProperty("exit").GetProperty("conditions")[0].GetProperty("right").GetProperty("kind").GetString().Should().Be("Sma");
    }

    [Fact]
    public async Task Save_SameId_ReplacesTheDefinition() // P1-D14
    {
        var store = NewStore();
        await store.SaveAsync(FakeCustomStrategyStore.Example);
        var renamed = FakeCustomStrategyStore.Example with { Name = "Trend dip v2", Description = "Changed." };
        _time.UtcNow = _time.UtcNow.AddHours(1);

        await store.SaveAsync(renamed);

        (await store.GetAllAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(renamed, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Delete_ReturnsTrueForAKnownIdAndFalseOtherwise() // P1-D14
    {
        var store = NewStore();
        await store.SaveAsync(FakeCustomStrategyStore.Example);

        (await store.DeleteAsync(FakeCustomStrategyStore.Example.Id)).Should().BeTrue();
        (await store.DeleteAsync(FakeCustomStrategyStore.Example.Id)).Should().BeFalse();
        (await store.DeleteAsync("custom-unknown")).Should().BeFalse();
        (await store.GetAsync(FakeCustomStrategyStore.Example.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull() // P1-D14
        => (await NewStore().GetAsync("custom-unknown")).Should().BeNull();

    [Fact]
    public async Task GetAll_IsSortedByNameOrdinalIgnoringCase() // P1-D14
    {
        var store = NewStore();
        foreach (var name in new[] { "beta", "Alpha", "gamma", "Beta two", "alpha 2" })
        {
            await store.SaveAsync(FakeCustomStrategyStore.Example with { Id = $"custom-{name.Replace(' ', '-').ToLowerInvariant()}", Name = name });
        }

        var names = (await NewStore().GetAllAsync()).Select(definition => definition.Name);

        names.Should().Equal("Alpha", "alpha 2", "beta", "Beta two", "gamma");
    }

    [Fact]
    public async Task Save_DoesNotValidateTheRules() // P1-D14: validation is Part 2's job
    {
        var invalid = new CustomStrategyDefinition("not-a-custom-id", "", new string('x', 600),
            new RuleSet(RuleCombinator.All, []),
            new RuleSet(RuleCombinator.Any, []));

        await NewStore().SaveAsync(invalid);

        (await NewStore().GetAsync("not-a-custom-id")).Should().BeEquivalentTo(invalid);
    }

    private SqliteCustomStrategyStore NewStore() => new(_temporary.Open(), _time);
}
