using System.Reflection;
using Microsoft.Extensions.Logging;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-C1: the list of every strategy the application can run,
// the built-in ones (found automatically) and the custom no-code ones (read from the store)
internal class StrategyCatalog : IStrategyCatalog
{
    private readonly List<IStrategy> _builtIns;
    private readonly ICustomStrategyStore _store;
    private readonly ICustomStrategyFactory _factory;
    private readonly ILogger<StrategyCatalog> _logger;

    // Constructor: the built-in strategies are found once, when the application starts
    public StrategyCatalog(ICustomStrategyStore store, ICustomStrategyFactory factory, ILogger<StrategyCatalog> logger)
    {
        _store = store;
        _factory = factory;
        _logger = logger;
        _builtIns = CreateBuiltIns(typeof(StrategyCatalog).Assembly.GetTypes());
    }

    // built-in strategies sorted by name, then custom strategies sorted by name
    public async Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        List<IStrategy> customs = new List<IStrategy>();
        IReadOnlyList<CustomStrategyDefinition> definitions = await _store.GetAllAsync(cancellationToken);
        foreach (CustomStrategyDefinition definition in definitions)
        {
            try
            {
                customs.Add(_factory.Create(definition));
            }
            catch (BacktestValidationException exception)
            {
                // a saved strategy that is no longer valid is skipped, the others still work
                _logger.LogWarning("Custom strategy {Id} is skipped because it is invalid: {Problems}", definition.Id, exception.Message);
            }
        }
        SortByName(customs);

        List<IStrategy> result = new List<IStrategy>(_builtIns);
        result.AddRange(customs);
        return result;
    }

    // exact id (upper and lower case matter), null when unknown
    public async Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        foreach (IStrategy strategy in _builtIns)
        {
            if (strategy.Id == id)
            {
                return strategy;
            }
        }

        if (!id.StartsWith(Conventions.CustomStrategyIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        CustomStrategyDefinition? definition = await _store.GetAsync(id, cancellationToken);
        if (definition == null)
        {
            return null;
        }

        // throws BacktestValidationException with the problems when the saved definition is invalid
        return _factory.Create(definition);
    }

    // creates one object for each class that is a strategy, is not abstract,
    // has a public constructor without parameters and is not marked [HiddenStrategy]
    internal static List<IStrategy> CreateBuiltIns(Type[] types)
    {
        List<IStrategy> strategies = new List<IStrategy>();
        List<Type> classes = new List<Type>();

        foreach (Type type in types)
        {
            bool isStrategy = type.IsClass && !type.IsAbstract && typeof(IStrategy).IsAssignableFrom(type);
            bool hasEmptyConstructor = type.GetConstructor(Type.EmptyTypes) != null;
            bool isHidden = type.GetCustomAttribute<HiddenStrategyAttribute>() != null;
            if (!isStrategy || !hasEmptyConstructor || isHidden)
            {
                continue;
            }

            object? instance = Activator.CreateInstance(type);
            if (instance is IStrategy strategy)
            {
                // two strategies with the same id: stop at startup and name both classes
                for (int i = 0; i < strategies.Count; i++)
                {
                    if (strategies[i].Id == strategy.Id)
                    {
                        throw new InvalidOperationException($"Two strategies use the id '{strategy.Id}': {classes[i].Name} and {type.Name}.");
                    }
                }

                strategies.Add(strategy);
                classes.Add(type);
            }
        }

        SortByName(strategies);
        return strategies;
    }

    // sort from A to Z (upper and lower case ignored), with a bubble sort like in TP1
    private static void SortByName(List<IStrategy> strategies)
    {
        for (int i = 0; i < strategies.Count - 1; i++)
        {
            for (int j = 0; j < strategies.Count - 1 - i; j++)
            {
                if (string.Compare(strategies[j].Name, strategies[j + 1].Name, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    IStrategy temp = strategies[j];
                    strategies[j] = strategies[j + 1];
                    strategies[j + 1] = temp;
                }
            }
        }
    }
}
