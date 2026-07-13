# Alko-Olimpiada — instalacja od zera

Instrukcja dla nowego współpracownika. Zakładam czysty Windows + zainstalowane Visual Studio.

## Opcja szybka: skrypt (zalecane)

Pobierz [setup.ps1](https://raw.githubusercontent.com/AradinX/Alko-Olimpiada-Game/master/setup.ps1) (prawy klik → Zapisz jako), potem w **PowerShellu**:

```
powershell -ExecutionPolicy Bypass -File .\Downloads\setup.ps1
```

Skrypt zainstaluje Git (z Git Bash i LFS), Unity Hub, edytor Unity 6000.5.3f1 i sklonuje repo. Na końcu wypisze, co kliknąć w Unity Hub. Jeśli coś padnie — kroki ręczne poniżej.

## 1. Git + Git LFS

1. Pobierz i zainstaluj Git: https://git-scm.com/download/win (wszędzie domyślne opcje).
2. Otwórz **Git Bash** (albo zwykły terminal) i włącz Git LFS — **to obowiązkowe**, bez tego modele 3D ściągną się jako puste pliki-wskaźniki:
   ```
   git lfs install
   ```
3. Przedstaw się gitowi (raz, na stałe):
   ```
   git config --global user.name  "TwojeImie"
   git config --global user.email "twoj@email.com"
   ```

## 2. Sklonuj repozytorium

```
cd C:\Users\TWOJA_NAZWA
git clone https://github.com/AradinX/Alko-Olimpiada-Game.git
```

Przy pierwszym pushu GitHub poprosi o logowanie — wybierz logowanie przez przeglądarkę.
(Musisz być wcześniej dodany jako collaborator do repo — Settings → Collaborators.)

## 3. Unity Hub + Unity

1. Pobierz i zainstaluj **Unity Hub**: https://unity.com/download
2. Zaloguj się (darmowe konto Unity Personal wystarczy).
3. Zainstaluj edytor w wersji dokładnie **6000.5.3f1** — projekt jest w tej wersji i inna może przekonwertować pliki:
   - Unity Hub → **Installs** → **Install Editor** → zakładka **Archive** → **download archive** → znajdź `6000.5.3f1`,
   - albo bezpośredni link: `unityhub://6000.5.3f1/c2eb47b3a2a9` (wklej w pasek przeglądarki przy włączonym Hubie).
4. Przy instalacji edytora zaznacz moduł **Windows Build Support (IL2CPP)** — reszta modułów niepotrzebna. Visual Studio już masz, więc odznacz jego instalację.

## 4. Otwórz projekt

1. Unity Hub → **Projects** → **Add** → wskaż folder `Alko-Olimpiada-Game\AlkoOlimpiada` (podfolder, nie główny katalog repo!).
2. Pierwsze otwarcie potrwa kilka minut (import assetów).
3. W Unity: **Edit → Preferences → External Tools → External Script Editor** → wybierz **Visual Studio**. Od teraz dwuklik na skrypcie otwiera VS z pełnym IntelliSense.

## 5. Codzienna praca

```
git pull          # przed rozpoczęciem pracy — zawsze
# ...praca...
git add -A
git commit -m "co zrobiłem"
git push
```

Zasady, żeby się nie pogryźć:

- **Zawsze `git pull` przed rozpoczęciem pracy.** Sceny Unity (.unity) i prefaby źle się mergują — konflikt w scenie to ból.
- Najlepiej **nie edytować tej samej sceny/prefabu jednocześnie** — dogadajcie się, kto co bierze.
- Nowe assety wrzucaj do `AlkoOlimpiada/Assets/` (Unity samo zrobi pliki `.meta` — commituj je razem z assetem).
- Duże pliki binarne (fbx, png, blend...) idą automatycznie przez Git LFS — nic nie musisz robić, o ile zrobiłeś `git lfs install` w kroku 1.

## Struktura repo

- `AlkoOlimpiada/` — właściwy projekt Unity (ten folder otwierasz w Unity Hub)
- `Assets/2d`, `Assets/3D` — pliki źródłowe grafik i modeli (blend, glb, png) — do edycji w Blenderze/programie graficznym, Unity ich nie używa bezpośrednio
- `GAMEPLAY.md` — opis mechanik gry
