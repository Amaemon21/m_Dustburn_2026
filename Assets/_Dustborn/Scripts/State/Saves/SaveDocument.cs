using System.Collections.Generic;

public sealed class SaveDocument
{
    public const int CURRENT_VERSION = 1;

    public int Version { get; set; } = CURRENT_VERSION;
    public Dictionary<string, object> Sections { get; } = new();
}
