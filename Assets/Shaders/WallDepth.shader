Shader "Custom/WallDepth"
{
    // Renders wall tilemap tiles with a bottom-edge shadow gradient based on
    // world-space Y position within each 1-unit tile.
    // Bottom of each tile fades to (1 - _ShadowStrength) brightness;
    // top of the tile stays at full brightness.
    // _ShadowFade controls how far up the tile the gradient extends (0-1).
    //
    // Tilemap.color tint is applied via vertex color (IN.color), same as
    // Unity's default sprite shaders — FloorAssembler.wallTint still works.

    Properties
    {
        _MainTex        ("Sprite Texture", 2D)             = "white" {}
        _ShadowStrength ("Bottom Shadow Strength", Range(0, 1)) = 0.6
        _ShadowFade     ("Shadow Fade Height (tile frac)", Range(0.05, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "RenderType"        = "Transparent"
            "RenderPipeline"    = "UniversalPipeline"
            "IgnoreProjector"   = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        // ── URP 2D Renderer pass ─────────────────────────────────────────────
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half   _ShadowStrength;
                half   _ShadowFade;
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
                half4  color       : COLOR;
                float  worldY      : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldY      = TransformObjectToWorld(IN.positionOS.xyz).y;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                color *= IN.color;

                // frac(worldY): 0 at tile bottom, ~1 at tile top (1 world unit = 1 tile)
                float tileRelY = frac(IN.worldY);

                // Smooth gradient: dark at bottom, full brightness at _ShadowFade
                float t = smoothstep(0.0, _ShadowFade, tileRelY);
                half shadow = lerp(1.0 - _ShadowStrength, 1.0, (half)t);
                color.rgb *= shadow;

                return color;
            }
            ENDHLSL
        }

        // ── URP 3D / Editor fallback ─────────────────────────────────────────
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half   _ShadowStrength;
                half   _ShadowFade;
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
                half4  color       : COLOR;
                float  worldY      : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldY      = TransformObjectToWorld(IN.positionOS.xyz).y;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                color *= IN.color;

                float tileRelY = frac(IN.worldY);
                float t = smoothstep(0.0, _ShadowFade, tileRelY);
                half shadow = lerp(1.0 - _ShadowStrength, 1.0, (half)t);
                color.rgb *= shadow;

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
