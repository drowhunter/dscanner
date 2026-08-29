using DScanner.DirectInput;

namespace DScanner.Tests;

public sealed class DirectInputValueNormalizerTests
{
    [Theory]
    [InlineData(-1000, -1)]
    [InlineData(0, 0)]
    [InlineData(1000, 1)]
    [InlineData(-2000, -1)]
    [InlineData(2000, 1)]
    public void Normalize_MapsAndClampsToSignedUnitRange(int value, double expected)
    {
        double actual = DirectInputValueNormalizer.Normalize(value, -1000, 1000);

        Assert.Equal(expected, actual, 10);
    }
}
