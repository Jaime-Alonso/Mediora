namespace Mediora.Tests;

public sealed class UnitTests
{
    [Fact]
    public void Value_ReturnsSingletonValue()
    {
        var first = Unit.Value;
        var second = Unit.Value;

        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void EqualityAndHashCode_AlwaysMatchForUnitValues()
    {
        var unit = Unit.Value;

        Assert.True(unit.Equals(Unit.Value));
        Assert.True(unit.Equals((object)Unit.Value));
        Assert.Equal(0, unit.GetHashCode());
        Assert.Equal("()", unit.ToString());
    }

    [Fact]
    public async Task Task_ReturnsUnitValue()
    {
        var value = await Unit.Task;

        Assert.Equal(Unit.Value, value);
    }
}
