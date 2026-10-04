using System;
using System.Collections.Generic;

namespace Dustborn.WorldGen.Testing
{
    public sealed class WorldGenCase
    {
        public string Id;
        public string Priority;
        public string Mode;
        public string Op;
        public string[] Cases;
        public string Note;
    }

    public static class WorldGenBridge
    {
        public static string Run(string op, string profile, string args, int warmup, int samples)
        {
            return WorldGenBench.Run(op, profile, args, warmup, samples);
        }

        public static string Operations()
        {
            return WorldGenBench.Operations();
        }

        public static string Environment()
        {
            return WorldGenBench.Environment();
        }

        public static string CatalogCsv()
        {
            return WorldGenBench.Catalog();
        }

        public static void Record(string catalogId, string caseArgs, string result)
        {
            WorldGenBenchRecorder.Record(catalogId, caseArgs, result);
        }

        public static void StartRuntime(string scenario, string profile, string args)
        {
            WorldGenBenchRuntime.Start(scenario, profile, args);
        }

        public static bool RuntimeFinished()
        {
            return WorldGenBenchRuntime.Finished;
        }

        public static string RuntimeResult()
        {
            return WorldGenBenchRuntime.Result;
        }

        public static void StopRuntime()
        {
            WorldGenBenchRuntime.Stop();
        }

        public static List<WorldGenCase> Catalog()
        {
            var cases = new List<WorldGenCase>();
            string[] lines = CatalogCsv().Split('\n');

            for (int index = 1; index < lines.Length; index++)
            {
                string line = lines[index].Trim('\r', ' ');

                if (line.Length == 0)
                    continue;

                string[] parts = Split(line);

                if (parts.Length < 7)
                    continue;

                cases.Add(new WorldGenCase
                {
                    Id = parts[0],
                    Priority = parts[1],
                    Mode = parts[2],
                    Op = parts[3],
                    Cases = parts[5].Split('|'),
                    Note = parts[6]
                });
            }

            return cases;
        }

        public static string Status(string result)
        {
            return Field(result, "status");
        }

        public static string Error(string result)
        {
            return Field(result, "error");
        }

        public static double Number(string result, string key)
        {
            string raw = Field(result, key);
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : double.NaN;
        }

        private static string Field(string json, string key)
        {
            if (json == null)
                return null;

            string token = "\"" + key + "\":";
            int start = json.IndexOf(token, StringComparison.Ordinal);

            if (start < 0)
                return null;

            start += token.Length;

            if (start >= json.Length)
                return null;

            if (json[start] == '"')
            {
                int end = json.IndexOf('"', start + 1);
                return end < 0 ? null : json.Substring(start + 1, end - start - 1);
            }

            int stop = start;

            while (stop < json.Length && json[stop] != ',' && json[stop] != '}' && json[stop] != ']')
                stop++;

            string raw = json.Substring(start, stop - start).Trim();
            return raw == "null" ? null : raw;
        }

        private static string[] Split(string line)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;

            for (int index = 0; index < line.Length; index++)
            {
                char symbol = line[index];

                if (symbol == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (symbol == ',' && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }

                current.Append(symbol);
            }

            parts.Add(current.ToString());
            return parts.ToArray();
        }
    }
}
