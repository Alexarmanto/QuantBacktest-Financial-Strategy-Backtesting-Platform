namespace QuantBacktest.Strategies;

// P2-C1: a strategy class marked with [HiddenStrategy] is not shown in the catalog (used by the template).
// Only the marked class is hidden: a class that inherits from it is shown (Inherited = false).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class HiddenStrategyAttribute : Attribute
{
}
