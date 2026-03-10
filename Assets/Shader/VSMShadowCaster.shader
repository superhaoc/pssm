//create by haoc

Shader "Hidden/VSMShadowCaster"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode"="UniversalForward" } 
        Pass
        {
            Name "VSMShadowCaster"
            Cull Back
            ZWrite On
            ZTest LEqual


            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float linearDepth : TEXCOORD0; // 线性深度
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _LightFarPlane;
            float4x4 _LightViewProj;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = mul(_LightViewProj, float4(positionWS, 1.0));
          
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                //因为output.positionCS.z都是ze的线性函数可以直接使用
                //ortho 投影为线性z,直接保存
                //output.viewDepth
                output.linearDepth = output.positionCS.z;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {


                float depth = input.linearDepth;
                float dx = ddx(depth);
                float dy = ddy(depth);
                float bias = 0.25 * (dx * dx + dy * dy);
                //推导:
                //D00, D10
                //D01, D11
                //D00 = c
                //D10 = c + a
                //D01 = c + b
                //D11 = c + a + b
                //μ = (c + (c+a) + (c+b) + (c+a+b)) / 4 = c + (a+b)/2
                //无偏 variance bias = [ (c-μ)^2 + (c+a-μ)^2 + (c+b-μ)^2 + (c+a+b-μ)^2 ] / 4
                return float4(depth, depth * depth + bias, 1, 0);
            }
            ENDHLSL
        }
    }
}