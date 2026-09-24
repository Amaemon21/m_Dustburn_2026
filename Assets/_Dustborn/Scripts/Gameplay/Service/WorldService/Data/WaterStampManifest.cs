using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public class WaterStampManifest
{
    public string name;
    public int version;
    public int[] resolution;
    public int channels;
    public int bit_depth;
    public WaterStampManifestRaw raw_format;
    public WaterStampManifestEntry[] stamps;

    public int Resolution => resolution != null && resolution.Length > 0 ? resolution[0] : 0;

    public long ExpectedBytes => (long)Resolution * Resolution * sizeof(ushort);

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (resolution == null || resolution.Length != 2 || resolution[0] != resolution[1] || resolution[0] < 2)
            errors.Add("resolution must be two equal sides of at least 2");

        if (channels != 1)
            errors.Add($"channels is {channels}, a water stamp mask has one");

        if (bit_depth != 16)
            errors.Add($"bit_depth is {bit_depth}, a water stamp mask is 16 bit");

        if (raw_format == null)
            errors.Add("raw_format is missing");
        else
            raw_format.Validate(ExpectedBytes, Resolution, errors);

        if (stamps == null || stamps.Length == 0)
        {
            errors.Add("the manifest lists no stamps");
            return errors;
        }

        var ids = new HashSet<int>();
        var names = new HashSet<string>();

        foreach (WaterStampManifestEntry stamp in stamps)
        {
            if (stamp == null)
            {
                errors.Add("a stamp entry is empty");
                continue;
            }

            if (!ids.Add(stamp.id))
                errors.Add($"stamp id {stamp.id} is used twice");

            if (string.IsNullOrEmpty(stamp.name) || !names.Add(stamp.name))
                errors.Add($"stamp {stamp.id} has an empty or repeated name '{stamp.name}'");

            stamp.Validate(errors);
        }

        return errors;
    }

    public static string Sha256(byte[] bytes)
    {
        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(bytes);
        var text = new StringBuilder(digest.Length * 2);

        foreach (byte value in digest)
            text.Append(value.ToString("x2"));

        return text.ToString();
    }
}

[Serializable]
public class WaterStampManifestRaw
{
    public string extension;
    public string sample_type;
    public string byte_order;
    public int header_bytes;
    public string row_order;
    public int row_stride_bytes;
    public long file_size_bytes;

    public void Validate(long expectedBytes, int resolution, List<string> errors)
    {
        if (row_stride_bytes != resolution * sizeof(ushort))
            errors.Add($"raw_format.row_stride_bytes is {row_stride_bytes}, a row of {resolution} uint16 samples is {resolution * sizeof(ushort)}");

        if (!string.Equals(sample_type, "uint16", StringComparison.OrdinalIgnoreCase))
            errors.Add($"raw_format.sample_type is '{sample_type}', expected uint16");

        if (!string.Equals(byte_order, "little-endian", StringComparison.OrdinalIgnoreCase))
            errors.Add($"raw_format.byte_order is '{byte_order}', expected little-endian");

        if (header_bytes != 0)
            errors.Add($"raw_format.header_bytes is {header_bytes}, expected 0");

        if (row_order == null || !row_order.StartsWith("top", StringComparison.OrdinalIgnoreCase))
            errors.Add($"raw_format.row_order is '{row_order}', expected top row first");

        if (file_size_bytes != expectedBytes)
            errors.Add($"raw_format.file_size_bytes is {file_size_bytes}, the resolution needs {expectedBytes}");
    }
}

[Serializable]
public class WaterStampManifestBranch
{
    public string kind;
    public float[] position;
    public float[] direction;

    public bool TryKind(out WaterStampBranchKind branchKind)
    {
        switch (kind)
        {
            case "tributary_in":
                branchKind = WaterStampBranchKind.TributaryIn;
                return true;
            case "branch_out":
                branchKind = WaterStampBranchKind.BranchOut;
                return true;
            default:
                branchKind = WaterStampBranchKind.TributaryIn;
                return false;
        }
    }
}

[Serializable]
public class WaterStampManifestEntry
{
    public int id;
    public string name;
    public string category;
    public string kind;
    public string mask_png16;
    public string mask_raw16;
    public string preview_only;
    public float[] recommended_footprint_m;
    public float recommended_depth_m;
    public float recommended_bank_width_m;
    public float nominal_channel_width_m;
    public float[] entry;
    public float[] exit;
    public WaterStampManifestBranch[] branches;
    public bool allow_mirror;
    public float min_scale;
    public float max_scale;
    public float weight;
    public string[] preferred_biomes;
    public string sha256_raw16;

    public bool TryKind(out WaterStampKind stampKind)
    {
        switch (kind)
        {
            case "river":
                stampKind = WaterStampKind.River;
                return true;
            case "lake":
                stampKind = WaterStampKind.Lake;
                return true;
            case "pond":
                stampKind = WaterStampKind.Pond;
                return true;
            default:
                stampKind = WaterStampKind.River;
                return false;
        }
    }

    public bool TryCategory(out WaterStampCategory stampCategory)
    {
        return Enum.TryParse(category, false, out stampCategory) && Enum.IsDefined(typeof(WaterStampCategory), stampCategory);
    }

    public Vector2 Footprint => new(recommended_footprint_m[0], recommended_footprint_m[1]);

    public void Validate(List<string> errors)
    {
        string label = $"stamp {id} '{name}'";

        if (!TryKind(out WaterStampKind stampKind))
            errors.Add($"{label}: unknown kind '{kind}'");

        if (!TryCategory(out _))
            errors.Add($"{label}: unknown category '{category}'");

        if (string.IsNullOrEmpty(mask_raw16))
            errors.Add($"{label}: mask_raw16 is empty");

        if (recommended_footprint_m == null || recommended_footprint_m.Length != 2 || recommended_footprint_m[0] <= 0f || recommended_footprint_m[1] <= 0f)
            errors.Add($"{label}: recommended_footprint_m must be two positive metres");

        if (recommended_depth_m <= 0f)
            errors.Add($"{label}: recommended_depth_m must be positive");

        if (recommended_bank_width_m < 0f)
            errors.Add($"{label}: recommended_bank_width_m is negative");

        if (min_scale <= 0f || max_scale < min_scale)
            errors.Add($"{label}: scale range {min_scale}..{max_scale} is empty");

        if (weight < 0f)
            errors.Add($"{label}: weight is negative");

        if (string.IsNullOrEmpty(sha256_raw16) || sha256_raw16.Length != 64)
            errors.Add($"{label}: sha256_raw16 is not a SHA-256 digest");

        if (stampKind != WaterStampKind.River)
            return;

        if (nominal_channel_width_m <= 0f)
            errors.Add($"{label}: a river needs a positive nominal_channel_width_m");

        if (!InUnit(entry) || !InUnit(exit))
            errors.Add($"{label}: a river needs entry and exit inside the unit square");

        if (branches == null)
            return;

        foreach (WaterStampManifestBranch branch in branches)
        {
            if (branch == null || !branch.TryKind(out _))
                errors.Add($"{label}: unknown branch kind '{branch?.kind}'");
            else if (!InUnit(branch.position) || branch.direction == null || branch.direction.Length != 2)
                errors.Add($"{label}: branch '{branch.kind}' needs a position inside the unit square and a direction");
        }
    }

    private static bool InUnit(float[] point)
    {
        return point != null && point.Length == 2 && point[0] >= 0f && point[0] <= 1f && point[1] >= 0f && point[1] <= 1f;
    }

    public IEnumerable<BiomeType> Biomes(List<string> warnings)
    {
        if (preferred_biomes == null)
            yield break;

        foreach (string biome in preferred_biomes)
        {
            if (Enum.TryParse(biome, true, out BiomeType type))
                yield return type;
            else
                warnings.Add($"stamp {id} '{name}': unknown biome '{biome}' ignored");
        }
    }

    public WaterStampDefinition ToDefinition(TextAsset mask, int resolution, List<string> warnings)
    {
        TryKind(out WaterStampKind stampKind);
        TryCategory(out WaterStampCategory stampCategory);

        var branchList = new List<WaterStampBranch>();

        if (branches != null)
        {
            foreach (WaterStampManifestBranch branch in branches)
            {
                branch.TryKind(out WaterStampBranchKind branchKind);
                branchList.Add(new WaterStampBranch(branchKind, new Vector2(branch.position[0], branch.position[1]), new Vector2(branch.direction[0], branch.direction[1]).normalized));
            }
        }

        var biomes = new List<BiomeType>(Biomes(warnings));

        return new WaterStampDefinition(id, name, stampKind, stampCategory, mask, resolution, Footprint, recommended_depth_m, recommended_bank_width_m,
            stampKind == WaterStampKind.River ? nominal_channel_width_m : 0f,
            entry != null && entry.Length == 2 ? new Vector2(entry[0], entry[1]) : Vector2.zero,
            exit != null && exit.Length == 2 ? new Vector2(exit[0], exit[1]) : Vector2.zero,
            branchList, allow_mirror, min_scale, max_scale, weight, biomes, sha256_raw16);
    }
}
