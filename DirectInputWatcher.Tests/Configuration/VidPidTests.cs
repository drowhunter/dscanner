namespace DirectInputWatcher.Tests;

public sealed class VidPidTests
{
    [Theory]
    [InlineData("346E:0003")]
    [InlineData("0x346e:0x0003")]
    public void ParseAcceptsDisplayedHexFormat(string value)
    {
        VidPid identity = VidPid.Parse(value);

        Assert.Equal(0x346E, identity.VendorId);
        Assert.Equal(0x0003, identity.ProductId);
        Assert.Equal("346E:0003", identity.ToString());
    }

    [Fact]
    public void ParseRejectsInvalidIdentity()
    {
        Assert.Throws<FormatException>(() => VidPid.Parse("346E"));
    }
}
