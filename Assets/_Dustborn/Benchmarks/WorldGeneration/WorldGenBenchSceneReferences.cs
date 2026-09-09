using UnityEngine;

public class WorldGenBenchSceneReferences : MonoBehaviour
{
    [SerializeField] private WorldGenBenchFixture _fixture;

    public WorldGenBenchFixture Fixture => _fixture;

#if UNITY_EDITOR
    public void EditorSetup(WorldGenBenchFixture fixture)
    {
        _fixture = fixture;
    }
#endif
}
