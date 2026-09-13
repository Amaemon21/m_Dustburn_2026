using System;

public sealed class SaveSection
{
    public Type DataType { get; }
    public string Key { get; }
    public string FileName { get; }
    public SaveScope Scope { get; }

    public SaveSection(Type dataType, string key, string fileName, SaveScope scope)
    {
        DataType = dataType;
        Key = key;
        FileName = fileName;
        Scope = scope;
    }
}
