using UnityEngine;
using System;
using System.Collections.Generic;

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Painter
{
    using Data;

    public class PaintableObjectData
    {
        public Action DataChangedAction;

        public MaterialDataManager DataManager;
        public EMaxLayers MaxLayers;

        public MeshRenderer MeshRenderer;

        public List<RenderTexture> RenderTextures;
        public List<Texture2D> ControlTextures;

        public RenderTexture HolesRenderTexture;
        public Texture2D HolesTexture;

        public RenderTexture PositionMap;
    }
}