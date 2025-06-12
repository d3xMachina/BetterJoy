namespace BetterJoy.Hardware.Data;

public record struct ThreeAxisShort(short X, short Y, short Z)
{
    public static readonly ThreeAxisShort Zero = new(0, 0, 0);
    public readonly bool Invalid => X == -1 || Y == -1 || Z == -1;

    public static ThreeAxisShort operator -(ThreeAxisShort left, ThreeAxisShort right)
    {
        return new ThreeAxisShort(
            (short)(left.X - right.X),
            (short)(left.Y - right.Y),
            (short)(left.Z - right.Z)
        );
    }

    public override string ToString()
    {
        return $"X: {X}, Y: {Y}, Z: {Z}";
    }
}
