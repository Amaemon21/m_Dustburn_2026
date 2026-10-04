using System.Collections.Generic;

public sealed class SaveDocument
{
    public const int CURRENT_VERSION = 2;

    public int Version { get; set; } = CURRENT_VERSION;
    internal bool RequiresSave { get; set; }
    public Dictionary<string, object> Sections { get; } = new();
}
