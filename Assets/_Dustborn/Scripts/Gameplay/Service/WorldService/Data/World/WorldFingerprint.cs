using Unity.Collections;

public static class WorldFingerprint
{
    public static string Of(int generatorVersion, int seed, int worldSize, NativeArray<byte> heights, NativeArray<byte> water,
        int placements, int props)
    {
        string heightSum = GenerationChecksum.Of(heights);
        string waterSum = GenerationChecksum.Of(water);
        string propCount = props > 0 ? $"/{props}" : string.Empty;

        return $"v{generatorVersion}/{seed}/{worldSize}/{heightSum}/{waterSum}/{placements}{propCount}";
    }

    public static string Of(int generatorVersion, int seed, int worldSize, byte[] heights, byte[] water, int placements, int props)
    {
        var heightArray = new NativeArray<byte>(heights, GenerationJobs.ALLOCATOR);
        var waterArray = new NativeArray<byte>(water, GenerationJobs.ALLOCATOR);

        try
        {
            return Of(generatorVersion, seed, worldSize, heightArray, waterArray, placements, props);
        }
        finally
        {
            heightArray.Dispose();
            waterArray.Dispose();
        }
    }
}
