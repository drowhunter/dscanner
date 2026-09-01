using DScanner.Mapping;

namespace DScanner.Tests;

public sealed class DeviceMappingFileNameTests
{
    private static readonly Guid InstanceGuid =
        Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");

    [Theory]
    [InlineData("T.16000M", "T.16000M.json")]
    [InlineData("Saitek Pro Flight X-56", "Saitek Pro Flight X-56.json")]
    [InlineData("Stick: <left>/right?", "Stick left right.json")]
    [InlineData("  padded  name  ", "padded name.json")]
    [InlineData("trailing dots...", "trailing dots.json")]
    public void Create_SanitizesDeviceNames(string deviceName, string expected) =>
        Assert.Equal(expected, DeviceMappingFileName.Create(deviceName, InstanceGuid));

    [Theory]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Create_DisambiguatesReservedWindowsNames(string deviceName)
    {
        string result = DeviceMappingFileName.Create(deviceName, InstanceGuid);

        Assert.Equal($"{deviceName}-abcdef01.json", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    public void Create_FallsBackToTheInstanceId(string deviceName) =>
        Assert.Equal(
            "device-abcdef01.json",
            DeviceMappingFileName.Create(deviceName, InstanceGuid));

    [Fact]
    public void Create_NeverProducesAnInvalidFileName()
    {
        string result = DeviceMappingFileName.Create(
            "Bad|Name*With\"Everything<>:?\\/", InstanceGuid);

        Assert.Equal(-1, result.IndexOfAny(Path.GetInvalidFileNameChars()));
    }
}
