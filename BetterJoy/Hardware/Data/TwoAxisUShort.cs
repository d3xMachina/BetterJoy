namespace BetterJoy.Hardware.Data;

public record struct TwoAxisUShort(ushort X, ushort Y)
{
    public override readonly string ToString()
    {
        return $"X: {X}, Y: {Y}";
    }
}
