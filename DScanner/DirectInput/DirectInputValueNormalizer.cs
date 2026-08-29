namespace DScanner.DirectInput;

public static class DirectInputValueNormalizer
{
    public static double Normalize(int value, int minimum, int maximum)
    {
        if (maximum <= minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum must be greater than minimum.");
        }

        double clamped = Math.Clamp((double)value, minimum, maximum);
        return ((clamped - minimum) / (maximum - minimum) * 2d) - 1d;
    }
}
