using System;

[Serializable]
public class ProgressData
{
    public string SlotName = string.Empty;
    public long CreatedUnixTime;
    public long UpdatedUnixTime;
    public float PlayTimeSeconds;
}
