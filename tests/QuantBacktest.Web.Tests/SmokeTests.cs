namespace QuantBacktest.Web.Tests;

public class SmokeTests
{
    [Fact]
    public void WebAssembly_IsTheExpectedOne()
    {
        typeof(Program).Assembly.GetName().Name.Should().Be("QuantBacktest.Web");
    }
}
