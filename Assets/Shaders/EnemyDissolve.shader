Shader "Custom/EnemyDissolve"
{
    Properties
    {
        _MainTex  ("Sprite Texture", 2D)    = "white" {}
        _NoiseTex ("Noise Texture",  2D)    = "white" {}
        _Dissolve ("Dissolve", Range(0,1))  = 0
        _Color    ("Tint", Color)           = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        // ── URP 2D Renderer pass ──────────────────────────────────────────────
        // "Universal2D" is the correct LightMode for the URP 2D renderer.
        // Rendered unlit — appropriate for a ghost/spirit effect.
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _Dissolve;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 worldXY     : TEXCOORD1;
                float4 color       : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.worldXY     = worldPos.xy;
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c     = SAMPLE_TEXTURE2D(_MainTex,  sampler_MainTex,  IN.uv);
                // World-space noise lookup — consistent across atlas UV offsets
                half  noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, IN.worldXY * 1.8).r;

                clip(noise - _Dissolve);

                c *= _Color * IN.color;
                return c;
            }
            ENDHLSL
        }

        // ── URP 3D Renderer fallback (for editor / non-2D setups) ─────────────
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _Dissolve;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 worldXY     : TEXCOORD1;
                float4 color       : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.worldXY     = worldPos.xy;
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c     = SAMPLE_TEXTURE2D(_MainTex,  sampler_MainTex,  IN.uv);
                half  noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, IN.worldXY * 1.8).r;
                clip(noise - _Dissolve);
                c *= _Color * IN.color;
                return c;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
