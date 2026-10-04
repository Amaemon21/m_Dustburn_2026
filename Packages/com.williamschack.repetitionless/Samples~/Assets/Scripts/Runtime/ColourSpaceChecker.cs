using System.Collections.Generic;
using UnityEngine;

namespace Repetitionless.Runtime.Samples
{
#if UNITY_EDITOR
    using Editor.Processors;
#endif
    // Used to check if the colour space matches the one in the sample scene
    // Updates it if they are mismatched

    [ExecuteInEditMode]
    public class ColourSpaceChecker : MonoBehaviour
    {
        [SerializeField] private List<Material> _repetitionlessMaterials = new List<Material>();

        void OnEnable()
        {
#if UNITY_EDITOR
            // Update the material colour space if it is different
            RepetitionlessColourSpaceUpdater.RepackMaterialsIfColourSpaceChanged(_repetitionlessMaterials, false);
#endif
        }
    }
}