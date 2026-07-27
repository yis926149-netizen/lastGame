Shader "Custom/ExplorationPillar"
{
	Properties
	{
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_Glossiness ("Smoothness", Range(0,1)) = 0.1
		_Metallic ("Metallic", Range(0,1)) = 0.0
		_Color ("Color", Color) = (0.25, 0.22, 0.2, 1)

		_NoiseTex ("Noise", 2D) = "white" {}
		_DissolveProgress ("Dissolve Progress", Range(0,1)) = 0
		_EdgeWidth ("Edge Width", Range(0, 0.2)) = 0.08
		[HDR] _EdgeColor ("Edge Color", Color) = (1.0, 0.85, 0.4, 3.0)

		_PillarBottomY ("Pillar Bottom Y", Float) = 0
		_PillarHeight ("Pillar Height", Float) = 1.8
	}

	SubShader
	{
		Tags { "RenderType"="Opaque" "Queue"="Geometry" }
		LOD 200

		CGPROGRAM
		#pragma surface surf Standard fullforwardshadows addshadow
		#pragma target 3.0

		sampler2D _MainTex;
		sampler2D _NoiseTex;

		struct Input
		{
			float2 uv_MainTex;
			float2 uv_NoiseTex;
			float3 worldPos;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;
		float _DissolveProgress;
		float _EdgeWidth;
		fixed4 _EdgeColor;
		float _PillarBottomY;
		float _PillarHeight;

		void surf(Input IN, inout SurfaceOutputStandard o)
		{
			float noise = tex2D(_NoiseTex, IN.uv_NoiseTex).r;
			float normalizedY = saturate((IN.worldPos.y - _PillarBottomY) / max(_PillarHeight, 0.001));

			// progress=0 时 cutoff=0，柱体完全可见；progress=1 时顶部先消失
			float cutoff = _DissolveProgress * (1.0 + normalizedY * 0.5);
			clip(noise - cutoff);

			float edgeMask = 1.0 - saturate((noise - cutoff) / max(_EdgeWidth, 0.0001));
			o.Emission = _EdgeColor.rgb * edgeMask * _EdgeColor.a;

			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Smoothness = _Glossiness;
			o.Metallic = _Metallic;
			o.Alpha = c.a;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
