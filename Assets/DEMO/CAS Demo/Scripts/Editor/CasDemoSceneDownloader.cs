// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.Tools;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using UnityEditor;

namespace CAS_Demo.Scripts.Editor
{
    public class CasDemoSceneDownloader : CasDemoDownloaderTool
    {
        private int _selectedIndex = 1;
        
        private string[] _urls = new[]
        {
            "https://github.com/kinemation/demoes/releases/download/cas/CAS_Demo_Scene_URP.unitypackage",
            "https://github.com/kinemation/demoes/releases/download/cas/CAS_Demo_Scene_HDRP.unitypackage"
        };

        private string[] _renderPipelines = new[]
        {
            "URP",
            "HDRP"
        };
        
        protected override string GetPackageUrl()
        {
            return _urls[_selectedIndex];
        }

        protected override string GetPackageFileName()
        {
            return "CasDemoScene";
        }

        public override string GetToolName()
        {
            return "Demo Scene";
        }

        public override void Render()
        {
            _selectedIndex = EditorGUILayout.Popup("Render Pipeline", _selectedIndex, _renderPipelines);
            base.Render();
        }

        public override string GetToolDescription()
        {
            return "Demo scene for Character Animation System.";
        }

        protected override List<ContentLicense> GetContentLicenses()
        {
            return null;
        }
    }
}