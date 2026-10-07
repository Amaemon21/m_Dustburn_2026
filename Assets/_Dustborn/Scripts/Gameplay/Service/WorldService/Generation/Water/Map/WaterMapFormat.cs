using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static class WaterMapFormat
{
    private const int MAGIC = 0x52544157;
    private const int VERSION = 5;

    private static readonly object CacheGate = new();
    private static object _cachedSource;
    private static long _cachedLength;
    private static WaterMap _cached;

    private static void Block(BinaryWriter writer, Array values, int size)
    {
        var bytes = new byte[values.Length * size];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        writer.Write(bytes);
    }

    private static void Block(BinaryReader reader, Array values, int length)
    {
        byte[] bytes = reader.ReadBytes(length);

        if (bytes.Length != length)
            throw new InvalidDataException("The water map ends early. Regenerate the world.");

        Buffer.BlockCopy(bytes, 0, values, 0, length);
    }

    public static WaterMap Load(TextAsset asset)
    {
        if (asset == null)
            return null;

        lock (CacheGate)
        {
            if (_cached != null && ReferenceEquals(_cachedSource, asset) && _cachedLength == asset.dataSize)
                return _cached;

            _cached = Read(asset.bytes);
            _cachedSource = asset;
            _cachedLength = asset.dataSize;

            return _cached;
        }
    }

    public static byte[] Write(WaterMap water)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(MAGIC);
        writer.Write(VERSION);
        writer.Write(water.Resolution);
        writer.Write(water.CellSize);
        writer.Write(water.WorldSize);
        writer.Write(water.SeaLevel);
        writer.Write(water.ShoreReach);

        writer.Write(water.Kinds);

        Block(writer, water.BodyIds, sizeof(short));
        Block(writer, water.ShoreSurface, sizeof(float));
        Block(writer, water.ShoreDistance, sizeof(float));

        foreach (bool frozen in water.Frozen)
            writer.Write(frozen);

        foreach (bool dry in water.Dry)
            writer.Write(dry);

        writer.Write(water.DetailCells.Count);
        Block(writer, water.DetailCells.ToArray(), sizeof(int));
        Block(writer, water.DetailOwners, sizeof(short));
        Block(writer, water.DetailGrounds, sizeof(float));

        writer.Write(water.Bodies.Count);

        foreach (WaterBody body in water.Bodies)
        {
            writer.Write((byte)body.Kind);
            writer.Write(body.Surface);
            writer.Write(body.Area);
            writer.Write(body.Depth);
            writer.Write(body.Center.x);
            writer.Write(body.Center.y);
        }

        writer.Write(water.Rivers.Count);

        foreach (RiverPath river in water.Rivers)
        {
            writer.Write(river.Points.Count);
            writer.Write((byte)river.Source);
            writer.Write((byte)river.Terminal);
            writer.Write(river.SourceArea);
            writer.Write(river.SourceDonor);
            writer.Write(river.Parent);

            foreach (RiverPoint point in river.Points)
            {
                writer.Write(point.Position.x);
                writer.Write(point.Position.y);
                writer.Write(point.Surface);
                writer.Write(point.Bed);
                writer.Write(point.Width);
                writer.Write(point.Flow);
                writer.Write(point.Submerged);
            }
        }

        writer.Write(water.Crossings.Count);

        foreach (WaterCrossing crossing in water.Crossings)
        {
            writer.Write(crossing.Position.x);
            writer.Write(crossing.Position.y);
            writer.Write(crossing.Direction.x);
            writer.Write(crossing.Direction.y);
            writer.Write(crossing.Flow.x);
            writer.Write(crossing.Flow.y);
            writer.Write(crossing.RiverWidth);
            writer.Write(crossing.WaterHeight);
            writer.Write((int)crossing.RoadKind);
            writer.Write(crossing.Bridge);
            writer.Write(crossing.Center.x);
            writer.Write(crossing.Center.y);
            writer.Write(crossing.Span);
            writer.Write(crossing.DeckHeight);
            writer.Write(crossing.RoadWidth);
        }

        writer.Flush();
        return stream.ToArray();
    }


    public static WaterMap Read(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));

        if (reader.ReadInt32() != MAGIC)
            throw new InvalidDataException("Not a water map");

        int version = reader.ReadInt32();

        if (version != VERSION)
            throw new InvalidDataException($"Water map version {version}, expected {VERSION}. Regenerate the world.");

        int resolution = reader.ReadInt32();
        float cellSize = reader.ReadSingle();
        float worldSize = reader.ReadSingle();
        float seaLevel = reader.ReadSingle();

        var map = new WaterMap(resolution, cellSize, worldSize, seaLevel) { ShoreReach = reader.ReadSingle() };
        int cells = resolution * resolution;

        reader.Read(map.Kinds, 0, cells);

        Block(reader, map.BodyIds, cells * sizeof(short));
        Block(reader, map.ShoreSurface, cells * sizeof(float));
        Block(reader, map.ShoreDistance, cells * sizeof(float));

        for (int i = 0; i < cells; i++)
            map.Frozen[i] = reader.ReadBoolean();

        for (int i = 0; i < cells; i++)
            map.Dry[i] = reader.ReadBoolean();

        int details = reader.ReadInt32();

        if (details < 0 || details > cells)
            throw new InvalidDataException($"The water map holds {details} shore cells for {cells} cells. Regenerate the world.");

        var detailCells = new int[details];
        var detailOwners = new short[details * WaterMap.CELL_NODES];
        var detailGround = new float[details * WaterMap.CELL_NODES];

        Block(reader, detailCells, details * sizeof(int));
        Block(reader, detailOwners, detailOwners.Length * sizeof(short));
        Block(reader, detailGround, detailGround.Length * sizeof(float));

        map.SetDetail(detailCells, detailOwners, detailGround);
        map.MarkShoreReady();

        int bodies = reader.ReadInt32();

        for (int i = 0; i < bodies; i++)
        {
            map.Bodies.Add(new WaterBody
            {
                Kind = (WaterKind)reader.ReadByte(),
                Surface = reader.ReadSingle(),
                Area = reader.ReadSingle(),
                Depth = reader.ReadSingle(),
                Center = new Vector2(reader.ReadSingle(), reader.ReadSingle())
            });
        }

        int rivers = reader.ReadInt32();

        for (int i = 0; i < rivers; i++)
        {
            var river = new RiverPath();
            int points = reader.ReadInt32();
            river.Source = (RiverSource)reader.ReadByte();
            river.Terminal = (RiverTerminal)reader.ReadByte();
            river.SourceArea = reader.ReadSingle();
            river.SourceDonor = reader.ReadSingle();
            river.Parent = reader.ReadInt32();

            for (int p = 0; p < points; p++)
            {
                river.Points.Add(new RiverPoint
                {
                    Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    Surface = reader.ReadSingle(),
                    Bed = reader.ReadSingle(),
                    Width = reader.ReadSingle(),
                    Flow = reader.ReadSingle(),
                    Submerged = reader.ReadBoolean()
                });
            }

            map.Rivers.Add(river);
        }

        int crossings = reader.ReadInt32();

        for (int i = 0; i < crossings; i++)
        {
            map.Crossings.Add(new WaterCrossing
            {
                Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                Direction = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                Flow = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                RiverWidth = reader.ReadSingle(),
                WaterHeight = reader.ReadSingle(),
                RoadKind = (RoadKind)reader.ReadInt32(),
                Bridge = reader.ReadBoolean(),
                Center = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                Span = reader.ReadSingle(),
                DeckHeight = reader.ReadSingle(),
                RoadWidth = reader.ReadSingle()
            });
        }

        return map;
    }
}
