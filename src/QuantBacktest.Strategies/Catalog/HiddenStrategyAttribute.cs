namespace QuantBacktest.Strategies;

// P2-C1: a strategy class marked with [HiddenStrategy] is not shown in the catalog (used by the template)
[AttributeUsage(AttributeTargets.Class)]
internal sealed class HiddenStrategyAttribute : Attribute
{
}
