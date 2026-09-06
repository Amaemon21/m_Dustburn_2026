using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Repetitionless.Runtime.Samples
{
    public class TerrainSceneSettings : MonoBehaviour
    {
        [SerializeField] private Terrain[] _terrains;
        [SerializeField] private RepetitionlessTerrain _repetitionlessTerrain;

        [Space(10)]
        [SerializeField] private Material _repetitionlessMaterial;
        [SerializeField] private Material _litMaterial;

        [HideInInspector] public bool TerrainUsingRepetitionless = true;

        public void UpdateRepetitionlessTerrainMaterial()
        {
            if (TerrainUsingRepetitionless) {
                _repetitionlessTerrain.enabled = true;
                _repetitionlessTerrain.UpdateTerrainMaterial(_repetitionlessMaterial);
                _repetitionlessTerrain.UpdateMaterialTerrainTextures();
            } else {
                _repetitionlessTerrain.enabled = false;
                UpdateTerrainMaterials();
            }
        }

        private void UpdateTerrainMaterials()
        {
            Material currentMaterial = TerrainUsingRepetitionless ? _repetitionlessMaterial : _litMaterial;
            for (uint i = 0; i < _terrains.Length; i++) {
                _terrains[i].materialTemplate = currentMaterial;
            }
        }

        public void ToggleTerrainRepetitionless()
        {
            TerrainUsingRepetitionless = !TerrainUsingRepetitionless;

            if (_repetitionlessTerrain != null)
                UpdateRepetitionlessTerrainMaterial();
            else
                UpdateTerrainMaterials();

            // Update terrain layers to make them all equal in tiling
            // Repetitionless uses world UVs, regular uses local so they differ a bit 
            TerrainLayer[] layers = _terrains[0].terrainData.terrainLayers;
            foreach (TerrainLayer layer in layers) {
                layer.tileSize = TerrainUsingRepetitionless ? new Vector2(1, 1) : new Vector2(7, 7);
#if UNITY_EDITOR
                EditorUtility.SetDirty(layer);
#endif
            }

#if UNITY_EDITOR
            // Repaint scene and game view
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

#if UNITY_EDITOR
        public void SelectRepetitionlessMaterial()
        {
            Selection.activeObject = _repetitionlessMaterial;
        }
#endif
    }
}