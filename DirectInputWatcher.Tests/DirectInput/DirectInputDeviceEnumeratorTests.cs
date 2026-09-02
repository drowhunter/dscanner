using DirectInputWatcher;

namespace DirectInputWatcher.Tests;

public sealed class DirectInputDeviceEnumeratorTests
{
    [Fact]
    public void GetVidPid_DecodesDirectInputProductGuid()
    {
        Guid productGuid = new("0003346E-0000-0000-0000-000000000000");

        (int? vendorId, int? productId) =
            DirectInputDeviceEnumerator.GetVidPid(productGuid);

        Assert.Equal(0x346E, vendorId);
        Assert.Equal(0x0003, productId);
    }
}
