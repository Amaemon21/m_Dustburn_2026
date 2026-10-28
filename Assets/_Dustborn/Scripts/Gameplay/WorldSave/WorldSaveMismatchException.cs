using System;

public sealed class WorldSaveMismatchException : InvalidOperationException
{
    public string SlotId { get; }
    public string Saved { get; }
    public string Loaded { get; }

    public WorldSaveMismatchException(string slotId, string saved, string loaded)
        : base($"Save slot '{slotId}' belongs to world '{saved}', the scene loads world '{loaded}'. Regenerate the world the slot was made on, or delete Saves/{slotId}")
    {
        SlotId = slotId;
        Saved = saved;
        Loaded = loaded;
    }
}
