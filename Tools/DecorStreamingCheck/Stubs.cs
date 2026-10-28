using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Dead;
        public static int Clones;
        private static readonly List<GameObject> Pending = new();
        public static readonly List<GameObject> Objects = new();
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (a is null || a.Dead) && (b is null || b.Dead);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
        public int GetInstanceID() => GetHashCode();
        public static GameObject Instantiate(GameObject source, Transform parent)
        {
            Clones++;
            var copy = new GameObject(source.name) { Profile = source.Profile, activeSelf = source.activeSelf };
            copy.transform.SetParent(parent, false);
            foreach (Component component in source.Components)
            {
                if (component is MeshRenderer renderer)
                {
                    MeshRenderer target = copy.AddComponent<MeshRenderer>();
                    target.sharedMaterials = renderer.sharedMaterials;
                    target.localBounds = renderer.localBounds;
                }
                else if (component is MeshFilter filter)
                    copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                else if (component is BlockView)
                    copy.AddComponent<BlockView>();
            }
            return copy;
        }
        public static GameObject Instantiate(GameObject source, Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject copy = Instantiate(source, parent);
            copy.transform.SetPositionAndRotation(position, rotation);
            return copy;
        }
        public static void Destroy(Object value)
        {
            if (value is GameObject go && go != null && !Pending.Contains(go))
                Pending.Add(go);
        }
        public static void FlushDestroy()
        {
            foreach (GameObject go in Pending.ToArray())
                DestroyNow(go);
            Pending.Clear();
        }
        private static void DestroyNow(GameObject go)
        {
            if (go == null)
                return;
            foreach (Transform child in go.transform.Children.ToArray())
                DestroyNow(child.gameObject);
            foreach (Component component in go.Components)
            {
                component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
                component.Dead = true;
            }
            go.transform.SetParent(null, false);
            go.Dead = true;
        }
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sorting) where T : Component
            => Objects.Where(go => go != null && go.activeInHierarchy).SelectMany(go => go.Components.OfType<T>()).ToArray();
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public PrefabProfile Profile;
        public bool activeSelf = true;
        public int layer;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "Object")
        {
            this.name = name;
            transform = AddComponent<Transform>();
            Objects.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var value = new T { gameObject = this };
            Components.Add(value);
            return value;
        }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public bool TryGetComponent<T>(out T value) where T : class { value = GetComponent<T>(); return value != null; }
        public T[] GetComponents<T>() => Components.OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>() => Components.OfType<T>().ToArray();
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
        public void GetComponentsInChildren<T>(bool inactive, List<T> output)
        {
            output.Clear();
            Collect(this, output);
        }
        private static void Collect<T>(GameObject go, List<T> output)
        {
            output.AddRange(go.Components.OfType<T>());
            foreach (Transform child in go.transform.Children)
                Collect(child.gameObject, output);
        }
        public void SetActive(bool active) => activeSelf = active;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public bool TryGetComponent<T>(out T value) where T : class => gameObject.TryGetComponent(out value);
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Transform parent;
        public Vector3 position, localScale;
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public void SetParent(Transform target, bool world)
        {
            parent?.Children.Remove(this);
            parent = target;
            parent?.Children.Add(this);
        }
        public void SetPositionAndRotation(Vector3 value, Quaternion rotation) => position = value;
        public bool IsChildOf(Transform value) => this == value || parent != null && parent.IsChildOf(value);
    }
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new(0f, 1f, 0f);
        public static Vector3 down => new(0f, -1f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float scale) => new(a.x * scale, a.y * scale, a.z * scale);
    }
    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion FromToRotation(Vector3 a, Vector3 b) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
        public static Vector3 operator *(Quaternion a, Vector3 b) => b;
    }
    public class Collider : Component { }
    public struct RaycastHit { public float distance; public Vector3 point, normal; public Collider collider; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public const int DefaultRaycastLayers = -5;
        public static int Queries, LastMask;
        public static Func<Vector3, RaycastHit> Hit;
        public static void SyncTransforms() { }
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] hits, float distance, int mask, QueryTriggerInteraction triggers)
        {
            Queries++;
            LastMask = mask;
            if (Hit == null)
                return 0;
            hits[0] = Hit(origin);
            return 1;
        }
    }
    public class LODGroup : Component
    {
        public void SetLODs(LOD[] lods) { }
        public void RecalculateBounds() { }
    }
    public struct LOD { public LOD(float height, Renderer[] renderers) { } }
    public static class Application { public static bool isPlaying = true; }
    public struct Bounds { public float Width; public void Expand(float value) => Width += value; }
    public struct Color { public static Color white => default; }
    public class Mesh : Object { public int subMeshCount; }
    public class Material : Object { }
    public class Texture2D : Object { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Renderer : Component
    {
        public Material[] sharedMaterials = Array.Empty<Material>();
        public bool enabled = true;
        public Bounds localBounds;
        public void GetSharedMaterials(List<Material> values) { values.Clear(); values.AddRange(sharedMaterials); }
    }
    public class MeshRenderer : Renderer { }
    public class SkinnedMeshRenderer : Renderer { public Mesh sharedMesh; }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new();
        public static void LogWarning(object message, Object context = null) => Warnings.Add(message.ToString());
        public static void Log(object message) { }
    }
    public sealed class SerializeField : Attribute { }
    public sealed class DisallowMultipleComponent : Attribute { }
    public enum FindObjectsInactive { Exclude }
    public enum FindObjectsSortMode { None }
    public static class Time { public static int frameCount; }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off, On } }
namespace UnityEditor
{
    public sealed class MenuItem : Attribute { public MenuItem(string path) { } }
    public static class PrefabUtility
    {
        public static UnityEngine.Object InstantiatePrefab(UnityEngine.GameObject prefab, UnityEngine.Transform parent)
            => UnityEngine.Object.Instantiate(prefab, parent);
    }
}
namespace NaughtyAttributes { public sealed class RequiredAttribute : Attribute { } }
namespace Unity.Profiling
{
    public readonly struct ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public Scope Auto() => default;
        public readonly struct Scope : IDisposable { public void Dispose() { } }
    }
}
namespace R3
{
    public sealed class Stream<T>
    {
        public Stream<T> Where(Func<T, bool> predicate) => this;
        public Stream<T> Take(int count) => this;
        public IDisposable Subscribe(Action<T> callback) => new Subscription();
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}
public interface IDamageable { }
public interface IDamageableView { }
public interface IDecorObject { }
public sealed class BlockConfig { }
public sealed class Block : IDamageable, IDisposable
{
    public readonly R3.Stream<int> Health = new();
    public bool Disposed;
    public Block(string id, BlockConfig config, int damage, Random random) { }
    public void Dispose() => Disposed = true;
}
public sealed class BlockService
{
    public readonly Dictionary<string, BlockView> Views = new();
    public IDamageable Resolve(BlockView view) => null;
    public void Detach(BlockView view)
    {
        if (view.Id != null && Views.TryGetValue(view.Id, out BlockView current) && current == view)
            Views.Remove(view.Id);
    }
}
public enum DecorScope { All, Grass, Trees, Rocks }
public enum DecorKind { Grass, Tree, Rock }
public struct DecorSurface { }
public struct DecorInstance
{
    public UnityEngine.Vector3 Position;
    public float Scale, Height, Shade;
    public int CellX, CellY;
}
public sealed class VoxelDecorLayer
{
    public UnityEngine.GameObject Prefab;
    public UnityEngine.Texture2D Card;
    public DecorKind Kind;
    public string Name = "Layer";
    public int Style;
}
public sealed class VoxelDecorPlacer
{
    public readonly List<VoxelDecorLayer> Layers = new();
    public readonly List<List<DecorInstance>> Data = new();
    public int Place(int layer, UnityEngine.Vector2 origin, float span, List<DecorInstance> output, DecorSurface frame)
    {
        output.AddRange(Data[layer]);
        return Data[layer].Count;
    }
}
public sealed class VoxelConfig
{
    public float GrassDistance;
    public int MaxLod(DecorKind kind) => 5;
}
public readonly struct DecorTint
{
    public const int LEVELS = 4;
    public bool IsNeutral => false;
    public int Level(float shade) => Math.Clamp((int)shade, 0, LEVELS - 1);
    public static DecorTint Of(VoxelDecorLayer layer) => default;
}
public sealed class GrassCardFactory : IDisposable
{
    public float Distance;
    public UnityEngine.Mesh Mesh => new();
    public UnityEngine.Material Material(int style) => new();
    public void Dispose() { }
}
public partial class MeshCombiner : IDisposable
{
    private readonly bool _card;
    private readonly DecorTint _tint;
    private readonly UnityEngine.Material[][][] _painted = new UnityEngine.Material[DecorTint.LEVELS][][];
    public PrefabProfile Profile { get; }
    public bool IsCard => _card;
    public static readonly List<int> Combined = new();
    public static readonly List<int> Instanced = new();
    public MeshCombiner(UnityEngine.GameObject prefab, DecorTint tint) { Profile = prefab.Profile; _tint = tint; }
    public MeshCombiner(UnityEngine.Mesh mesh, UnityEngine.Material material, DecorTint tint)
    { _card = true; Profile = new PrefabProfile { HasMesh = true, Readable = true, Vertices = 12 }; }
    public GrassCardMeshBuild ScheduleCards(IReadOnlyList<DecorInstance> values, int start, int count) => new(values, start, count);
    public void AttachCards(GrassCardMeshBuild build, UnityEngine.Transform parent, string name, UnityEngine.Rendering.ShadowCastingMode shadows)
    { foreach (int index in build.Keys) Combined.Add(index); }
    public static MeshCombiner Far(UnityEngine.GameObject prefab, DecorTint tint) => new(prefab, tint);
    public static UnityEngine.Quaternion Orientation(in DecorInstance instance) => default;
    public int Combine(IReadOnlyList<DecorInstance> values, int budget, UnityEngine.Transform parent, string name, UnityEngine.Rendering.ShadowCastingMode shadows)
    { foreach (DecorInstance value in values) Combined.Add(value.CellX); return 1; }
    public int Instance(IReadOnlyList<DecorInstance> values, UnityEngine.Transform parent, string name, UnityEngine.Rendering.ShadowCastingMode shadows, float distance)
    { foreach (DecorInstance value in values) Instanced.Add(value.CellX); return 1; }
    public int InstanceParts(IReadOnlyList<DecorInstance> values, UnityEngine.Transform parent, string name, UnityEngine.Rendering.ShadowCastingMode shadows, float distance)
        => Instance(values, parent, name, shadows, distance);
    private UnityEngine.Material[] Variants(UnityEngine.Material[] values, int level)
        => values.Select(source => new UnityEngine.Material { name = source.name + "/shade" + level }).ToArray();
    public void Dispose() { }
}
public sealed class GeneratedMesh : UnityEngine.Component
{
    public static void Destroy(UnityEngine.GameObject go) { go.SetActive(false); UnityEngine.Object.Destroy(go); }
}
public enum WorldGenStage { DecorPlace, DecorStep, DecorCombine, DecorInstance, DecorInstantiate, PropBatch, PropReground, GrassMeshSchedule, GrassMeshUpload }
public static class WorldGenProbe
{
    public readonly struct Span : IDisposable { public void Dispose() { } public void Finish(int value) { } }
    public static Span Measure(WorldGenStage stage) => default;
}
public sealed class GrassCardMeshBuild : IDisposable
{
    public readonly int[] Keys;
    public int Count => Keys.Length;
    public bool IsCompleted { get; private set; }
    public bool Disposed;
    public GrassCardMeshBuild(IReadOnlyList<DecorInstance> values, int start, int count)
        => Keys = Enumerable.Range(start, count).Select(index => values[index].CellX).ToArray();
    public void Complete() => IsCompleted = true;
    public void Dispose() { Complete(); Disposed = true; }
}
public sealed class PropPlacement
{
    public UnityEngine.GameObject Prefab;
    public UnityEngine.Vector3 Position;
    public float Yaw, GroundOffset;
    public bool TiltToGround;
}
public interface IWorldObjectHook { void Spawned(UnityEngine.GameObject instance, WorldObjectKey key); }
public readonly struct WorldObjectKey { public static WorldObjectKey Prop(int index) => default; }
