Shader "Unlit/Particle3D"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float normal : TEXCOORD1;
                float3 color : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float3 _Color;
            float _Scale;

            // Compute shader
            StructuredBuffer<float3> _Positions;
            StructuredBuffer<float3> _DebugColors;

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                float3 centreWorld = float3(_Positions[instanceID]);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * _Scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

                float3 colour = float3(_DebugColors[instanceID]);

				v2f o;
				o.uv = v.uv;
                o.normal = v.normal;
                o.color = colour;
				o.vertex = UnityObjectToClipPos(objectVertPos);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float shading = saturate(dot(_WorldSpaceLightPos0.xyz, i.normal));
				shading = (shading + 1.2) / 1.4;

				return float4(i.color.xyz * shading, 1);
            }
            ENDCG
        }
    }
}
