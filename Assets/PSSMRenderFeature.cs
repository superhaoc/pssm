//
// Created by haoc on 2022/05/10.
//
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PSSMRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class PSSMSettings
    {
        [Range(1, 4)] public int splitCount = 4;
        public int shadowResolution = 1024;
        [Range(0.0f, 1.0f)] public float splitLambda = 0.5f;
        [Range(0.0f, 0.1f)] public float blendRange = 0.05f;
        public LayerMask shadowCullingMask = -1;
        public bool EnableVSM = false;
    }
    
    public PSSMSettings settings = new PSSMSettings();
    
    private PSSMRenderPass m_PSSMPass;
    
    public override void Create()
    {
        m_PSSMPass = new PSSMRenderPass(settings);
        m_PSSMPass.renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
       // if (renderingData.shadowData.supportsMainLightShadows)
       if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(m_PSSMPass);
        }
    }
    
    protected override void Dispose(bool disposing)
    {
        m_PSSMPass?.Dispose();
    }
}