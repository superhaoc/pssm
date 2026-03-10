//create by haoc
//利用gpu 硬件linear sampling 优化版本 
//每像素21 sample 降低为11 sample tap
Shader "Hidden/GaussianBlurLinear_21tap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off 
        ZWrite Off 
        ZTest Always
        // 水平方向 Pass
        Pass
        {
            Name "Horizontal"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag_horizontal

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;   // 纹素大小（x = 1/宽度，y = 1/高度）
            
            //推导: (d0w0 + (d0+1)w1) / (w0 + w1) => d0 + w1/(w0+w1) ,d0 为起始offset
            //原kernel:  float[](0.14107424, 0.132526984, 0.109868729, 0.080381679, 0.051898313, 0.029570767,0.014869116, 0.00659813, 0.002583865, 0.00089296,0.000272337);
            //线性采样优化后的21-tap高斯核（中心 + 5组对称偏移）
            static const float CENTER_WEIGHT = 0.14107424;
            static const float BLUR_OFFSETS[5] = { 1.4533, 3.3925, 5.3347, 7.281, 9.236 };
            static const float BLUR_WEIGHTS[5]  = { 0.242395713, 0.132279992, 0.044439883, 0.009181995, 0.001165297 };

            half4 frag_horizontal(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                // 中心采样
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * CENTER_WEIGHT;

                // 对每组偏移进行正负方向采样
                for (int i = 0; i < 5; i++)
                {
                    float offset = BLUR_OFFSETS[i] * _MainTex_TexelSize.x;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( offset, 0)) * BLUR_WEIGHTS[i];
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2( offset, 0)) * BLUR_WEIGHTS[i];
                }
                return color;
            }
            ENDHLSL
        }

        // 垂直方向 Pass
        Pass
        {
            Name "Vertical"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag_vertical

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
   
            static const float CENTER_WEIGHT = 0.14107424;
            static const float BLUR_OFFSETS[5] = { 1.4533, 3.3925, 5.3347, 7.281, 9.236 };
            static const float BLUR_WEIGHTS[5]  = { 0.242395713, 0.132279992, 0.044439883, 0.009181995, 0.001165297 };

            half4 frag_vertical(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * CENTER_WEIGHT;

                for (int i = 0; i < 5; i++)
                {
                    float offset = BLUR_OFFSETS[i] * _MainTex_TexelSize.y;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  offset)) * BLUR_WEIGHTS[i];
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0,  offset)) * BLUR_WEIGHTS[i];
                }
                
                //最后归一化上面的近似的期望和方差
                if (color.z == 0) color = float4(1, 1, 1,0);
                float2 result = color.xy / color.z;
                return float4(result, 0, 0);
            }
            ENDHLSL
        }
    }
}