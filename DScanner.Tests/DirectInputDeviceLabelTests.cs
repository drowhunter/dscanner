using DScanner.DirectInput;

namespace DScanner.Tests;

public sealed class DirectInputDeviceLabelTests
{
    [Fact]
    public void Format_AppendsFirstEightInstanceGuidCharacters()
    {
        string label = DirectInputDeviceLabel.Format(
            "Identical Joystick",
            Guid.Parse("31A184C3-91F8-4D39-8F81-EF48C662129C"));

        Assert.Equal("Identical Joystick [ID 31a184c3]", label);
    }

    [Fact]
    public void FormatIdentifier_ReturnsOnlyShortInstanceGuid()
    {
        string identifier = DirectInputDeviceLabel.FormatIdentifier(
            Guid.Parse("31A184C3-91F8-4D39-8F81-EF48C662129C"));

        Assert.Equal("[ID 31a184c3]", identifier);
    }
}
