# Prompty do generowania tekstur (ChatGPT / Images 2.0)

Każdy materiał ma **własną teksturę 1024×1024**. Do promptu **dołącz odpowiednią podkładkę UV**
z tego katalogu.

Gotowe pliki wrzucaj do `AlkoOlimpiada/Assets/3D/MapKit/Tekstury/` pod nazwą
`<NazwaMateriału>_BaseColor.png`, np. `Rzutka_Lufka_BaseColor.png`.

Mapy `_Normal` i `_AO` są wypalone i podpięte — nie generuj ich.

---

## Kluczowa sprawa: lufka rozciąga teksturę 4×

Zmierzone na modelu: obwód lufki **3.20 cm**, długość **12.82 cm**. Rzut walcowy mapuje
`u 0..1` na obwód, a `v 0..1` na długość, więc **wszystko narysowane w kwadratowym obrazku
wychodzi na modelu czterokrotnie rozciągnięte wzdłuż lufki**.

Kompensujemy to powtórzeniem: napis występuje w obrazku **4 razy jeden nad drugim**, każde
wystąpienie na ćwiartce wysokości. Po rozciągnięciu litery mają poprawne proporcje, a napis
biegnie wzdłuż lufki czterokrotnie — tak jak prawdziwy nadruk na rzutce.

W samym obrazku litery będą wyglądać na spłaszczone. **Tak ma być.**

---

## 1. Lufka rzutki — z napisem — `Rzutka_Lufka_UV.png`

> Create a 1024×1024 flat texture map (UV atlas sheet) for a 3D dart barrel.
>
> CRITICAL TECHNICAL REQUIREMENTS:
> - Completely flat 2D graphic. No lighting, no shadows, no highlights, no perspective,
>   no rendered object, no background, no mockup.
> - The image must tile SEAMLESSLY left-to-right: the left edge must continue perfectly
>   into the right edge, because this wraps around a cylinder.
> - Full bleed — artwork fills the entire square edge to edge.
> - The vertical axis runs along the dart: the BOTTOM edge is the sharp steel point,
>   the TOP edge is the back where the flights attach.
> - This image gets stretched 4× vertically when applied to the model, so all artwork must
>   be drawn VERTICALLY COMPRESSED to one quarter of its natural height. Letters and shapes
>   will look squashed and flattened in this image — that is intentional and correct.
>
> LAYOUT:
> The word "ALKOOLIMPIADA" rotated 90° counter-clockwise so it reads bottom-to-top, repeated
> in a regular grid of 2 columns × 4 rows — 8 copies total, evenly spaced, filling the image.
> Each copy spans about one quarter of the image height. Bold condensed sans-serif, white
> letters with a thin black outline, each copy sitting on a solid red vertical band.
>
> BACKGROUND, between and behind the text bands:
> Clean diagonal candy stripes in saturated red, white, electric blue and gold, plus thin
> horizontal metallic gold bands as separators. The bottom 15% of the image is bare polished
> steel with no stripes and no text — that is the point of the dart.
>
> STRICTLY AVOID: checkerboards, pixel grids, mosaic or tile patterns, random colored squares,
> black rectangles, glitch or noise effects. Every shape must be a clean solid area with
> crisp edges. Flat vector style, high contrast, no gradients, no grunge.
>
> Spell the word EXACTLY: A-L-K-O-O-L-I-M-P-I-A-D-A

Poprzednia tekstura miała pośrodku chaotyczny pas szachownicy z czarnymi polami i to
właśnie ten zakaz na końcu ma wyciąć.

---

## 2. Lotka rzutki — sam wzór, bez napisu — `Rzutka_Lotka_UV.png`

Oba piórka nachodzą na ten sam kwadrat, więc jedna grafika trafia na oba. Siatka wycina
**ośmiokąt wpisany w kwadrat** — rogi przepadną. Lotka nie jest rozciągnięta, proporcje 1:1.

> Create a 1024×1024 flat texture map for a dart flight (the fin at the back of a dart).
>
> CRITICAL TECHNICAL REQUIREMENTS:
> - Completely flat 2D graphic. No lighting, no shadows, no 3D rendering, no perspective,
>   no mockup, no background outside the artwork.
> - Full bleed — artwork fills the entire square edge to edge.
> - The mesh crops this square to an OCTAGON, so the four corners will be cut off.
> - NO TEXT, NO LETTERS, NO WORDS, NO NUMBERS anywhere in the image.
>
> DESIGN:
> Bold symmetrical graphic pattern: diagonal color blocks and lightning-bolt shapes in
> saturated red, gold, electric blue and white, with a few small white five-pointed stars
> and thin speed lines radiating from the centre. Symmetrical about the vertical axis.
> Clean flat vector style, crisp edges, high contrast, no gradients, no grunge.

---

## 3. Korpus granata — `Granat_Korpus_UV.png`

Gotowe i podpięte, generuj tylko jeśli chcesz zmienić wygląd.

> Create a 1024×1024 flat texture map (UV atlas sheet) for a 3D Mk 2 "pineapple" hand grenade body.
>
> CRITICAL TECHNICAL REQUIREMENTS:
> - Completely flat 2D graphic. No lighting, no shadows, no 3D rendering, no perspective,
>   no background, no mockup.
> - Must tile SEAMLESSLY left-to-right — it wraps around the grenade body.
> - Full bleed, artwork fills the entire square.
> - Vertical axis: BOTTOM edge is the flat base, TOP edge is the narrow fuze neck.
>
> DESIGN:
> Olive drab military green painted cast iron. A single narrow bright yellow stencil band
> running horizontally across the upper third. Small worn white stencil lettering and chipped
> paint revealing dark grey metal along the edges. Flat painted look, matte.

---

## 4. Metalowe części granata — `Granat_Metal`

Dźwignia, zawleczka i kółko. Nie generuj — goła stal, płaski kolor plus normal mapa wystarczą.

---

## Po wygenerowaniu

Wrzuć PNG-i do `Tekstury/` pod właściwymi nazwami i daj znać — podmienię i zrobię render
kontrolny. Materiały i prefaby (`Assets/Prefabs/Rzutka.prefab`, `Granat.prefab`) już istnieją,
więc podmiana samego pliku wystarczy.

---

## 5. Drewno szafy i kufra pod styl stołu — `Szafa_BaseColor.png`, `Kufer_BaseColor.png`

Cel: szafa i kufer mają wyglądać na zrobione z tego samego drewna co `stol.glb`.

**Do promptu dołącz dwa pliki:**
1. `Stol_ref_2048.jpg` — referencja stylu (tekstura stołu z Tripo, zjechana z 8192 do 2048)
2. `Szafa_UV.png` albo `Kufer_UV.png` — podkładka UV, tylko po to, żeby model widział skalę wysp

Kolory zmierzone na teksturze stołu: cień `#482C0F`, baza `#70481C`, słoje jasne `#946633`.

Atlas szafy i kufra to rzut prostopadłościenny — prostokątne wyspy w losowych obrotach.
Nie da się na nim malować per-wyspa, więc **generujemy jednolity materiał drewna**, który
wygląda poprawnie niezależnie od tego, gdzie wyspa wyląduje. Niebieski pas grecki i mosiądz
zostają na osobnych slotach materiału, nie w tej teksturze.

> Create a 1024×1024 seamless tileable wood texture map. Match the attached reference image
> as closely as possible — same species, same tone, same grain character.
>
> CRITICAL TECHNICAL REQUIREMENTS:
> - Completely flat 2D albedo map. No lighting, no shadows, no highlights, no ambient
>   occlusion, no perspective, no rendered object, no background, no mockup.
> - Must tile SEAMLESSLY on all four edges — top continues into bottom, left into right.
> - Full bleed, artwork fills the entire square edge to edge.
> - Uniform overall brightness across the whole image. No vignette, no darker corners,
>   no single hero area — every region must be usable on its own.
>
> DESIGN — match the reference:
> Warm golden-oak planks running VERTICALLY, 5 to 7 planks across the width. Base tone
> #70481C, lighter grain streaks up to #946633, seams and shadow gaps #482C0F. Straight
> longitudinal grain with fine parallel lines, a few soft knots, gentle tonal variation
> from plank to plank so no two neighbouring boards are identical. Thin dark seam line
> between planks, about 2 px wide. Scattered dark iron nail heads, small circles about
> 10 px across, sitting near the plank ends in an irregular pattern — same style as the
> bolt heads in the reference.
>
> STRICTLY AVOID: checkerboards, pixel grids, mosaic or tile patterns, random coloured
> squares, glitch or noise effects, visible repetition seams, text, logos, watermarks,
> heavy grunge, dirt splatter, moss, cracks or damage. Clean, even, semi-realistic
> game-asset wood — the kind that reads well when a 20 cm patch of it is stretched
> across a cabinet door.

Ta sama tekstura na oba modele jest OK — wtedy generuj raz i zapisz pod obiema nazwami.
