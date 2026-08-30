using DirectInputWatcher;

namespace DirectInputWatcher.Tests;

public sealed class VidPidParserTests
{
    [Fact]
    public void TryParse_ParsesPnPIdentifier()
    {
        bool parsed = VidPidParser.TryParse(
            @"HID\VID_045E&PID_02FF&IG_00\8&123456&0&0000",
            out int vendorId,
            out int productId);

        Assert.True(parsed);
        Assert.Equal(0x045E, vendorId);
        Assert.Equal(0x02FF, productId);
    }
}
