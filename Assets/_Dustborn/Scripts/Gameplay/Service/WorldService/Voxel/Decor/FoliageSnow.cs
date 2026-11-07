using UnityEngine;

public static class FoliageSnow
{
    private static readonly int ENABLE = Shader.PropertyToID("_Enable_Frosting");
    private static readonly int HEIGHT = Shader.PropertyToID("_Frosting_Height");
    private static readonly int FALLOFF = Shader.PropertyToID("_Frosting_Falloff");
    private static readonly int COLOR = Shader.PropertyToID("_Frosting_Color");
    private static readonly int GLOBAL_WEATHER = Shader.PropertyToID("_Use_Global_Weather_Controller");
    private static readonly Color SNOW_COLOR = new(0.93f, 0.96f, 0.98f, 1f);

    public static bool Supports(Material material) => material.HasProperty(ENABLE);

    public static void Apply(Material source, Material target, float amount)
    {
        if (!Supports(source))
            return;

        amount = Mathf.Clamp01(amount);
        target.SetFloat(ENABLE, amount > 0f ? 1f : 0f);

        if (target.HasProperty(GLOBAL_WEATHER))
            target.SetFloat(GLOBAL_WEATHER, 0f);

        if (amount <= 0f)
            return;

        bool maximum = amount >= 1f;

        if (target.HasProperty(HEIGHT))
            target.SetFloat(HEIGHT, maximum ? 2f : Mathf.Max(0.01f, source.GetFloat(HEIGHT)) * Mathf.Lerp(2f, 0.5f, amount));

        if (target.HasProperty(FALLOFF))
            target.SetFloat(FALLOFF, maximum ? 128f : Mathf.Max(0.01f, source.GetFloat(FALLOFF)) * Mathf.Lerp(0.75f, 1.15f, amount));

        if (target.HasProperty(COLOR))
            target.SetColor(COLOR, maximum ? Color.white : SNOW_COLOR);
    }
}
