using System;
using System.Collections.Generic;
using System.Reflection;

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
        private static Func<string, string, string, int, int, string> _run;
        private static Func<string> _operations;
        private static Func<string> _environment;
        private static Func<string> _catalog;
        private static Action<string, string, string> _record;
        private static Action<string, string, string> _startRuntime;
        private static Func<bool> _runtimeFinished;
        private static Func<string> _runtimeResult;
        private static Action _stopRuntime;
        private static string _failure;

        public static bool Available => Bind() == null;

        public static string Failure => Bind();

        public static string Run(string op, string profile, string args, int warmup, int samples)
        {
            Require();
            return _run(op, profile, args, warmup, samples);
        }

        public static string Operations()
        {
            Require();
            return _operations();
        }

        public static string Environment()
        {
            Require();
            return _environment();
        }

        public static string CatalogCsv()
        {
            Require();
            return _catalog();
        }

        public static void Record(string catalogId, string caseArgs, string result)
        {
            Require();
            _record(catalogId, caseArgs, result);
        }

        public static void StartRuntime(string scenario, string profile, string args)
        {
            Require();
            _startRuntime(scenario, profile, args);
        }

        public static bool RuntimeFinished()
        {
            Require();
            return _runtimeFinished();
        }

        public static string RuntimeResult()
        {
            Require();
            return _runtimeResult();
        }

        public static void StopRuntime()
        {
            Require();
            _stopRuntime();
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

        private static void Require()
        {
            string failure = Bind();

            if (failure != null)
                throw new InvalidOperationException(failure);
        }

        private static string Bind()
        {
            if (_run != null)
                return null;

            if (_failure != null)
                return _failure;

            try
            {
                Type bench = Find("WorldGenBench");
                Type recorder = Find("WorldGenBenchRecorder");
                Type runtime = Find("WorldGenBenchRuntime");

                _run = (Func<string, string, string, int, int, string>)Delegate.CreateDelegate(
                    typeof(Func<string, string, string, int, int, string>), bench.GetMethod("Run", Public));

                _operations = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), bench.GetMethod("Operations", Public));
                _environment = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), bench.GetMethod("Environment", Public));
                _catalog = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), bench.GetMethod("Catalog", Public));

                _record = (Action<string, string, string>)Delegate.CreateDelegate(
                    typeof(Action<string, string, string>), recorder.GetMethod("Record", Public));

                _startRuntime = (Action<string, string, string>)Delegate.CreateDelegate(
                    typeof(Action<string, string, string>), runtime.GetMethod("Start", Public));

                _stopRuntime = (Action)Delegate.CreateDelegate(typeof(Action), runtime.GetMethod("Stop", Public));

                PropertyInfo finished = runtime.GetProperty("Finished", Public);
                PropertyInfo result = runtime.GetProperty("Result", Public);

                _runtimeFinished = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), finished.GetGetMethod());
                _runtimeResult = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), result.GetGetMethod());

                return null;
            }
            catch (Exception exception)
            {
                _failure = "The world generation benchmark bridge is not available: " + exception.Message;
                return _failure;
            }
        }

        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Static;

        private static Type Find(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);

                if (type != null)
                    return type;
            }

            throw new InvalidOperationException($"Type '{name}' was not found in any loaded assembly.");
        }
    }
}
