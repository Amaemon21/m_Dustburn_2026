using Unity.Collections;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

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
        string heightSum = GenerationChecksum.OfRawBytes(heights);
        string waterSum = GenerationChecksum.OfRawBytes(water);
        string propCount = props > 0 ? $"/{props}" : string.Empty;
        return $"v{generatorVersion}/{seed}/{worldSize}/{heightSum}/{waterSum}/{placements}{propCount}";
    }

    public static string WithContent(string fingerprint, params string[] parts)
    {
        using SHA256 hash = SHA256.Create();
        foreach (string part in parts)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(part ?? string.Empty);
            byte[] length = { (byte)bytes.Length, (byte)(bytes.Length >> 8), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 24) };
            hash.TransformBlock(length, 0, length.Length, length, 0);
            hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
        }

        hash.TransformFinalBlock(System.Array.Empty<byte>(), 0, 0);
        return fingerprint + "/c2/" + System.BitConverter.ToString(hash.Hash).Replace("-", string.Empty);
    }

    public static string WithLayout(string fingerprint, RoadNetworkAsset roads, PoiPlacementAsset placement)
    {
        using SHA256 hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        WriteRoads(writer, roads.Roads);
        WriteRoads(writer, roads.Streets);
        writer.Write(placement.Placements.Count);
        foreach (PoiPlacement poi in placement.Placements)
        {
            writer.Write(poi.Prefab == null ? string.Empty : poi.Prefab.name);
            Write(writer, poi.Position);
            writer.Write(poi.Rotation);
            Write(writer, poi.Footprint);
            Write(writer, poi.PivotOffset);
            writer.Write((int)poi.District);
            writer.Write(poi.ContentSeed);
            writer.Write(JsonUtility.ToJson(poi.Gameplay));
        }

        writer.Write(placement.Props.Count);
        foreach (PropPlacement prop in placement.Props)
        {
            writer.Write(prop.Prefab == null ? string.Empty : prop.Prefab.name);
            Write(writer, prop.Position);
            writer.Write(prop.Yaw);
            writer.Write(prop.GroundOffset);
            writer.Write(prop.TiltToGround);
            writer.Write((int)prop.Context);
        }

        writer.Write(placement.Gameplay == null ? string.Empty : JsonUtility.ToJson(placement.Gameplay));
        writer.Flush();
        stream.FlushFinalBlock();
        return fingerprint + "/l2/" + System.BitConverter.ToString(hash.Hash).Replace("-", string.Empty);
    }

    private static void WriteRoads(BinaryWriter writer, IReadOnlyList<Road> roads)
    {
        writer.Write(roads.Count);
        foreach (Road road in roads)
        {
            writer.Write((int)road.Kind);
            writer.Write(road.Width);
            writer.Write(road.Points.Length);
            foreach (Vector2 point in road.Points)
                Write(writer, point);
        }
    }

    private static void Write(BinaryWriter writer, Vector2 point)
    {
        writer.Write(point.x);
        writer.Write(point.y);
    }

    private static void Write(BinaryWriter writer, Vector3 point)
    {
        writer.Write(point.x);
        writer.Write(point.y);
        writer.Write(point.z);
    }
}
