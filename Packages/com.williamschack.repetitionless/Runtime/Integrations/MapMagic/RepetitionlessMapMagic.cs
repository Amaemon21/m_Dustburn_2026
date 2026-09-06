#if MAPMAGIC2
using UnityEngine;
using System.Collections.Generic;

using MapMagic.Core;
using MapMagic.Products;
using MapMagic.Terrains;

using Repetitionless.Runtime.Utilities;

namespace Repetitionless.Runtime.Integrations.MapMagic
{
    /// <summary>
    /// Handles Repetitionless materials interfacing with a MapMagicObject, automatically updating terrain textures and syncing the terrain layers to the material
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(MapMagicObject))]
    public class RepetitionlessMapMagic : MonoBehaviour
    {
        private MapMagicObject _mapMagicObject; 

        /// <summary>
        /// The MapMagicObject this component is referencing
        /// </summary>
        public MapMagicObject MapMagicObject => _mapMagicObject;

        [SerializeField] private Material _mainMaterial;

        /// <summary>
        /// The main material set in the inspector
        /// </summary>
        public Material MainMaterial => _mainMaterial;

        private Material _defaultTerrainMaterial;

        /// <summary>
        /// If the material is applied to draft (Low-Detail) terrains
        /// </summary>
        public bool ApplyToDraftTerrains = true;

        private void OnEnable()
        {
            // Sync material terrain layers in editor
            // Do this function on enable for all pinned tiles

            _mapMagicObject = GetComponent<MapMagicObject>();
            _defaultTerrainMaterial = RenderPipelineUtilities.GetDefaultTerrainMaterial();

            TerrainTile.OnTileApplied -= OnTileApplied;
            TerrainTile.OnTileApplied += OnTileApplied;

            SetupAllTiles();
        }

        private void OnDisable()
        {
            TerrainTile.OnTileApplied -= OnTileApplied;

#if UNITY_EDITOR
            DisableAllTiles();
#endif
        }

#if UNITY_EDITOR
        private void OnDestroy()
        {
            // OnDisable handles the rest and is called before this
            RemoveAllTiles();
        }
#endif

        private void OnTileApplied(TerrainTile tile, TileData data, StopToken stopToken)
        {
            SetupTile(tile);
        }

        private void ForEachTerrainInTile(TerrainTile tile, System.Action<Terrain, RepetitionlessTerrain> action)
        {
            if (tile == null || !tile.transform.IsChildOf(transform))
                    return;

            List<Terrain> terrains = new List<Terrain>() { tile.main.terrain };
            if (ApplyToDraftTerrains) terrains.Add(tile.draft.terrain);

            foreach (Terrain terrain in terrains) {
                RepetitionlessTerrain repetitionlessTerrain;
                terrain.TryGetComponent(out repetitionlessTerrain);

                action?.Invoke(terrain, repetitionlessTerrain);
            }
        }

        private void ForEachTerrain(System.Action<Terrain, RepetitionlessTerrain> action)
        {
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                ForEachTerrainInTile(tile, action);
            }
        }

        private void SetupAllTiles()
        {
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                SetupTile(tile);
            }
        }

        private void SetupTile(TerrainTile tile)
        {
            ForEachTerrainInTile(tile, (terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null) {
                    repetitionlessTerrain = terrain.gameObject.AddComponent<RepetitionlessTerrain>();
                }

                repetitionlessTerrain.enabled = true;

                if (_mainMaterial == null)
                    return;

                if (repetitionlessTerrain.MainMaterial == null) {
                    repetitionlessTerrain.UpdateTerrainMaterial(_mainMaterial);
                }

                repetitionlessTerrain.UpdateMaterialTerrainTextures();
                repetitionlessTerrain.AssignMaterialInstance();
            });
        }

        private void DisableAllTiles()
        {
            // Disable all repetitionless terrains and reset materials
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                DisableTile(tile);
            }
        }

        private void DisableTile(TerrainTile tile)
        {
            ForEachTerrainInTile(tile, (terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null)
                    return;

                repetitionlessTerrain.enabled = false;
                terrain.materialTemplate = _defaultTerrainMaterial;
            });
        }

        /// <summary>
        /// Removes the materials from every terrain
        /// </summary>
        public void RemoveAllTilesMaterials()
        {
            // Remove all repetitionless terrain components
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                RemoveTileMaterials(tile);
            }
        }

        private void RemoveTileMaterials(TerrainTile tile)
        {
            ForEachTerrainInTile(tile, (terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null)
                    return;

                repetitionlessTerrain.UpdateTerrainMaterial(null, true);
                terrain.materialTemplate = _defaultTerrainMaterial;
            });
        }

        private void RemoveAllTiles()
        {
            // Remove all repetitionless terrain components
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                RemoveTile(tile);
            }
        }

        private void RemoveTile(TerrainTile tile)
        {
            ForEachTerrainInTile(tile, (terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null)
                    return;

                DestroyImmediate(repetitionlessTerrain);
            });
        }

        /// <summary>
        /// Updates the terrain textures on the material instance of every terrain
        /// </summary>
        public void UpdateMaterialTerrainTextures()
        {
            // Call the same function on each RepetitionlessTerrain
            ForEachTerrain((terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null)
                    return;
                    
                repetitionlessTerrain.UpdateMaterialTerrainTextures();
            });
        }

        /// <summary>
        /// Creates a new material instance and updates every terrain
        /// </summary>
        /// <param name="material">
        /// The material that will be instanced
        /// </param>
        /// <param name="assignMaterial">
        /// If the material instance should be assigned to the terrains
        /// </param>
        public void UpdateTerrainMaterials(Material material, bool assignMaterial = true)
        {
            // Call the same function on each RepetitionlessTerrain
            ForEachTerrain((terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null)
                    return;
                    
                repetitionlessTerrain.UpdateTerrainMaterial(material, assignMaterial);
            });
        }

        /// <summary>
        /// Enables or Disables draft terrains based on ApplyToDraftTerrains
        /// </summary>
        public void UpdateDraftTerrains()
        {
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                if (tile == null) continue;

                Terrain draftTerrain = tile.draft.terrain;

                RepetitionlessTerrain repetitionlessTerrain;
                draftTerrain.TryGetComponent(out repetitionlessTerrain);

                if (repetitionlessTerrain == null)
                    continue;

                repetitionlessTerrain.enabled = ApplyToDraftTerrains;
                if (ApplyToDraftTerrains) {
                    SetupTile(tile);
                } else {
                    draftTerrain.materialTemplate = _defaultTerrainMaterial;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Gets the first terrain in the MapMagic grid
        /// </summary>
        /// <returns>
        /// The first active terrain in the MapMagic grid
        /// </returns>
        public Terrain GetFirstTerrain()
        {
            // Check one terrain, if its material has changed, we can assume all terrains have and reapply for all terrains
            if(_mapMagicObject.tiles.grid.Count == 0)
                return null;

            // Get the first tile out of the grid
            TerrainTile checkingTile = null;
            foreach (var kvp in _mapMagicObject.tiles.grid) {
                checkingTile = kvp.Value;
                break;
            }
            if (checkingTile == null) return null; // Shouldnt happen

            return checkingTile.ActiveTerrain;
        }

        /// <summary>
        /// Updates the main material and assigns it to every terrain
        /// </summary>
        /// <param name="mat">
        /// The material to use
        /// </param>
        public void AssignNewMaterial(Material mat)
        {
            _mainMaterial = mat;

            ForEachTerrain((terrain, repetitionlessTerrain) => {
                if (repetitionlessTerrain == null)
                    return;
                    
                repetitionlessTerrain.AssignMaterialInstance();
            });
        }

        /// <summary>
        /// Checks if a terrain is using the repetitionless material and if not, it re-assigns it to every terrain
        /// </summary>
        public void CheckAndUpdateMaterials()
        {
            if (_mainMaterial == null)
                return;

            Terrain mainTerrain = GetFirstTerrain();
            if (mainTerrain == null) return;

            RepetitionlessTerrain repetitionlessTerrain;
            mainTerrain.gameObject.TryGetComponent(out repetitionlessTerrain);
            if (repetitionlessTerrain == null) return;

            if (mainTerrain.materialTemplate == repetitionlessTerrain.MaterialInstance)
                return;

            // Update material for all terrains
            foreach (TerrainTile tile in _mapMagicObject.tiles.grid.Values) {
                SetupTile(tile);
            }
        }
#endif
    }
}
#endif