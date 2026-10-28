using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class KawaseBlur : ScriptableRendererFeature
{
    [System.Serializable]
    public class KawaseBlurSettings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        public Material blurMaterial = null;

        [Range(2,15)]
        public int blurPasses = 1;

        [Range(1,4)]
        public int downsample = 1;
        public bool copyToFramebuffer;
        public string targetName = "_blurTexture";
    }

    public KawaseBlurSettings settings = new KawaseBlurSettings();

    private static int _requests;

    private KawaseBlurPass _pass;

    public static Texture Result { get; private set; }

    public static void Request() => _requests++;

    public static void Release()
    {
        if (_requests > 0)
            _requests--;
    }

    public override void Create()
    {
        _pass?.Dispose();
        _pass = new KawaseBlurPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.blurMaterial == null || _requests == 0)
            return;

        if (!RendersToScreen(ref renderingData.cameraData))
            return;

        renderer.EnqueuePass(_pass);
    }

    private static bool RendersToScreen(ref CameraData camera)
    {
        return camera.cameraType == CameraType.Game
            && camera.renderType == CameraRenderType.Base
            && camera.targetTexture == null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
    }

    private class KawaseBlurPass : ScriptableRenderPass
    {
        private const string PASS_NAME = "KawaseBlur";
        private const float FIRST_OFFSET = 1.5f;

        private static readonly int BLIT_TEXTURE = Shader.PropertyToID("_BlitTexture");
        private static readonly int BLIT_SCALE_BIAS = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int OFFSET = Shader.PropertyToID("_offset");
        private static readonly Vector4 FULL_SCALE_BIAS = new(1f, 1f, 0f, 0f);
        private static readonly MaterialPropertyBlock PROPERTIES = new();

        private readonly Material _material;
        private readonly int _passes;
        private readonly int _downsample;
        private readonly bool _copyToFramebuffer;
        private readonly string _targetName;
        private readonly int _targetId;

        private RTHandle _result;

        private class BlurPassData
        {
            public TextureHandle Source;
            public Material Material;
            public float Offset;
        }

        public KawaseBlurPass(KawaseBlurSettings settings)
        {
            _material = settings.blurMaterial;
            _passes = settings.blurPasses;
            _downsample = Mathf.Max(1, settings.downsample);
            _copyToFramebuffer = settings.copyToFramebuffer;
            _targetName = settings.targetName;
            _targetId = Shader.PropertyToID(settings.targetName);
            renderPassEvent = settings.renderPassEvent;
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            if (_result != null && Result == _result.rt)
                Result = null;

            _result?.Release();
            _result = null;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
                return;

            var source = resources.activeColorTexture;
            var desc = BlurDescriptor(renderGraph.GetTextureDesc(source));

            desc.name = "_KawaseBlurPing";
            var ping = renderGraph.CreateTexture(desc);
            desc.name = "_KawaseBlurPong";
            var pong = renderGraph.CreateTexture(desc);

            AddBlurPass(renderGraph, source, ping, FIRST_OFFSET, false);
            for (var i = 1; i < _passes - 1; i++)
            {
                AddBlurPass(renderGraph, ping, pong, 0.5f + i, false);
                (ping, pong) = (pong, ping);
            }

            var lastOffset = 0.5f + _passes - 1f;
            if (_copyToFramebuffer)
            {
                AddBlurPass(renderGraph, ping, source, lastOffset, false);
                return;
            }

            desc.name = _targetName;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _result, desc, _targetName);
            Result = _result.rt;
            AddBlurPass(renderGraph, ping, renderGraph.ImportTexture(_result), lastOffset, true);
        }

        private TextureDesc BlurDescriptor(TextureDesc camera)
        {
            camera.sizeMode = TextureSizeMode.Explicit;
            camera.width = Mathf.Max(1, camera.width / _downsample);
            camera.height = Mathf.Max(1, camera.height / _downsample);
            camera.msaaSamples = MSAASamples.None;
            camera.bindTextureMS = false;
            camera.useMipMap = false;
            camera.autoGenerateMips = false;
            camera.enableRandomWrite = false;
            camera.clearBuffer = false;
            camera.filterMode = FilterMode.Bilinear;
            camera.wrapMode = TextureWrapMode.Clamp;
            return camera;
        }

        private void AddBlurPass(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, float offset, bool publishTarget)
        {
            using var builder = renderGraph.AddRasterRenderPass<BlurPassData>(PASS_NAME, out var data);
            data.Source = source;
            data.Material = _material;
            data.Offset = offset;

            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            if (publishTarget)
            {
                builder.AllowPassCulling(false);
                builder.SetGlobalTextureAfterPass(destination, _targetId);
            }

            builder.SetRenderFunc((BlurPassData passData, RasterGraphContext context) => Draw(passData, context));
        }

        private static void Draw(BlurPassData data, RasterGraphContext context)
        {
            PROPERTIES.Clear();
            PROPERTIES.SetTexture(BLIT_TEXTURE, data.Source);
            PROPERTIES.SetVector(BLIT_SCALE_BIAS, FULL_SCALE_BIAS);
            PROPERTIES.SetFloat(OFFSET, data.Offset);
            context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3, 1, PROPERTIES);
        }
    }
}
