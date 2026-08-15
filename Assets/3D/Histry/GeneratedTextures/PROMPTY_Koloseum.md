# Prompty do generowania tekstur — Koloseum / świątynia grecka

Źródło stylu: `Assets/3D/Kamienie/Pas-kamienny-2_BaseColor.png`
Paleta zmierzona z tego pliku (64 131 próbek, czarne tło pominięte):

| rola | hex | udział |
|---|---|---|
| dominanta | `#B0A090` | 59,4% |
| cień / spoina | `#807060` | 18,5% |
| światło | `#C0B0A0` | 7,5% |
| przejścia | `#A09080`, `#908070` | 8,2% |

Jasność: p10 = 117, mediana = 163, p90 = 169, max = 179. Niski kontrast, nic nie jest białe ani czarne.

Generuj w **1024×1024**, poza obramowaniem (**1536×1024**).

---

## BLOK STYLU — wklej na początku KAŻDEGO promptu

```
STYLE — identical for every texture in this set, do not deviate:

Stylised hand-painted game texture. Matte, flat-lit, uniform illumination.
No baked shadows, no ambient occlusion, no specular highlights, no gloss.

Palette — warm desaturated limestone, saturation never above 15%:
- dominant mid tone #B0A090, covering roughly 60% of the surface
- shadow and recess tone #807060, roughly 18%
- highlight tone #C0B0A0, roughly 8%
- transition tones #A09080 and #908070 fill the remainder
Every colour keeps R > G > B with about a 30-point gap between R and B.
The warm cast is constant: never grey, never blue, never pink, never green.
Luminance stays within 117-179 on a 0-255 scale. Low overall contrast.

Surface reads as weathered ancient Acropolis limestone: patchy and uneven,
some areas noticeably lighter, some darker, soft-edged weathering blotches,
faint pitting, small chips. The variation is random and organic.

NO repeating motif. NO regular grid. NO decorative carving. NO Greek key or
meander ornament. NO network of cracks. NO moss, NO vegetation, NO snow.

Flat orthographic top-down view, camera perpendicular to the surface.
Zero perspective, zero vignette, no border, no frame, no text, no watermark,
no colour chart, no lighting gradient across the image.
Seamless and tileable on all four edges.
```

---

## 1. Podłoga — `Podloga_grecka` (4,94 × 8,78 m)

```
[BLOK STYLU]

SUBJECT: Ancient Greek temple floor.
Large rectangular limestone flagstones, each roughly 0.8 to 1.2 metres across,
laid in irregular courses of slightly varying width. Joints are thin dark
recesses in #807060, never forming a perfect grid — the rows are offset by
random amounts and some stones are noticeably longer than others.
Stone centres are worn smooth and slightly paler from centuries of foot
traffic; the tone darkens gradually toward the joints. Scatter a handful of
individual slabs that are clearly paler or clearly darker than their
neighbours, positioned at random.
SCALE: the image covers exactly 2 x 2 metres of real floor.
```

---

## 2. Schody — `Schody_greckie` (6,61 × 10,7 m)

```
[BLOK STYLU]

SUBJECT: Ancient Greek temple steps, seen from directly above.
The same limestone as the temple floor, but reading as long horizontal step
treads: broad blocks running left to right across the full width, with subtle
horizontal banding where one tread meets the next.
Front edges of the treads are slightly lighter and visibly chipped from wear;
the tread surfaces are mottled and uneven. Vertical joints are sparse, roughly
one every 1.5 metres, and never line up between adjacent rows.
SCALE: the image covers exactly 2 x 2 metres. Must sit next to the floor
texture without any visible change in stone size or colour.
```

---

## 3. Konstrukcja dachu — `Dach_grecki` (4,96 × 8,8 m)

> Uwaga: to kamienna konstrukcja pod dachówkami. Same dachówki (`Dachowki_greckie`) mają własną, zachowaną teksturę — tej nie ruszamy.

```
[BLOK STYLU]

SUBJECT: The massive limestone slabs and beams forming a Greek temple roof
structure, the stonework beneath the tiles.
Large plain blocks with noticeably fewer joints than the floor — long spans of
uninterrupted stone. Slightly coarser and about 5% darker overall than the
floor, with more dust accumulation and more weathering blotches, plus hairline
chips at the block corners. No carving, no ornament, plain structural stone.
SCALE: the image covers exactly 2 x 2 metres. Same stone species and same
palette as the floor and steps, just dustier.
```

---

## 4. Obramowanie dachu — `Obramowanie_frontu_dachu` + `Obramowanie_tylu_dachu` (4,72 × 0,23 m)

> Oba elementy są identyczne co do milimetra. Wygeneruj **jedną** teksturę i użyj do obu — albo dwa warianty tego samego promptu, jeśli chcesz je odróżnić.
> Generuj w **1536×1024**.

```
[BLOK STYLU]

SUBJECT: Cornice band along the edge of a Greek temple roof — a long narrow
horizontal strip of limestone.
It reads as one continuous carved beam rather than separate blocks: a smooth
flat face with very few joints, at most one every 2 metres. Because rain
washes this edge, it is slightly cleaner and about 5% lighter than the temple
floor, with darker weathering streaks running vertically down the face at
irregular intervals. Plain moulding only — no ornament, no dentils, no
carved figures.
COMPOSITION: wide and short, the band runs the full width of the image.
SCALE: the image covers 2 metres of the band's length.
```

---

## 5. Kolumny — `kolumna-grecka` (śr. 2,15 m × wys. 7,38 m w grze)

> Kolumna wciąż siedzi na starej teksturze z Tripo (`tripo_node_..._BaseColor`) — zimna biel, odstaje od podłogi i dachu. Geometria ma już bębny, bazę i głowicę **modelowane**, więc tekstura ma być gładkim kamieniem: żadnych żłobkowań, żadnych poziomych pierścieni malowanych.
> Obwód ≈ 6,8 m, więc kafel 2 × 2 m owija się ~3,4× wokół i ~3,7× w pionie. Mapowanie cylindryczne w Blenderze → bake na UV kolumny (UV z Tripo jest atlasem, nie da się kafelkować bezpośrednio).

```
[BLOK STYLU]

SUBJECT: The shaft of an ancient Greek limestone column, unfluted and smooth.
Plain dressed stone with a fine chiselled surface: faint vertical tool marks
running top to bottom, barely visible, never forming stripes of even width.
Weathering is vertical in character — soft darker streaks in #807060 running
down the surface at irregular intervals, wider apart than one per 30 cm, plus
a few paler washed patches in #C0B0A0 between them. Scatter small chips and
shallow pits at random, slightly more of them in the lower half.
The stone is a touch smoother and about 3% lighter than the temple floor,
as a column sheds rain instead of collecting dirt.

NO flutes, NO vertical grooves or channels, NO drum joints, NO horizontal
banding, NO capital, NO base, NO carving — all of that is modelled geometry.
NO single dominant streak that would read as a seam.

SCALE: the image covers exactly 2 x 2 metres of the shaft's surface. Must sit
next to the floor and step textures without any visible change in grain size
or colour.
```

---

## Co dalej w Blenderze

1. Wygenerowane pliki → `GeneratedTextures/Koloseum_v2/Source/` jako `Source_<Element>_v1.png`.
2. GPT nie zrobi naprawdę bezszwowego kafla — sprawdź krawędzie (offset o połowę) i popraw, zanim zbakujesz.
3. GPT nie generuje poprawnych map **Normal** ani **Roughness** — nie proś go o nie. Wyprowadź je z BaseColor po stronie Blendera.
4. Bake per obiekt na jego własnym UV → dopiero to jest „tekstura idealnie pod baking". Rozdzielczości z poprzedniej serii: Podłoga / Schody / Dach 2048², Obramowanie 1024².
