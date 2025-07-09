namespace BetterJoy.Controller;

public record struct Stick(float X, float Y)
{
    public static readonly Stick Zero = new(0, 0);

    public override readonly string ToString()
    {
        return $"X: {X}, Y: {Y}";
    }
}
