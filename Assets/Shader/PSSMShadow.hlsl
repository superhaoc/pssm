#ifndef PSSM_SHADOW_INCLUDED
#define PSSM_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_PSSMShadowAtlas);
SAMPLER(sampler_PSSMShadowAtlas);

//vsm
TEXTURE2D(_VSMShadow);
SAMPLER(sampler_VSMShadow);
float4x4 _VSMMatrix;


float4 _PSSMSplitDepths;
float _PSSMBlendRange;
float4 _PSSMAtlasData; // x: atlas size, y: splits per row, z: 1/atlas size, w: 1/tile size
int _PSSMSplitCount;
float4x4 _PSSMSplitMatrices[4];

float4x4 _PSSMSplitViewPortMatrices[4];

struct PSSMShadowData
{
    float4 shadowCoord;
    float splitDepth;
    int splitIndex;
    float blendWeight;
    bool needsBlend;
};

// 计算分割段索引
int GetPSSMSplitIndex(float depthVS)
{
    // 视图空间深度转换为线性深度
    float linearDepth = depthVS;
    
    // 根据分割深度确定分割段
    for (int i = 0; i < _PSSMSplitCount; i++)
    {
        if (linearDepth < _PSSMSplitDepths[i])
            return i;
    }
    return _PSSMSplitCount - 1;
}

// 获取图集中的UV坐标
float2 GetPSSMAtlasUV(int splitIndex, float2 shadowUV)
{
    float splitsPerRow = _PSSMAtlasData.y;
    float tileSize = 1.0f / splitsPerRow;
    float invAtlasSize = _PSSMAtlasData.z;
    
    int row = splitIndex / splitsPerRow;
    int col = splitIndex % splitsPerRow;
    
    float2 tileOffset = float2(col, row) * tileSize;
    float2 tileScale = float2(tileSize, tileSize);
    
    // 添加半纹素偏移以避免纹理过滤问题
    float2 halfTexel = float2(0.5f * invAtlasSize, 0.5f * invAtlasSize);
    
    //return tileOffset + shadowUV * tileScale + halfTexel;
    return shadowUV + halfTexel;
}

bool IsInLightFrustum(float4 clipPos)
{
    return (-clipPos.w <= clipPos.x && clipPos.x <= clipPos.w) &&
        (-clipPos.w <= clipPos.y && clipPos.y <= clipPos.w) && 
        (-clipPos.w <= clipPos.z && clipPos.z <= clipPos.w) ;
}

 
//因为Unity用的reversed-z
//得用切比雪夫下尾形式
 
float ComputeVSMCoverage(float2 uv, float lightDepth)
{
    float2 s = SAMPLE_TEXTURE2D(_VSMShadow, sampler_VSMShadow, uv).xy;

    
    float x = s.r;
    float x2 = s.g;

    // calculate the variance of the texel based on
    // the formula var = E(x^2) - E(x)^2
    // https://en.wikipedia.org/wiki/Algebraic_formula_for_the_variance#Proof
    float var = x2 - x * x;
    var = max(var, 0.00008);

    // calculate our initial probability based on the basic depths
    // if our depth is closer than x, then the fragment has a 100%
    // probability of being lit (p=1)
    float p_inv = lightDepth >= x;

    // calculate the upper bound of the probability using Chebyshev's inequality
    // https://en.wikipedia.org/wiki/Chebyshev%27s_inequality
    float delta =  x - lightDepth;
    float p_max = var / (var + delta * delta);

    float p_max_inv = smoothstep(0.75, 1, p_max);
    //p_max = 2.44 - p_max * 3;
    //p_max *= pow(2, cascade);

    // To alleviate the light bleeding, expand the shadows to fill in the gaps
    //float amount = _VarianceShadowExpansion;
    //p_max = clamp( (p_max - amount) / (1 - amount), 0, 1);
    //float p_max_inv = clamp(p_max + 0.5, 0, 1);

    //p_max_inv = 1;  // XXXXXXXXXXXXXX

    return max(p_max_inv, p_inv);
}


// 采样PSSM阴影贴图
float SamplePSSMShadow(float3 positionWS, float depthVS)
{
    float shadow = 0.0;
#ifdef _ENABLE_VSM
    float4 lightCoord = mul(_VSMMatrix, float4(positionWS, 1.0));
    float2 lightUV = saturate(lightCoord.xy / lightCoord.w * 0.5 + 0.5);
    bool bVisible = IsInLightFrustum(lightCoord);
  
    lightUV.y = 1.0 - lightUV.y;
    shadow = ComputeVSMCoverage(lightUV, lightCoord.z) * bVisible ;
 
#else
    int splitIndex = GetPSSMSplitIndex(depthVS);
    // 采样第一个阴影贴图
    float4 ndcCoord1 = mul(_PSSMSplitMatrices[splitIndex], float4(positionWS, 1.0));
    bool bVisible = IsInLightFrustum(ndcCoord1);
    ndcCoord1.xyzw /= ndcCoord1.w;

    float4 shadowCoord = mul(_PSSMSplitViewPortMatrices[splitIndex], ndcCoord1);    
    float2 uv1 = GetPSSMAtlasUV(splitIndex, shadowCoord.xy);
    float shadowDepth = SAMPLE_TEXTURE2D(_PSSMShadowAtlas, sampler_PSSMShadowAtlas, uv1).r;

        //注意Unity中的reversed-z
    shadow = ndcCoord1.z <= shadowDepth ? 0.0 : 1.0;
    shadow = lerp(1.0,shadow,bVisible);
#endif
    
return shadow;
}


#endif