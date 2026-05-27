Shader "FishFlock/FishBuiltInShader" 
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        _AnimationSpeed("Animation Speed", Range(0 , 10)) = 0.5
		_Scale("Scale", Range(0 , 1)) = 0.8
		_Yaw("Yaw", Float) = 0.2
		_Roll("Roll", Float) = 0.2

        _EmissionMap("Emission Map", 2D) = "black" {}
		[HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #include "../../_Shared/FishFlockShared.cginc"
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma multi_compile_instancing
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma instancing_options procedural:fishProceduralSetup

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_EmissionMap;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        uniform float _AnimationSpeed;
		uniform float _Yaw;
		uniform float _Roll;
		uniform float _Scale;

		sampler2D _EmissionMap;
		fixed4 _EmissionColor;

        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);

			float3 vertexPos = v.vertex.xyz;

            v.vertex.xyz = AnimateVertex(vertexPos, _AnimationSpeed, _Yaw, _Roll, _Scale);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo comes from a texture tinted by color
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            // Metallic and smoothness come from slider variables
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap) * _EmissionColor;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
