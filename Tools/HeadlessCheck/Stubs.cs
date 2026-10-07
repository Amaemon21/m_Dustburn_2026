using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x:0}, {y:0})";
        public float magnitude => Mathf.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        private static readonly Vector2 zeroVector = new(0f, 0f);
        private static readonly Vector2 oneVector = new(1f, 1f);
        public static Vector2 zero => zeroVector;
        public static Vector2 one => oneVector;
        public void Normalize() { float m = magnitude; if (m > 1E-05f) { x /= m; y /= m; } else { x = 0f; y = 0f; } }
        public static Vector2 Normalize(in Vector2 value) { float m = value.magnitude; return m > 1E-05f ? new Vector2(value.x / m, value.y / m) : zeroVector; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator *(float b, Vector2 a) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static Vector2 Min(Vector2 a, Vector2 b) => new(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public Vector2 normalized { get { float m = magnitude; return m > 1E-05f ? new Vector2(x / m, y / m) : zero; } }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        private static readonly Vector3 upVector = new(0f, 1f, 0f);
        private static readonly Vector3 forwardVector = new(0f, 0f, 1f);
        private static readonly Vector3 zeroVector = new(0f, 0f, 0f);
        public static Vector3 up => upVector;
        public static Vector3 forward => forwardVector;
        public static Vector3 zero => zeroVector;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator *(float b, Vector3 a) => a * b;
        public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
        public override string ToString() => $"({x:0.##}, {y:0.##}, {z:0.##})";
        public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return a + (b - a) * t; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)System.Math.Sqrt(sqrMagnitude);
        public void Normalize() { float m = magnitude; if (m > 1e-5f) { x /= m; y /= m; z /= m; } else { x = y = z = 0f; } }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Vector2 center => new(x + width * 0.5f, y + height * 0.5f);
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax)
            => new() { x = xmin, y = ymin, width = xmax - xmin, height = ymax - ymin };
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color operator *(Color c, float v) => new Color(c.r * v, c.g * v, c.b * v, c.a * v);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public static readonly float Epsilon = float.Epsilon;
        public const float PI = 3.14159265f;
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Pow(float v, float p) => (float)Math.Pow(v, p);
        public static float Abs(float v) => (float)Math.Abs(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static int Abs(int v) => Math.Abs(v);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Min(float a, float b, float c) => Min(Min(a, b), c);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Max(float a, float b, float c) => Max(Max(a, b), c);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1e-6f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Exp(float p) => (float)Math.Exp(p);
        public static float Log(float v, float b) => (float)Math.Log(v, b);
        public static float Log(float v) => (float)Math.Log(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float Acos(float v) => (float)Math.Acos(v);
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float Tan(float v) => (float)Math.Tan(v);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float DeltaAngle(float a, float b) { float d = (b - a) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }
    }

    public static class Random
    {
        public static int Range(int a, int b) => a;
    }

    public class Object
    {
        public string name;
        public static bool operator ==(Object a, object b) => ReferenceEquals(a, b) || (a is null && b is null);
        public static bool operator !=(Object a, object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
    }

    public class Transform : Object
    {
        public Vector3 position;
        public Matrix4x4 worldToLocalMatrix => default;
        public Matrix4x4 localToWorldMatrix => default;
        public Vector3 localPosition;
        public int childCount => 0;
        public Transform GetChild(int i) => null;
        public void SetParent(Transform p, bool worldPositionStays) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { }
        public GameObject gameObject => null;
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 center, Vector3 size) { min = center; max = center; }
        public Vector3 size => max - min;
        public Vector3 center => (min + max) * 0.5f;
        public void Encapsulate(Vector3 point) { }
    }

    public struct Matrix4x4
    {
        public Vector3 MultiplyPoint3x4(Vector3 point) => point;
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a;
    }

    public class Mesh : Object
    {
        public Bounds bounds => default;
        public bool isReadable => true;
    }

    public class MeshRenderer : Renderer { }

    public enum LODFadeMode { None, CrossFade, SpeedTree }

    public struct LOD
    {
        public float screenRelativeTransitionHeight;
        public float fadeTransitionWidth;
        public Renderer[] renderers;
    }

    public class LODGroup : Component
    {
        public LODFadeMode fadeMode { get; set; }
        public bool animateCrossFading { get; set; }
        public LOD[] GetLODs() => new LOD[0];
        public void SetLODs(LOD[] levels) { }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class MeshCollider : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Renderer : Component
    {
        public bool enabled;
    }

    public class GameObject : Object
    {
        public Transform transform => null;
        public T AddComponent<T>() where T : Component, new() => new T();
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public SceneManagement.Scene scene => default;
    }

    namespace SceneManagement
    {
        public struct Scene { }
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public bool TryGetComponent<T>(out T component) { component = default; return false; }
    }

    public class Behaviour : Component { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleComponentAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponentAttribute : Attribute
    {
        public RequireComponentAttribute(Type type) { }
    }

    public class MonoBehaviour : Behaviour
    {
        public static GameObject Instantiate(GameObject o, Vector3 p, Quaternion r, Transform parent) => o;
        public static GameObject Instantiate(GameObject o, Transform parent) => o;
    }

    public class TextAsset : Object
    {
        public TextAsset() { }
        public TextAsset(byte[] data) { bytes = data; }
        public byte[] bytes;
        public long dataSize => bytes == null ? 0 : bytes.Length;
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => default;
    }

    public static class Debug
    {
        public static void Log(object m, Object c = null) { System.Console.WriteLine("[log] " + m); }
        public static void LogWarning(object m, Object c = null) { System.Console.WriteLine("[warn] " + m); }
        public static void LogError(object m, Object c = null) { System.Console.WriteLine("[error] " + m); }
    }

    public static class Application
    {
        public static bool isPlaying => false;
        public static string persistentDataPath => "";
    }

    public class UnityException : Exception { }

    public enum FilterMode { Point, Bilinear }
    public enum TextureWrapMode { Clamp, Repeat }
    public enum TextureFormat { RGBA32 }

    public class Texture : Object { }

    public class Texture2D : Texture
    {
        public int width, height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Texture2D(int w, int h, TextureFormat f, bool mips) { width = w; height = h; }
        public Texture2D(int w, int h, TextureFormat f, bool mips, bool linear) { width = w; height = h; }
        public void SetPixels32(Color32[] p) { }
        public Color32[] GetPixels32() => Array.Empty<Color32>();
        public void Apply(bool a, bool b) { }
        public byte[] EncodeToPNG() => Array.Empty<byte>();
    }

    public class TerrainLayer : Object { }

    public enum DetailRenderMode { GrassBillboard, Grass, VertexLit }

    public enum DetailScatterMode { InstanceCountMode, CoverageMode }

    public class DetailPrototype
    {
        public GameObject prototype;
        public Texture2D prototypeTexture;
        public bool usePrototypeMesh;
        public bool useInstancing;
        public DetailRenderMode renderMode;
        public float minWidth, maxWidth, minHeight, maxHeight;
        public float noiseSpread;
        public int noiseSeed;
        public Color healthyColor, dryColor;
        public bool Validate(out string errorMessage) { errorMessage = null; return true; }
    }

    public class TreePrototype
    {
        public GameObject prefab;
        public float bendFactor;
    }

    public struct TreeInstance
    {
        public Vector3 position;
        public float widthScale, heightScale, rotation;
        public int prototypeIndex;
        public Color color, lightmapColor;
    }

    public class TerrainData : Object
    {
        public Vector3 size;
        public int alphamapResolution;
        public int heightmapResolution;
        public int baseMapResolution;
        public int detailResolution;
        public TerrainLayer[] terrainLayers;
        public DetailPrototype[] detailPrototypes;
        public void SetDetailResolution(int resolution, int perPatch) { detailResolution = resolution; }
        public void SetDetailScatterMode(DetailScatterMode mode) { }
        public void SetAlphamaps(int x, int y, float[,,] maps) { }
        public void SetDetailLayer(int x, int y, int layer, int[,] counts) { }
        public void SetHeights(int x, int y, float[,] heights) { }
        public float GetSteepness(float u, float v) => 0f;
        public float GetInterpolatedHeight(float u, float v) => 0f;
        public TreePrototype[] treePrototypes;
        public void SetTreeInstances(TreeInstance[] instances, bool snapToHeightmap) { }
    }

    public class Terrain : Component
    {
        public TerrainData terrainData;
        public static Terrain[] activeTerrains => Array.Empty<Terrain>();
        public float SampleHeight(Vector3 p) => 0f;
    }

    public class Material : Object { }

    [AttributeUsage(AttributeTargets.All)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.All)] public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.All)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.All)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.All)] public class MinAttribute : Attribute { public MinAttribute(float v) { } }
    [AttributeUsage(AttributeTargets.All)] public class CreateAssetMenuAttribute : Attribute { public string fileName; public string menuName; }

    namespace Rendering
    {
        public enum CompareFunction { Always }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public enum ImportAssetOptions { ForceUpdate }
    public enum TextureImporterType { Default }
    public enum TextureImporterNPOTScale { None }
    public enum TextureImporterCompression { Uncompressed }

    public class AssetImporter
    {
        public static AssetImporter GetAtPath(string p) => null;
    }

    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType;
        public TextureImporterNPOTScale npotScale;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public bool mipmapEnabled;
        public bool sRGBTexture;
        public bool isReadable;
        public int maxTextureSize;
        public TextureImporterCompression textureCompression;
        public void SaveAndReimport() { }
    }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object => default;
        public static void CreateAsset(UnityEngine.Object o, string p) { }
        public static void SaveAssets() { }
        public static void ImportAsset(string p, ImportAssetOptions o) { }
        public static bool Contains(UnityEngine.Object o) => false;
        public static string GetAssetPath(UnityEngine.Object o) => "";
        public static string GenerateUniqueAssetPath(string p) => p;
        public static void SaveAssetIfDirty(UnityEngine.Object o) { }
    }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object o) { }
    }

    public static class PrefabUtility
    {
        public static UnityEngine.Object InstantiatePrefab(UnityEngine.Object prefab, Transform parent) => prefab;
    }

    public static class Undo
    {
        public static void RegisterCreatedObjectUndo(UnityEngine.Object o, string name) { }
    }

    namespace SceneManagement
    {
        public static class EditorSceneManager
        {
            public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) { }
        }
    }

    public static class Handles
    {
        public static Color color;
        public static UnityEngine.Rendering.CompareFunction zTest;
        public static void DrawWireDisc(Vector3 c, Vector3 n, float r) { }
        public static void DrawLine(Vector3 a, Vector3 b) { }
        public static void DrawAAPolyLine(float w, Vector3[] p) { }
        public static void Label(Vector3 position, string text) { }
    }
}

namespace NaughtyAttributes
{
    using System;

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class BoxGroupAttribute : Attribute { public BoxGroupAttribute(string n) { } }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class FoldoutAttribute : Attribute { public FoldoutAttribute(string n) { } }
    [AttributeUsage(AttributeTargets.All)] public class InfoBoxAttribute : Attribute { public InfoBoxAttribute(string n) { } }
    [AttributeUsage(AttributeTargets.All)] public class RequiredAttribute : Attribute { public RequiredAttribute(string n = null) { } }
    [AttributeUsage(AttributeTargets.All)] public class ShowNativePropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All)] public class ButtonAttribute : Attribute { public ButtonAttribute(string n = null) { } }
    [AttributeUsage(AttributeTargets.All)] public class MinValueAttribute : Attribute { public MinValueAttribute(float v) { } public MinValueAttribute(int v) { } }
    [AttributeUsage(AttributeTargets.All)] public class DropdownAttribute : Attribute { public DropdownAttribute(string n) { } }
    [AttributeUsage(AttributeTargets.All)] public class ResizableTextAreaAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All)] public class LabelAttribute : Attribute { public LabelAttribute(string n) { } }
    [AttributeUsage(AttributeTargets.All)] public class HorizontalLineAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All)] public class ShowAssetPreviewAttribute : Attribute { public ShowAssetPreviewAttribute(int w = 64, int h = 64) { } }
    [AttributeUsage(AttributeTargets.All)] public class ShowIfAttribute : Attribute { public ShowIfAttribute(string n) { } }
    [AttributeUsage(AttributeTargets.All)] public class ReadOnlyAttribute : Attribute { }

    public class DropdownList<T> : List<KeyValuePair<string, T>>
    {
        public void Add(string name, T value) => Add(new KeyValuePair<string, T>(name, value));
    }
}

namespace Unity.Profiling
{
    public struct ProfilerMarker
    {
        public ProfilerMarker(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public void Begin()
        {
        }

        public void End()
        {
        }
    }
}
