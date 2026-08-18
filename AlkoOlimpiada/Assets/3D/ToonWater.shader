// Kreskówkowa woda pod URP.
// Warstwa 1: kolor pasmami wg głębokości (wymaga Depth Texture + CopyDepthMode=AfterOpaques).
// Warstwa 2: twarda piana przy brzegu.
// Warstwa 3: kaustyki na Voronoi - odpowiednik grafu Radial Shear -> Voronoi -> Power -> RippleColor.
Shader "Custom/ToonWater"
{
    Properties
    {
        [Header(Odcien)]
        _ShallowColor ("Kolor przy brzegu", Color) = (0.30, 0.82, 0.80, 0.55)
        _DeepColor    ("Kolor w glebi",     Color) = (0.04, 0.30, 0.55, 0.92)
        _DepthMax     ("Glebokosc pelnego koloru (m)", Float) = 3.5
        _Bands        ("Liczba pasm koloru (1 = plynnie, bez progow)", Range(1, 12)) = 1
        _BandChaos    ("Wariacja koloru na tafli", Range(0, 0.6)) = 0.30
        _BandNoiseScale ("Skala plam koloru", Float) = 0.05

        [Header(Piana przy brzegu)]
        _FoamColor       ("Kolor piany", Color) = (1, 1, 1, 1)
        _FoamDistance    ("Zasieg piany (m)", Float) = 1.2
        _FoamCutoff      ("Prog piany", Range(0, 1)) = 0.45
        _FoamNoiseScale  ("Skala szumu piany", Float) = 1.2
        _FoamNoiseAmount ("Sila szumu piany", Range(0, 1)) = 0.45

        [Header(Kaustyki)]
        _RippleColor    ("Kolor kaustyk", Color) = (0.35, 0.90, 1.0, 1)
        _RippleStrength ("Sila kaustyk", Range(0, 2)) = 0.85
        _RippleTiling   ("Skala kaustyk (powtorzen na metr)", Float) = 0.05
        _RippleDensity  ("Gestosc komorek", Float) = 4
        _RippleSlimness ("Cienkosc zylek", Range(1, 24)) = 7
        _RippleSpeed    ("Predkosc kaustyk", Float) = 0.5
        _ShearStrength  ("Skret promienisty", Range(0, 1)) = 0.2
        _RippleFade     ("Dystans zaniku (m)", Float) = 160

        [Header(Ruch)]
        _Speed ("Predkosc przewijania", Float) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ToonWaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthMax;
                float  _Bands;
                float  _BandChaos;
                float  _BandNoiseScale;
                float4 _FoamColor;
                float  _FoamDistance;
                float  _FoamCutoff;
                float  _FoamNoiseScale;
                float  _FoamNoiseAmount;
                float4 _RippleColor;
                float  _RippleStrength;
                float  _RippleTiling;
                float  _RippleDensity;
                float  _RippleSlimness;
                float  _RippleSpeed;
                float  _ShearStrength;
                float  _RippleFade;
                float  _Speed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // --- szum wartosciowy, na plamy koloru i postrzepienie piany ---
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise21(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // --- odpowiedniki wezlow Shader Grapha ---

            // wezel Radial Shear
            float2 RadialShear(float2 uv, float2 center, float strength)
            {
                float2 delta = uv - center;
                float  d2 = dot(delta, delta);
                return uv + float2(delta.y, -delta.x) * (d2 * strength);
            }

            // wezel Voronoi (ta sama funkcja losujaca co w Shader Graphie)
            float2 voronoiRandom(float2 uv, float offset)
            {
                float2x2 m = float2x2(15.27, 47.63, 99.41, 89.98);
                uv = frac(sin(mul(uv, m)) * 46839.32);
                return float2(sin(uv.y * offset) * 0.5 + 0.5, cos(uv.x * offset) * 0.5 + 0.5);
            }

            float Voronoi(float2 uv, float angleOffset, float cellDensity)
            {
                float2 g = floor(uv * cellDensity);
                float2 f = frac(uv * cellDensity);
                float res = 8.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 lattice = float2(x, y);
                        float2 offset = voronoiRandom(lattice + g, angleOffset);
                        res = min(res, distance(lattice + offset, f));
                    }
                }
                return res;
            }

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.screenPos  = ComputeScreenPos(pos.positionCS);
                o.fogCoord   = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);

                // ile wody miedzy tafla a dnem
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float glebokosc = max(sceneEye - input.screenPos.w, 0.0);

                float2 uv = input.positionWS.xz;
                float  t  = _Time.y * _Speed;

                // kolor pasmami. Wolny szum o duzej skali wchodzi PRZED kwantyzacja,
                // wiec zamiast gradientu robi wielkie plaskie plamy jasniejszej wody.
                float nc = noise21(uv * _BandNoiseScale + float2(t * 0.4, -t * 0.25));
                float g = saturate(glebokosc / max(_DepthMax, 0.001));
                g = saturate(g - (nc - 0.5) * 2.0 * _BandChaos);
                // UWAGA: przy _Bands = 1 kwantyzacja musi byc POMINIETA, nie policzona.
                // floor(g * 1) / max(0, 1) to floor(g), czyli 0 dla wszystkiego ponizej rownej jedynki
                // - cala tafla wpada wtedy w kolor plycizny z twarda krawedzia tam, gdzie g dobija do 1.
                if (_Bands > 1.5)
                    g = saturate(floor(g * _Bands) / (_Bands - 1.0));

                half4 col = lerp(_ShallowColor, _DeepColor, g);

                // kaustyki: Radial Shear -> Voronoi (kat animowany czasem) -> Power -> kolor.
                // Voronoi zwraca odleglosc do najblizszej komorki: 0 w srodkach, max na granicach,
                // wiec potega wycina z tego cienkie jasne zylki.
                // UWAGA: Radial Shear w grafie dostaje UV 0..1. Podanie mu metrow rozsadza wzor,
                // bo dot(delta,delta) rosnie kwadratowo z odlegloscia. Skrecamy wiec w przestrzeni
                // znormalizowanej (100 m = 1 jednostka) i dopiero potem skalujemy do metrow.
                float2 rel = RadialShear(uv * 0.01, float2(0.0, 0.0), _ShearStrength);
                float2 uvR = rel * 100.0 * _RippleTiling;
                float vor = Voronoi(uvR, _Time.y * _RippleSpeed, _RippleDensity);
                // odleglosc Voronoi rzadko dobija do 1, bez podbicia zylki wychodza szare
                float zylki = pow(saturate(vor * 1.25), _RippleSlimness);
                // proceduralny Voronoi nie ma mipmap - bez wygaszania sypie sie w szum na horyzoncie
                float dystans = length(GetCameraPositionWS() - input.positionWS);
                zylki *= saturate(1.0 - dystans / max(_RippleFade, 1.0));
                col.rgb += _RippleColor.rgb * zylki * _RippleStrength * _RippleColor.a;

                // piana przy brzegu, granica postrzepiona szumem i scieta progiem
                float brzeg = 1.0 - saturate(glebokosc / max(_FoamDistance, 0.001));
                float fn = noise21(uv * _FoamNoiseScale + float2(t * 1.5, -t));
                float piana = step(_FoamCutoff, brzeg + (fn - 0.5) * 2.0 * _FoamNoiseAmount);
                col.rgb = lerp(col.rgb, _FoamColor.rgb, piana * _FoamColor.a);
                col.a   = max(col.a, piana * _FoamColor.a);

                col.rgb = MixFog(col.rgb, input.fogCoord);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
