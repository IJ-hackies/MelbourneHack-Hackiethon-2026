Shader "Custom/EnemyOutline"
{
    Properties
    {
        _MainTex      ("Sprite Texture", 2D)    = "white" {}
        _OutlineColor ("Outline Color",  Color) = (1, 0, 0, 1)
        _OutlineSize  ("Outline Size (px)", Range(1, 4)) = 1
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
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // _ST and _TexelSize must live outside the CBUFFER for 2D SRP Batcher compatibility
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4  _OutlineColor;
                float  _OutlineSize;
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
                float4 color       : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.color       = IN.color;
                return OUT;
            }

            half SampleAlpha(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // Discard pixels that are part of the sprite itself —
                // the parent SpriteRenderer renders those above us.
                if (c.a > 0.5)
                    discard;

                // Sample the 8 neighbours; if any is opaque, this pixel is on the outline edge.
                float2 ts = _MainTex_TexelSize.xy * _OutlineSize;

                half maxA = 0;
                maxA = max(maxA, SampleAlpha(IN.uv + float2( ts.x,    0)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(-ts.x,    0)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(   0,  ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(   0, -ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2( ts.x,  ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(-ts.x,  ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2( ts.x, -ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(-ts.x, -ts.y)));

                if (maxA < 0.5)
                    discard;

                return _OutlineColor;
            }
            ENDHLSL
        }

        // ── URP 3D / Editor fallback ──────────────────────────────────────────
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4  _OutlineColor;
                float  _OutlineSize;
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
                float4 color       : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.color       = IN.color;
                return OUT;
            }

            half SampleAlpha(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                if (c.a > 0.5) discard;

                float2 ts = _MainTex_TexelSize.xy * _OutlineSize;
                half maxA = 0;
                maxA = max(maxA, SampleAlpha(IN.uv + float2( ts.x,    0)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(-ts.x,    0)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(   0,  ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(   0, -ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2( ts.x,  ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(-ts.x,  ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2( ts.x, -ts.y)));
                maxA = max(maxA, SampleAlpha(IN.uv + float2(-ts.x, -ts.y)));

                if (maxA < 0.5) discard;
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
