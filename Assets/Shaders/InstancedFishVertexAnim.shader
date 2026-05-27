Shader "TestBoids/Instanced Fish Vertex Anim"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5

        [Header(Fish Vertex Animation)]
        _Modifier("Reference Modifier", Vector) = (1, 1, 1, 1)
        _Speed("Speed", Float) = 5
        _Amplitude("Body Amplitude", Float) = 0.025
        _Frequency("Body Frequency", Float) = 18
        _HeadLock("Head Lock Z", Float) = -0.15
        _BodyLength("Animated Body Length", Float) = 1
        _TailStart("Tail Start Z", Float) = 0.45
        _TailAmplitude("Tail Extra Amplitude", Float) = 0.04
        _TailFrequency("Tail Frequency", Float) = 26
        _SideAxis("Side Axis XYZ", Vector) = (1, 0, 0, 0)

        [HideInInspector] _FishAnimParams("Per Instance Anim Params", Vector) = (0, 1, 1, 1)
        [HideInInspector] _FishTint("Per Instance Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2
        [HideInInspector] _AlphaClip("__clip", Float) = 0
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        LOD 250

        HLSLINCLUDE
        #pragma target 3.0

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        float3 _LightDirection;
        float3 _LightPosition;

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _Metallic;
            half _Smoothness;
            float4 _Modifier;
            float _Speed;
            float _Amplitude;
            float _Frequency;
            float _HeadLock;
            float _BodyLength;
            float _TailStart;
            float _TailAmplitude;
            float _TailFrequency;
            float4 _SideAxis;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(FishProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _FishAnimParams)
            UNITY_DEFINE_INSTANCED_PROP(float4, _FishTint)
        UNITY_INSTANCING_BUFFER_END(FishProps)

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half fogFactor : TEXCOORD3;
            float4 shadowCoord : TEXCOORD4;
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float3 SafeNormalize(float3 value, float3 fallback)
        {
            float lengthSq = dot(value, value);
            return lengthSq > 0.000001 ? value * rsqrt(lengthSq) : fallback;
        }

        float GetFishBodyMask(float z)
        {
            float normalizedZ = saturate((z - _HeadLock) / max(0.0001, _BodyLength));
            return 1.0 - cos(normalizedZ * 1.5707963);
        }

        float3 ApplyFishVertexAnimation(float3 positionOS)
        {
            float4 animParams = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FishAnimParams);
            float phaseOffset = animParams.x + (_Modifier.x * _Modifier.y);
            float speedMul = max(0.0, animParams.y);
            float amplitudeMul = animParams.z;
            float frequencyMul = max(0.0001, animParams.w);
            float time = _Time.y * _Speed * speedMul;

            float bodyMask = GetFishBodyMask(positionOS.z);
            float bodyPhase = (positionOS.z * _Frequency * _Modifier.z * frequencyMul) + time + phaseOffset;
            float bodyWave = sin(bodyPhase) * _Amplitude * amplitudeMul * bodyMask / max(0.0001, _Modifier.w);

            float tailMask = smoothstep(_TailStart, _TailStart + max(0.0001, _BodyLength * 0.35), positionOS.z);
            float tailPhase = (positionOS.z * _TailFrequency * _Modifier.z * frequencyMul) + (time * 1.28) + phaseOffset;
            float tailWave = sin(tailPhase) * _TailAmplitude * amplitudeMul * tailMask / max(0.0001, _Modifier.w);

            float3 sideAxis = SafeNormalize(_SideAxis.xyz, float3(1, 0, 0));
            positionOS += sideAxis * (bodyWave + tailWave);
            return positionOS;
        }

        Varyings FishForwardVertex(Attributes input)
        {
            Varyings output = (Varyings)0;

            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 animatedPositionOS = ApplyFishVertexAnimation(input.positionOS.xyz);
            VertexPositionInputs positionInputs = GetVertexPositionInputs(animatedPositionOS);
            VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.positionWS = positionInputs.positionWS;
            output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
            output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
            output.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
            output.positionCS = positionInputs.positionCS;

            return output;
        }

        half4 FishForwardFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            half4 fishTint = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FishTint);
            albedoAlpha *= fishTint;

            #if defined(_ALPHATEST_ON)
                clip(albedoAlpha.a - _Cutoff);
            #endif

            InputData inputData = (InputData)0;
            inputData.positionWS = input.positionWS;
            inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
            inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            inputData.shadowCoord = input.shadowCoord;
            inputData.fogCoord = input.fogFactor;
            inputData.vertexLighting = VertexLighting(input.positionWS, inputData.normalWS);
            inputData.bakedGI = SampleSH(inputData.normalWS);
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            inputData.shadowMask = half4(1, 1, 1, 1);

            SurfaceData surfaceData = (SurfaceData)0;
            surfaceData.albedo = albedoAlpha.rgb;
            surfaceData.alpha = albedoAlpha.a;
            surfaceData.metallic = _Metallic;
            surfaceData.specular = half3(0.2, 0.2, 0.2);
            surfaceData.smoothness = _Smoothness;
            surfaceData.normalTS = half3(0, 0, 1);
            surfaceData.occlusion = 1;
            surfaceData.emission = 0;
            surfaceData.clearCoatMask = 0;
            surfaceData.clearCoatSmoothness = 0;

            half4 color = UniversalFragmentPBR(inputData, surfaceData);
            color.rgb = MixFog(color.rgb, input.fogFactor);
            return color;
        }

        float4 GetAnimatedShadowPositionHClip(Attributes input)
        {
            float3 animatedPositionOS = ApplyFishVertexAnimation(input.positionOS.xyz);
            float3 positionWS = TransformObjectToWorld(animatedPositionOS);
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

            float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            return ApplyShadowClamping(positionCS);
        }

        struct DepthOnlyVaryings
        {
            float2 uv : TEXCOORD0;
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        DepthOnlyVaryings FishDepthOnlyVertex(Attributes input)
        {
            DepthOnlyVaryings output = (DepthOnlyVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 animatedPositionOS = ApplyFishVertexAnimation(input.positionOS.xyz);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.positionCS = TransformObjectToHClip(animatedPositionOS);
            return output;
        }

        half4 FishDepthOnlyFragment(DepthOnlyVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            #if defined(_ALPHATEST_ON)
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
            #endif

            return 0;
        }

        struct ShadowVaryings
        {
            float2 uv : TEXCOORD0;
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        ShadowVaryings FishShadowVertex(Attributes input)
        {
            ShadowVaryings output = (ShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.positionCS = GetAnimatedShadowPositionHClip(input);
            return output;
        }

        half4 FishShadowFragment(ShadowVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            #if defined(_ALPHATEST_ON)
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
            #endif

            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex FishForwardVertex
            #pragma fragment FishForwardFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex FishShadowVertex
            #pragma fragment FishShadowFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex FishDepthOnlyVertex
            #pragma fragment FishDepthOnlyFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
