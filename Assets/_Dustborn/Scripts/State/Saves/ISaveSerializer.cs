using System.Collections.Generic;

public interface ISaveSerializer
{
    string Serialize(SaveDocument document);
    SaveDocument Deserialize(string text, IReadOnlyList<SaveSection> sections);
}
