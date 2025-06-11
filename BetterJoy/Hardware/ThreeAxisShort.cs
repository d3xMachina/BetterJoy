namespace BetterJoy.Hardware;

public record ThreeAxisShort(short X, short Y, short Z)
{
    public override string ToString()
    {
        return $"X: {X}, Y: {Y}, Z: {Z}";
    }

    public bool Invalid => X == -1 || Y == -1 || Z == -1;
}
