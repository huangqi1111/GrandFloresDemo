Shader "DNF60/HitFlash" {
 Properties { [PerRendererData] _MainTex("Sprite",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Flash("Flash",Range(0,1))=0 [HideInInspector] _RendererColor("RendererColor",Color)=(1,1,1,1) [HideInInspector] _Flip("Flip",Vector)=(1,1,1,1) }
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True"} Cull Off Lighting Off ZWrite Off Blend One OneMinusSrcAlpha
 Pass {CGPROGRAM
 #pragma vertex SpriteVert
 #pragma fragment HitFrag
 #pragma multi_compile_instancing
 #include "UnitySprites.cginc"
 float _Flash;
 fixed4 HitFrag(v2f i):SV_Target {fixed4 c=SampleSpriteTexture(i.texcoord)*i.color;c.rgb=lerp(c.rgb,fixed3(1,1,1),_Flash);c.rgb*=c.a;return c;}
 ENDCG }
 }
}

