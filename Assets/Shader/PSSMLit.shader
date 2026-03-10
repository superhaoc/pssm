// PSSMLit.shader
Shader "Custom/PSSM Lit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma shader_feature _ENABLE_VSM
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PSSMShadow.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float depthVS : TEXCOORD3;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END
            
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.depthVS = -vertexInput.positionVS.z; // 视图空间深度
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // 采样纹理
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                
                // 光照计算
                Light mainLight = GetMainLight();
                // 计算PSSM阴影
                float shadow = SamplePSSMShadow(input.positionWS, input.depthVS);
                // 或者使用PCF版本：
                // float shadow = SamplePSSMShadowPCF(input.positionWS, input.depthVS, 0);
                
                // 光照计算
                float3 diffuse = mainLight.color * mainLight.distanceAttenuation * shadow;
                
                // 简单漫反射
                float NdotL = saturate(dot(input.normalWS, mainLight.direction));
                float3 lighting = diffuse * NdotL;
                
                // 环境光
                float3 ambient = SampleSH(input.normalWS);
                
                // 最终颜色
                float3 finalColor = (lighting + ambient) * baseColor.rgb;
                
                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }
        
       
    }
}