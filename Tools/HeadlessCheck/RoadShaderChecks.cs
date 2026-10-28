using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static class RoadShaderChecks
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenInclude(IntPtr self, uint kind, IntPtr name, IntPtr parent, out IntPtr data, out uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CloseInclude(IntPtr self, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate UIntPtr BlobSize(IntPtr self);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(byte[] source, UIntPtr bytes, string name, IntPtr defines, IntPtr include,
        string entry, string profile, uint flags, uint flags2, out IntPtr code, out IntPtr errors);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int D3DPreprocess(byte[] source, UIntPtr bytes, string name, IntPtr defines, IntPtr include,
        out IntPtr text, out IntPtr errors);

    private sealed class Includes : IDisposable
    {
        private readonly string _root;
        private readonly Dictionary<string, string> _packages = new();
        private readonly Dictionary<IntPtr, string> _files = new();
        private readonly OpenInclude _open;
        private readonly CloseInclude _close;
        private readonly IntPtr _table;
        public IntPtr Pointer { get; }
        public string Error { get; private set; }

        public Includes(string root)
        {
            _root = root;

            foreach (string directory in Directory.GetDirectories(Path.Combine(root, "Library", "PackageCache")))
                _packages[Path.GetFileName(directory).Split('@')[0]] = directory;

            _open = Open;
            _close = Close;
            _table = Marshal.AllocHGlobal(IntPtr.Size * 2);
            Marshal.WriteIntPtr(_table, 0, Marshal.GetFunctionPointerForDelegate(_open));
            Marshal.WriteIntPtr(_table, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_close));
            Pointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, _table);
        }

        private int Open(IntPtr self, uint kind, IntPtr name, IntPtr parent, out IntPtr data, out uint bytes)
        {
            data = IntPtr.Zero;
            bytes = 0;

            try
            {
                string file = Marshal.PtrToStringAnsi(name).Replace('/', Path.DirectorySeparatorChar);
                string path;

                if (file.StartsWith("Packages" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    string[] parts = file.Split(Path.DirectorySeparatorChar, 3);
                    string package = _packages.TryGetValue(parts[1], out string cached) ? cached : Path.Combine(_root, "Packages", parts[1]);
                    path = Path.Combine(package, parts[2]);
                }
                else if (_files.TryGetValue(parent, out string parentFile))
                    path = Path.Combine(Path.GetDirectoryName(parentFile), file);
                else
                    path = Path.Combine(_root, file);

                string text = Regex.Replace(File.ReadAllText(path), @"(?m)(#define\s+\w+)\(\)", "$1(unused)");
                text = Regex.Replace(text, @"\b([A-Z][A-Z0-9_]+)\(\)", "$1(0)");
                if (Path.GetFileName(path) == "UniversalDOTSInstancing.hlsl")
                    text = Regex.Replace(text, @"(?m)^#define unity_SpecCube0_HDR[^\r\n]*", "#define unity_SpecCube0_HDR unity_DOTS_SpecCube0_HDR");
                byte[] content = Encoding.UTF8.GetBytes(text);
                data = Marshal.AllocHGlobal(content.Length);
                Marshal.Copy(content, 0, data, content.Length);
                bytes = (uint)content.Length;
                _files[data] = path;
                return 0;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                return unchecked((int)0x80004005);
            }
        }

        private int Close(IntPtr self, IntPtr data)
        {
            _files.Remove(data);
            Marshal.FreeHGlobal(data);
            return 0;
        }

        public void Dispose()
        {
            foreach (IntPtr data in _files.Keys)
                Marshal.FreeHGlobal(data);

            Marshal.FreeHGlobal(Pointer);
            Marshal.FreeHGlobal(_table);
            GC.KeepAlive(_open);
            GC.KeepAlive(_close);
        }
    }

    public static void Run()
    {
        string root = Directory.GetCurrentDirectory();

        while (!File.Exists(Path.Combine(root, "Packages", "manifest.json")))
            root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Run inside the Unity project.");

        string path = Path.Combine(root, "Assets", "_Dustborn", "Content", "Shaders", "RoadAsphalt.shader");
        string shader = File.ReadAllText(path);
        int first = shader.IndexOf("HLSLINCLUDE", StringComparison.Ordinal) + "HLSLINCLUDE".Length;
        int last = shader.IndexOf("ENDHLSL", first, StringComparison.Ordinal);
        string body = shader.Substring(first, last - first);
        string[] variants =
        {
            "",
            "#define _MAIN_LIGHT_SHADOWS 1\n#define _ADDITIONAL_LIGHTS 1\n#define _ADDITIONAL_LIGHT_SHADOWS 1\n#define _SHADOWS_SOFT 1\n#define FOG_LINEAR 1\n",
            "#define _MAIN_LIGHT_SHADOWS_CASCADE 1\n#define _ADDITIONAL_LIGHTS 1\n#define _CLUSTER_LIGHT_LOOP 1\n#define _SHADOWS_SOFT_HIGH 1\n#define FOG_EXP2 1\n",
            "#define _MAIN_LIGHT_SHADOWS_SCREEN 1\n#define _ADDITIONAL_LIGHTS_VERTEX 1\n#define _GBUFFER_NORMALS_OCT 1\n",
            "#define INSTANCING_ON 1\n",
            "#define DOTS_INSTANCING_ON 1\n"
        };

        foreach (string variant in variants)
        {
            foreach (string entry in new[] { "RoadVertex", "RoadFragment", "RoadDepth", "RoadDepthNormals" })
            {
                string stage = entry == "RoadVertex" ? "SHADER_STAGE_VERTEX" : "SHADER_STAGE_FRAGMENT";
                string header = "#define UNITY_UNIFIED_SHADER_PRECISION_MODEL 1\n#define SHADER_API_D3D11 1\n#define UNITY_COMPILER_HLSL 1\n#define SHADER_TARGET 45\n#define UNITY_VERSION 600030\n#define " + stage + " 1\n";
                byte[] source = Encoding.UTF8.GetBytes(header + variant + body);
                using var include = new Includes(root);
                int result = D3DCompile(source, (UIntPtr)source.Length, path, IntPtr.Zero, include.Pointer, entry,
                    entry == "RoadVertex" ? "vs_5_0" : "ps_5_0", 1 << 11, 0, out IntPtr code, out IntPtr errors);
                string diagnostic = ReadBlob(errors);

                if (code != IntPtr.Zero)
                    Marshal.Release(code);

                if (errors != IntPtr.Zero)
                    Marshal.Release(errors);

                if (result < 0)
                {
                    using var preprocessing = new Includes(root);
                    D3DPreprocess(source, (UIntPtr)source.Length, path, IntPtr.Zero, preprocessing.Pointer, out IntPtr expanded, out IntPtr preprocessingErrors);
                    File.WriteAllText(Path.Combine(root, "Tools", "HeadlessCheck", "obj", "road_shader_preprocessed.hlsl"), ReadBlob(expanded));

                    if (expanded != IntPtr.Zero)
                        Marshal.Release(expanded);

                    if (preprocessingErrors != IntPtr.Zero)
                        Marshal.Release(preprocessingErrors);

                    throw new InvalidOperationException($"{entry}: {include.Error}\n{diagnostic}");
                }
            }
        }

        Console.WriteLine($"Road shader compiled offline with installed URP headers: {variants.Length * 4} Direct3D shader stages including shadows, fog, clustered lights, oct normals and DOTS instancing.");
    }

    private static string ReadBlob(IntPtr blob)
    {
        if (blob == IntPtr.Zero)
            return "";

        IntPtr table = Marshal.ReadIntPtr(blob);
        var pointer = Marshal.GetDelegateForFunctionPointer<BlobPointer>(Marshal.ReadIntPtr(table, IntPtr.Size * 3));
        var size = Marshal.GetDelegateForFunctionPointer<BlobSize>(Marshal.ReadIntPtr(table, IntPtr.Size * 4));
        return Marshal.PtrToStringAnsi(pointer(blob), (int)size(blob).ToUInt64());
    }
}
