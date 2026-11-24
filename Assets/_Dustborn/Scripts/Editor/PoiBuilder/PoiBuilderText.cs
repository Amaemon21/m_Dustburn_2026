internal static class PoiBuilderText
{
    private const string PREFIX = "editor.poiBuilder.";

    public static string Get(string key) => LocalizationService.Text(PREFIX + key);
    public static string Format(string key, params object[] values) => LocalizationService.Format(PREFIX + key, values);
    public static string Part(PoiPart part) => Get("part" + part);
    public static string Section(PoiSection section) => Get("section" + section);
}
