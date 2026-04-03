
Shader "Custom/FlipShader" {
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FlipX;  // 1 = flip horizontal
            float _FlipY;  // 1 = flip vertical

            fixed4 frag(v2f_img i) : SV_Target {
                                float2 uv = i.uv;
                        if (_FlipX > 0.5) uv.x = 1 - uv.x;
                        if (_FlipY > 0.5) uv.y = 1 - uv.y;
                        return tex2D(_MainTex, uv);
                            }
                            ENDCG
        }
    }
}