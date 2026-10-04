using System;

public sealed class UnsupportedSaveVersionException : Exception
{
    public UnsupportedSaveVersionException(int version)
        : base($"No migration path from save version {version} to {SaveDocument.CURRENT_VERSION}") { }
}
