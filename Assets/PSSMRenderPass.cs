//
// Created by haoc on 2022/05/11.
//

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PSSMRenderPass : ScriptableRenderPass
{
    private class PSSMSplitData
    {
        public float near;
        public float far;
        public Matrix4x4 viewMatrix;
        public Matrix4x4 projMatrix;
        public Vector4 viewportRect; // x, y, width, height (normalized 0-1), subviewport
        public Rect viewportPixelRect;
    }

    private const string ProfilerTag = "PSSM Shadows";
    private const string ProfilerVSMTag = "VSM Shadows";
    private const string ShadowAtlasTextureName = "_PSSMShadowAtlas";
    private const string ShadowVSMTextureName = "_VSMShadow";
    private const string CascadeCountName = "_PSSMSplitCount";
    private readonly PSSMRenderFeature.PSSMSettings m_Settings;
    private RenderTexture m_ShadowAtlas;
    private RTHandle m_DepthTexture;
    private RenderTexture m_vsmShadowMap;
    private PSSMSplitData[] m_SplitData;
    private Material m_ShadowMaterial;
    private float[] m_SplitDepths;
    private Matrix4x4[] m_SplitMatrices;
    private Matrix4x4[] m_viewPortMatrices;
    private Material m_vsmMaterial;
    private Material m_gsBlurMaterial;
    private Rect m_vsmRect;
    private Matrix4x4 m_vsmLightProjMatrix;
    private int m_tempTex1Id;
    private int m_tempTex2Id;
    private bool m_btemporyRTReady;
    private RenderTargetIdentifier m_source;
    private RenderTargetIdentifier m_tempTex1;
    private RenderTargetIdentifier m_tempTex2;
    public PSSMRenderPass(PSSMRenderFeature.PSSMSettings settings)
    {
        m_Settings = settings;
        m_SplitData = new PSSMSplitData[settings.splitCount];
        for (int i = 0; i < settings.splitCount; i++)
        {
            m_SplitData[i] = new PSSMSplitData();
        }

        m_SplitDepths = new float[settings.splitCount + 1];
        m_SplitMatrices = new Matrix4x4[settings.splitCount];
        m_viewPortMatrices = new Matrix4x4[settings.splitCount];

        // 创建阴影渲染材质（使用URP内置的阴影shader）
        m_ShadowMaterial = CoreUtils.CreateEngineMaterial("Hidden/Internal-DepthNormalsTexture");

        if (settings.EnableVSM)
        {
            m_vsmMaterial = CoreUtils.CreateEngineMaterial("Hidden/VSMShadowCaster");
            Shader.EnableKeyword("_ENABLE_VSM");
            m_gsBlurMaterial = CoreUtils.CreateEngineMaterial("Hidden/GaussianBlurLinear_21tap");

            m_tempTex1Id = Shader.PropertyToID("_TempRT1");
            m_tempTex2Id = Shader.PropertyToID("_TempRT2");
            m_btemporyRTReady = false;
        }
        else
        {
            Shader.DisableKeyword("_ENABLE_VSM");
        }
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        // 创建或更新阴影图集
        if (m_DepthTexture == null)
        {
            // 计算图集大小
            int atlasSize = CalculateAtlasSize();

            var depthDescriptor = new RenderTextureDescriptor(atlasSize, atlasSize, RenderTextureFormat.Depth, 16);
            depthDescriptor.depthBufferBits = 16;
            depthDescriptor.msaaSamples = 1;

            // 使用 RenderingUtils.ReAllocateIfNeeded 创建 RTHandle
            RenderingUtils.ReAllocateIfNeeded(ref m_DepthTexture, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "PSSM Shadow Atlas");
        }

        if (m_Settings.EnableVSM && m_vsmShadowMap == null)
        {
            if (m_vsmShadowMap != null)
                m_vsmShadowMap.Release();
            m_vsmShadowMap = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGBFloat)
            {
                name = "VSM Shadow Map",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_vsmRect = new Rect(0, 0, 512, 512);
        }

        ConfigureTarget(m_DepthTexture);
        ConfigureClear(ClearFlag.All, Color.black);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        if (m_Settings.EnableVSM && m_vsmShadowMap != null && !m_btemporyRTReady)
        {
            m_btemporyRTReady = true;
            m_source = new RenderTargetIdentifier(m_vsmShadowMap);
            int width = m_vsmShadowMap.width;
            int height = m_vsmShadowMap.height;
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBFloat, 0)
            {
                useMipMap = false,
                autoGenerateMips = false
            };

            cmd.GetTemporaryRT(m_tempTex1Id, desc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(m_tempTex2Id, desc, FilterMode.Bilinear);

            m_tempTex1 = new RenderTargetIdentifier(m_tempTex1Id);
            m_tempTex2 = new RenderTargetIdentifier(m_tempTex2Id);
        }

    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        if (m_Settings.EnableVSM)
        {
            cmd.ReleaseTemporaryRT(m_tempTex1Id);
            cmd.ReleaseTemporaryRT(m_tempTex2Id);
            m_btemporyRTReady = false;
        }
    }

    private void RenderVSM(CommandBuffer cmd, ScriptableRenderContext context, RenderingData renderingData)
    {
        if (m_Settings.EnableVSM)
        {
            cmd.SetViewport(m_vsmRect);
            cmd.SetRenderTarget(m_vsmShadowMap);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.SetGlobalMatrix("_LightViewProj", m_vsmLightProjMatrix);

            var drawingSettings = new DrawingSettings(new ShaderTagId("VSMShadowCaster"), new SortingSettings(renderingData.cameraData.camera))
            {
                enableDynamicBatching = true,
                enableInstancing = true,
                overrideMaterial = m_vsmMaterial,
                overrideMaterialPassIndex = 0
            };
            drawingSettings.SetShaderPassName(0, new ShaderTagId("UniversalForward"));

            var filterSettings = new FilteringSettings(RenderQueueRange.opaque, m_Settings.shadowCullingMask);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filterSettings);

            //2 blur pass and calcuating EV and Var
            if (m_btemporyRTReady)
            {
                Blit(cmd, m_source, m_tempTex1, m_gsBlurMaterial, 0);
                Blit(cmd, m_tempTex1, m_tempTex2, m_gsBlurMaterial, 1);
                //copy result back to the orginal source
                Blit(cmd, m_tempTex2, m_source);
            }

        }
    }
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isSceneViewCamera)
            return;

        VisibleLight mainLight = renderingData.lightData.mainLightIndex >= 0
            ? renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex]
            : default;

        if (mainLight.light == null || mainLight.lightType != LightType.Directional)
            return;


        CommandBuffer cmd = null;
        if (m_Settings.EnableVSM)
        {
            cmd = CommandBufferPool.Get(ProfilerVSMTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerVSMTag)))
            {
                CalculateStandardMatrix(renderingData.cameraData.camera, mainLight);
                RenderVSM(cmd, context, renderingData);
            }
        }
        else
        {
            cmd = CommandBufferPool.Get(ProfilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(ProfilerTag)))
            {
                // 计算分割距离
                CalculateSplitDistances(renderingData.cameraData.camera);

                // 为每个分割段计算投影矩阵
                for (int i = 0; i < m_Settings.splitCount; i++)
                {
                    CalculateSplitMatrices(i, renderingData.cameraData.camera, mainLight);
                }

                // 渲染每个分割段的阴影
                RenderSplitsToAtlas(cmd, context, renderingData);
            }
        }

        // 设置Shader全局变量
        SetupShaderProperties(cmd);


        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    private int CalculateAtlasSize()
    {
        // 计算图集大小，假设使用网格布局
        int splitsPerRow = Mathf.CeilToInt(Mathf.Sqrt(m_Settings.splitCount));
        return splitsPerRow * m_Settings.shadowResolution;
    }

    private void CalculateSplitDistances(Camera camera)
    {
        float near = camera.nearClipPlane;
        float far = camera.farClipPlane;
        int splitCount = m_Settings.splitCount;

        // 计算分割点
        for (int i = 0; i <= splitCount; i++)
        {
            // 线性分割
            float linearSplit = near + (far - near) * (i / (float)splitCount);

            // 对数分割
            float logSplit = near * Mathf.Pow(far / near, i / (float)splitCount);

            // 混合分割
            float mixedSplit = Mathf.Lerp(linearSplit, logSplit, m_Settings.splitLambda);

            m_SplitDepths[i] = mixedSplit;

            if (i < splitCount)
            {
                m_SplitData[i].near = m_SplitDepths[i];
                m_SplitData[i].far = m_SplitDepths[i + 1];
            }
        }
    }

    /// <summary>
    /// 计算crop修改矩阵,caster object 铺满整个ndc空间
    /// </summary>
    /// 
    private void CalculateCropMatrix()
    {
        Bounds combinedBounds = new Bounds();
        bool hasValidRenderer = false;
        foreach (var rtype in RendererCollector.AllTargetRenderers)
        {
            var renderer = rtype.render;
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (!hasValidRenderer)
            {
                // 用第一个有效 Renderer 的 bounds 初始化
                combinedBounds = renderer.bounds;
                hasValidRenderer = true;
            }
            else
            {
                // 合并后续的 bounds
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        Vector3 min = combinedBounds.min;
        Vector3 max = combinedBounds.max;

        // 生成 8 个顶点（按顺序，顺序不重要，但要完整）
        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(max.x, min.y, min.z);
        corners[2] = new Vector3(min.x, max.y, min.z);
        corners[3] = new Vector3(max.x, max.y, min.z);
        corners[4] = new Vector3(min.x, min.y, max.z);
        corners[5] = new Vector3(max.x, min.y, max.z);
        corners[6] = new Vector3(min.x, max.y, max.z);
        corners[7] = new Vector3(max.x, max.y, max.z);


        Vector3[] transformedCorners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            transformedCorners[i] = m_vsmLightProjMatrix.MultiplyPoint(corners[i]);
        }

        Bounds newBounds = new Bounds(transformedCorners[0], Vector3.zero);
        for (int i = 1; i < 8; i++)
            newBounds.Encapsulate(transformedCorners[i]);

        Matrix4x4 cropMatrix = Matrix4x4.identity;
        float scaleX = 2.0f / (newBounds.max.x - newBounds.min.x);
        float scaleY = 2.0f / (newBounds.max.y - newBounds.min.y);
        float scaleZ = 1.0f / (newBounds.max.z - newBounds.min.z);

        float offsetX = -0.5f * (newBounds.max.x + newBounds.min.x) * scaleX;
        float offsetY = -0.5f * (newBounds.max.y + newBounds.min.y) * scaleY;
        float offsetZ = -newBounds.min.z * scaleZ;

        cropMatrix.SetRow(0, new Vector4(scaleX, 0, 0, offsetX));
        cropMatrix.SetRow(1, new Vector4(0, scaleY, 0, offsetY));
        cropMatrix.SetRow(2, new Vector4(0, 0, scaleZ, offsetZ));
        cropMatrix.SetRow(3, new Vector4(0, 0, 0, 1));

        m_vsmLightProjMatrix = cropMatrix * m_vsmLightProjMatrix;
    }

    private void CalculateStandardMatrix(Camera camera, VisibleLight mainLight)
    {
        Vector3[] frustumCornersVS = GetFrustumCorners(-camera.nearClipPlane, -camera.farClipPlane, camera);
        Vector3[] frustumCornersWS = new Vector3[8];
        Matrix4x4 viewToWorld = camera.cameraToWorldMatrix;
        for (int i = 0; i < 8; i++)
        {
            frustumCornersWS[i] = viewToWorld.MultiplyPoint3x4(frustumCornersVS[i]);
        }

        Vector3 lightDir = mainLight.localToWorldMatrix.GetColumn(2).normalized;
        Vector3 lightPos = CalculateLightPosition(frustumCornersWS, lightDir);
        Vector3 lookTarget = lightPos + lightDir * 100f;
        Vector3 auxAxis = Vector3.up;
        Vector3 viewDir = (lookTarget - lightPos).normalized;


        if (Mathf.Abs(Vector3.Dot(viewDir, auxAxis)) > 0.9999f)
        {
            auxAxis = Vector3.forward;
        }

        var lightViewMatrix = Matrix4x4.LookAt(lightPos, lookTarget, auxAxis).inverse;

        if (SystemInfo.usesReversedZBuffer)
        {
            lightViewMatrix.m20 = -lightViewMatrix.m20;
            lightViewMatrix.m21 = -lightViewMatrix.m21;
            lightViewMatrix.m22 = -lightViewMatrix.m22;
            lightViewMatrix.m23 = -lightViewMatrix.m23;
        }

        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        Debug.DrawLine(frustumCornersWS[4], frustumCornersWS[5], Color.black);
        Debug.DrawLine(frustumCornersWS[5], frustumCornersWS[6], Color.black);
        Debug.DrawLine(frustumCornersWS[6], frustumCornersWS[7], Color.black);
        Debug.DrawLine(frustumCornersWS[7], frustumCornersWS[4], Color.black);

        for (int i = 0; i < 8; i++)
        {
            Vector3 cornerLS = lightViewMatrix.MultiplyPoint(frustumCornersWS[i]);
            min = Vector3.Min(min, cornerLS);
            max = Vector3.Max(max, cornerLS);
        }

        //var lightProjMatrix = Matrix4x4.Ortho(min.x, max.x, min.y, max.y, -max.z, -min.z);
        var lightProjMatrix = Matrix4x4.Ortho(min.x, max.x, min.y, max.y, min.z, -min.z);
        m_vsmLightProjMatrix = lightProjMatrix * lightViewMatrix;

        //ndc z -1~1 => reversed-z 1~0 and flip y axis
        m_vsmLightProjMatrix = GL.GetGPUProjectionMatrix(m_vsmLightProjMatrix, true);

        if (RendererCollector.AllTargetRenderers.Count > 0)
        {
            CalculateCropMatrix();
        }

    }
    private void CalculateSplitMatrices(int splitIndex, Camera camera, VisibleLight mainLight)
    {
        PSSMSplitData split = m_SplitData[splitIndex];

        //获取当前分割段的视锥体在视图空间中的8个顶点
        Vector3[] frustumCornersVS = GetFrustumCorners(-split.near, -split.far, camera);

        //将顶点转换到世界空间
        Vector3[] frustumCornersWS = new Vector3[8];
        Matrix4x4 viewToWorld = camera.cameraToWorldMatrix;
        for (int i = 0; i < 8; i++)
        {
            frustumCornersWS[i] = viewToWorld.MultiplyPoint(frustumCornersVS[i]);
        }

        //计算光源视图矩阵
        Vector3 lightDir = mainLight.localToWorldMatrix.GetColumn(2).normalized;
        Vector3 lightPos = CalculateLightPosition(frustumCornersWS, lightDir);
        Vector3 lookTarget = lightPos + lightDir * 100f;

        Vector3 auxAxis = Vector3.up;
        Vector3 viewDir = (lookTarget - lightPos).normalized;
        if (Mathf.Abs(Vector3.Dot(viewDir, auxAxis)) > 0.9999f)
        {
            auxAxis = Vector3.forward;
        }

        split.viewMatrix = Matrix4x4.LookAt(lightPos, lookTarget, auxAxis).inverse;

        if (SystemInfo.usesReversedZBuffer)
        {
            split.viewMatrix.m20 = -split.viewMatrix.m20;
            split.viewMatrix.m21 = -split.viewMatrix.m21;
            split.viewMatrix.m22 = -split.viewMatrix.m22;
            split.viewMatrix.m23 = -split.viewMatrix.m23;
        }

        if (splitIndex == 0)
            Debug.DrawLine(lightPos, lightPos + lookTarget.normalized * 13, Color.red);

        if (splitIndex == 0)
            Debug.DrawLine(frustumCornersWS[4], frustumCornersWS[5], Color.blue);

        //将世界空间顶点转换到光源空间
        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 cornerLS = split.viewMatrix.MultiplyPoint(frustumCornersWS[i]);
            min = Vector3.Min(min, cornerLS);
            max = Vector3.Max(max, cornerLS);
        }

        float size = Mathf.Max(max.x - min.x, max.y - min.y);
        split.projMatrix = Matrix4x4.Ortho(min.x, max.x, min.y, max.y, -max.z, -min.z);
        //split.projMatrix = Matrix4x4.Ortho(-size/2,size/2,-size/2,size/2, -max.z ,-min.z);

        //计算图集视口
        int splitsPerRow = Mathf.CeilToInt(Mathf.Sqrt(m_Settings.splitCount));
        int row = splitIndex / splitsPerRow;
        int col = splitIndex % splitsPerRow;

        float tileSize = 1.0f / splitsPerRow;
        split.viewportRect = new Vector4(
            col * tileSize,
            row * tileSize,
            tileSize,
            tileSize
        );

        split.viewportPixelRect = new Rect(
            col * m_Settings.shadowResolution,
            row * m_Settings.shadowResolution,
            m_Settings.shadowResolution,
            m_Settings.shadowResolution
        );

        //存储最终的投影矩阵（包含图集变换），采样的时候要用到这个matrix
        Matrix4x4 textureScaleAndOffset = Matrix4x4.identity;
        textureScaleAndOffset.m00 = 0.5f * tileSize;
        textureScaleAndOffset.m11 = -0.5f * tileSize;

        textureScaleAndOffset.m03 = split.viewportRect.z * 0.5f + split.viewportRect.x;
        textureScaleAndOffset.m13 = split.viewportRect.w * 0.5f + split.viewportRect.y;

        m_viewPortMatrices[splitIndex] = textureScaleAndOffset;

        //GL.GetGPUProjectionMatrix内部会左乘下面这个矩阵,scale&bias
        //	1   0   0   0
        //	0   -1   0   0
        //	0   0 0.5 0.5
        //	0   0   0   1
        m_SplitMatrices[splitIndex] = GL.GetGPUProjectionMatrix(split.projMatrix * split.viewMatrix, true);
    }

    private Vector3[] GetFrustumCorners(float near, float far, Camera camera)
    {
        Vector3[] corners = new Vector3[8];

        // 获取透视投影的视锥体角点
        float fov = camera.fieldOfView * Mathf.Deg2Rad;
        float aspect = camera.aspect;

        float nearHeight = 2.0f * Mathf.Tan(fov * 0.5f) * near;
        float nearWidth = nearHeight * aspect;

        float farHeight = 2.0f * Mathf.Tan(fov * 0.5f) * far;
        float farWidth = farHeight * aspect;

        // 近平面
        corners[0] = new Vector3(-nearWidth * 0.5f, -nearHeight * 0.5f, near);
        corners[1] = new Vector3(nearWidth * 0.5f, -nearHeight * 0.5f, near);
        corners[2] = new Vector3(nearWidth * 0.5f, nearHeight * 0.5f, near);
        corners[3] = new Vector3(-nearWidth * 0.5f, nearHeight * 0.5f, near);

        // 远平面
        corners[4] = new Vector3(-farWidth * 0.5f, -farHeight * 0.5f, far);
        corners[5] = new Vector3(farWidth * 0.5f, -farHeight * 0.5f, far);
        corners[6] = new Vector3(farWidth * 0.5f, farHeight * 0.5f, far);
        corners[7] = new Vector3(-farWidth * 0.5f, farHeight * 0.5f, far);

        return corners;
    }

    private Vector3 CalculateLightPosition(Vector3[] frustumCornersWS, Vector3 lightDir)
    {
        // 计算视锥体包围盒的中心
        Vector3 center = Vector3.zero;
        foreach (var corner in frustumCornersWS)
        {
            center += corner;
        }
        center /= frustumCornersWS.Length;

        // 计算需要沿着光线反方向后退的距离
        // 确保整个视锥体都在光源摄像机视野内
        float maxDistance = 0f;
        foreach (var corner in frustumCornersWS)
        {
            Vector3 vecToCorner = corner - center;
            float distanceAlongLight = Vector3.Dot(vecToCorner, -lightDir); // 注意：这里是负的lightDir
            maxDistance = Mathf.Max(maxDistance, distanceAlongLight);
        }

        // 后退足够远，确保看到整个视锥体
        return center - lightDir * (maxDistance + 10f);
    }

    private void RenderSplitsToAtlas(CommandBuffer cmd, ScriptableRenderContext context, RenderingData renderingData)
    {
        ShaderTagId shadowTagId = new ShaderTagId("ShadowCaster");

        cmd.SetRenderTarget(m_DepthTexture);
        cmd.ClearRenderTarget(true, true, Color.black);
        // 渲染每个分割段
        for (int i = 0; i < m_Settings.splitCount; i++)
        {
            PSSMSplitData split = m_SplitData[i];
            cmd.SetViewport(split.viewportPixelRect);
            cmd.SetViewProjectionMatrices(split.viewMatrix, split.projMatrix);

            var settings = new DrawingSettings(shadowTagId, new SortingSettings(renderingData.cameraData.camera))
            {
                enableDynamicBatching = renderingData.supportsDynamicBatching,
                enableInstancing = true
            };

            var filterSettings = new FilteringSettings(RenderQueueRange.opaque, m_Settings.shadowCullingMask);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawRenderers(renderingData.cullResults, ref settings, ref filterSettings);
        }

        cmd.SetViewProjectionMatrices(
            renderingData.cameraData.camera.worldToCameraMatrix,
            renderingData.cameraData.camera.projectionMatrix
        );
    }

    private void SetupShaderProperties(CommandBuffer cmd)
    {
        // 设置阴影图集
        cmd.SetGlobalTexture(ShadowAtlasTextureName, m_DepthTexture);
        // 设置分割数据
        cmd.SetGlobalInt(CascadeCountName, m_Settings.splitCount);

        if (m_Settings.EnableVSM)
        {
            cmd.SetGlobalMatrix("_VSMMatrix", m_vsmLightProjMatrix);
            cmd.SetGlobalTexture(ShadowVSMTextureName, m_vsmShadowMap);
        }
        // 设置分割深度
        Vector4 splitDepths = new Vector4(
            m_SplitDepths[1],
            m_SplitDepths[2],
            m_SplitDepths[3],
            m_SplitDepths[4]
        );
        cmd.SetGlobalVector("_PSSMSplitDepths", splitDepths);
        // 设置混合范围
        cmd.SetGlobalFloat("_PSSMBlendRange", m_Settings.blendRange);

        // 设置投影矩阵数组
        cmd.SetGlobalMatrixArray("_PSSMSplitMatrices", m_SplitMatrices);
        cmd.SetGlobalMatrixArray("_PSSMSplitViewPortMatrices", m_viewPortMatrices);

        // 设置图集信息
        int atlasSize = CalculateAtlasSize();
        int splitsPerRow = Mathf.CeilToInt(Mathf.Sqrt(m_Settings.splitCount));
        Vector4 atlasData = new Vector4(
            atlasSize,
            splitsPerRow,
            1.0f / atlasSize,
            1.0f / m_Settings.shadowResolution
        );
        cmd.SetGlobalVector("_PSSMAtlasData", atlasData);
    }

    public void Dispose()
    {
        if (m_ShadowAtlas != null)
        {
            m_ShadowAtlas.Release();
            m_ShadowAtlas = null;
        }

        if (m_vsmShadowMap != null)
        {
            m_vsmShadowMap.Release();
            m_vsmShadowMap = null;
        }

        CoreUtils.Destroy(m_ShadowMaterial);
        CoreUtils.Destroy(m_vsmMaterial);
        CoreUtils.Destroy(m_gsBlurMaterial);
    }
}