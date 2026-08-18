// Kreskówkowe niebo pod URP - skybox, odpowiednik grafu z referek (sky2/sky3/sky4).
// Warstwa 1: gradient nieba horyzont -> zenit, opcjonalnie w pasmach (jak _Bands w ToonWater).
// Warstwa 2: chmury z trzech Gradient Noise: (A - B) * C -> Clamp, dokladnie jak w grafie.
// Warstwa 3: zanik przy horyzoncie - odpowiednik maski Length -> One Minus -> Remap -> Clamp z grafu.
Shader "Skybox/ToonSky"
{
    Properties
    {
        [Header(Niebo)]
        _ZenithColor  ("Kolor w zenicie",        Color) = (0.13, 0.42, 0.85, 1)
        _HorizonColor ("Kolor przy horyzoncie",  Color) = (0.72, 0.88, 0.98, 1)
        _HorizonPower ("Ostrosc przejscia",      Range(0.2, 8)) = 2.2
        _SkyBands     ("Liczba pasm nieba (1 = plynnie, bez progow)", Range(1, 12)) = 1

        [Header(Chmury)]
        [HDR] _CloudColor ("Kolor chmur",        Color) = (1, 1, 1, 1)
        _CloudScale    ("Skala chmur",           Float) = 3
        _CloudCutoff   ("Prog chmury (wiecej = mniej chmur)", Range(0, 1)) = 0.36
        _CloudSoftness ("Miekkosc krawedzi",     Range(0.001, 0.6)) = 0.10
        _CloudBoost    ("Jasnosc rdzeni (>1 = HDR pod bloom)", Float) = 1.6
        // przy horyzoncie perspektywa sciska szum w kaszke - stad domyslnie duzy zanik
        _HorizonFade   ("Zanik chmur przy horyzoncie", Range(0.01, 0.9)) = 0.7

        [Header(Wiatr)]
        _WindDir   ("Kierunek wiatru (XZ)", Vector) = (1, 0, 0.35, 0)
        _WindSpeed ("Predkosc wiatru",      Float) = 0.015
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ToonSky"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float  _HorizonPower;
                float  _SkyBands;
                float4 _CloudColor;
                float  _CloudScale;
                float  _CloudCutoff;
                float  _CloudSoftness;
                float  _CloudBoost;
                float  _HorizonFade;
                float4 _WindDir;
                float  _WindSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // --- wezel Gradient Noise z Shader Grapha (Deterministic), 1:1 ---
            float2 gradientNoiseDir(float2 p)
            {
                p = fmod(p, 289.0);
                float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
                x = fmod((34.0 * x + 1.0) * x, 289.0);
                x = frac(x / 41.0) * 2.0 - 1.0;
                return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
            }

            float GradientNoise(float2 p)
            {
                float2 ip = floor(p);
                float2 fp = frac(p);
                float d00 = dot(gradientNoiseDir(ip),                 fp);
                float d01 = dot(gradientNoiseDir(ip + float2(0, 1)),  fp - float2(0, 1));
                float d10 = dot(gradientNoiseDir(ip + float2(1, 0)),  fp - float2(1, 0));
                float d11 = dot(gradientNoiseDir(ip + float2(1, 1)),  fp - float2(1, 1));
                fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
                return lerp(lerp(d00, d10, fp.x), lerp(d01, d11, fp.x), fp.y) + 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // siatka skyboxa siedzi na kamerze, wiec wektor od kamery to kierunek patrzenia.
                // Przez macierz obiektu, bo Unity wpisuje w nia obrot skyboxa.
                o.dirWS = TransformObjectToWorld(input.positionOS.xyz) - GetCameraPositionWS();
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.dirWS);

                // --- niebo: gradient wzdluz wysokosci, ewentualnie skwantyzowany na pasma ---
                float t = pow(saturate(1.0 - dir.y), _HorizonPower);
                // UWAGA (ten sam warunek co w ToonWater): przy _SkyBands = 1 kwantyzacje trzeba
                // POMINAC, bo floor(t) sklei cale niebo w jeden kolor z twarda krawedzia.
                if (_SkyBands > 1.5)
                    t = saturate(floor(t * _SkyBands) / (_SkyBands - 1.0));
                float3 col = lerp(_ZenithColor.rgb, _HorizonColor.rgb, t);

                // --- chmury ---
                // Rzut kierunku na plaszczyzne chmur. +0.15 w mianowniku zamiast czystego dir.y,
                // bo przy horyzoncie iloraz leci do nieskonczonosci i szum rozsypuje sie w aliasing.
                float2 uv = dir.xz / (max(dir.y, 0.0) + 0.15) * _CloudScale;
                uv += normalize(float2(_WindDir.x, _WindDir.z) + 1e-5) * (_Time.y * _WindSpeed);

                // graf: Gradient Noise 8 minus Gradient Noise 10, razy Gradient Noise 13.5, Clamp 0..1.
                // Skale trzymam w tych samych proporcjach (1 : 1.25 : 1.69), offsety rozjezdzaja ziarna.
                float n1 = GradientNoise(uv);
                float n2 = GradientNoise(uv * 1.25 + 31.7);
                float n3 = GradientNoise(uv * 1.69 - 17.3 + _Time.y * _WindSpeed * 0.6);
                // x4, bo (A-B)*C mieści sie realnie w ~0..0.25 - bez podbicia progi z Inspectora
                // dzialaja tylko w pierwszym procencie suwaka.
                float gestosc = saturate((n1 - n2) * n3 * 4.0);

                float chmura = smoothstep(_CloudCutoff, _CloudCutoff + _CloudSoftness, gestosc);
                // odpowiednik maski radialnej z grafu: tam wygaszala chmure przy krawedzi quada,
                // tutaj przy horyzoncie - i przy okazji zabija aliasing rozciagnietego szumu.
                chmura *= smoothstep(0.0, _HorizonFade, dir.y);

                // Jasnosc rdzenia rosnie z gestoscia - to Remap(0..1 -> 0.._CloudBoost) z grafu,
                // ale remapowane od 1, nie od 0: w grafie ciemne krawedzie ratowal bloom po HDR,
                // tutaj bez niego wychodzila szara obwodka wokol kazdej chmury.
                float3 chmuraRGB = _CloudColor.rgb * lerp(1.0, _CloudBoost, gestosc);
                col = lerp(col, chmuraRGB, chmura);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
