namespace KenoKoch.Modules.Plc.Program;

/// <summary>Nachbildung der S7-Anweisungen NORM_X und SCALE_X (TIA Portal).</summary>
public static class S7Math
{
    /// <summary>NORM_X: normiert <paramref name="value"/> aus [min, max] auf 0,0 … 1,0.</summary>
    public static float NormX(int value, int min, int max) => (float)(value - min) / (max - min);

    /// <summary>SCALE_X: skaliert 0,0 … 1,0 auf [min, max].</summary>
    public static float ScaleX(float value, float min, float max) => value * (max - min) + min;
}
