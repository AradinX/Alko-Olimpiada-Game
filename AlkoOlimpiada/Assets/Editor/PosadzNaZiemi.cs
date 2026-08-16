using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Sadzanie propsow na gruncie huba i stawianie strefy szatni.
// Grunt to zaimportowana WyspaTest.fbx - siatki nie da sie edytowac z Unity, wiec
// "wyrownanie" to sprowadzenie obiektow do wspolnej wysokosci, nie splaszczanie terenu.
public static class PosadzNaZiemi
{
    const float StartWysokosc = 50f;   // skad puszczamy promien w dol
    const float Zasieg = 200f;

    struct Trafienie
    {
        public GameObject go;
        public float grunt;            // Y powierzchni pod obiektem
        public float spod;             // ile spod obiektu jest ponizej jego pivota
    }

    static List<Trafienie> Zmierz(out int nietrafione)
    {
        nietrafione = 0;
        var zaznaczone = new HashSet<Transform>(
            Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<Transform>()));
        var wynik = new List<Trafienie>();

        foreach (var go in Selection.gameObjects)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;

            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            var start = new Vector3(b.center.x, b.max.y + StartWysokosc, b.center.z);
            var trafienia = Physics.RaycastAll(start, Vector3.down, Zasieg);
            System.Array.Sort(trafienia, (a, c) => a.distance.CompareTo(c.distance));

            bool ok = false;
            foreach (var h in trafienia)
            {
                if (zaznaczone.Contains(h.transform)) continue;   // nie lapiemy sami siebie
                wynik.Add(new Trafienie
                {
                    go = go,
                    grunt = h.point.y,
                    spod = go.transform.position.y - b.min.y
                });
                ok = true;
                break;
            }
            if (!ok) nietrafione++;
        }
        return wynik;
    }

    static void Zastosuj(List<Trafienie> t, float? wspolnyGrunt)
    {
        Undo.RecordObjects(t.Select(x => (Object)x.go.transform).ToArray(), "Posadz na ziemi");
        foreach (var x in t)
        {
            var p = x.go.transform.position;
            p.y = (wspolnyGrunt ?? x.grunt) + x.spod;
            x.go.transform.position = p;
        }
    }

    [MenuItem("Tools/Grunt/Posadz zaznaczone na ziemi")]
    static void Posadz()
    {
        var t = Zmierz(out int brak);
        if (t.Count == 0) { Debug.LogWarning("Nic nie trafilo w grunt — zaznacz obiekty i sprawdz, czy grunt ma collider."); return; }
        Zastosuj(t, null);
        Debug.Log($"Posadzono {t.Count} obiektow na gruncie." + (brak > 0 ? $" {brak} bez trafienia w collider." : ""));
    }

    [MenuItem("Tools/Grunt/Posadz i wyrownaj do wspolnego poziomu")]
    static void PosadzIWyrownaj()
    {
        var t = Zmierz(out int brak);
        if (t.Count == 0) { Debug.LogWarning("Nic nie trafilo w grunt — zaznacz obiekty i sprawdz, czy grunt ma collider."); return; }

        var g = t.Select(x => x.grunt).OrderBy(v => v).ToList();
        float mediana = g.Count % 2 == 1 ? g[g.Count / 2] : (g[g.Count / 2 - 1] + g[g.Count / 2]) / 2f;
        Zastosuj(t, mediana);
        Debug.Log($"Wyrownano {t.Count} obiektow na Y={mediana:F3} " +
                  $"(grunt pod nimi wahal sie {g.First():F2}..{g.Last():F2}, czyli {g.Last() - g.First():F2} m)." +
                  (brak > 0 ? $" {brak} bez trafienia w collider." : ""));
    }

    [MenuItem("Tools/Grunt/Przenies szatnie na zaznaczona szafe")]
    static void PrzeniesSzatnie()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogWarning("Zaznacz szafe."); return; }

        if (go.GetComponentsInChildren<Renderer>().Length == 0)
        { Debug.LogWarning($"{go.name} nie ma renderera — zaznacz korzen modelu szafy."); return; }

        var stare = Object.FindObjectsByType<WardrobeShop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (stare.Any(w => w.transform.IsChildOf(go.transform)))
        { Debug.LogWarning($"{go.name} juz jest szatnia."); return; }

        Undo.SetCurrentGroupName("Przenies szatnie");
        int grupa = Undo.GetCurrentGroup();

        float promien = stare.Length > 0 ? stare[0].radius : 4f;
        foreach (var w in stare)
        {
            Debug.Log($"Usuwam stara szatnie: {w.name} z {w.transform.position}");
            Undo.DestroyObjectImmediate(w.gameObject);
        }

        // stara byla prymitywem z siatka i BoxColliderem - nowa to sama strefa,
        // bo bryle i kolizje ma juz model szafy
        var strefa = new GameObject("Szatnia");
        Undo.RegisterCreatedObjectUndo(strefa, "Przenies szatnie");
        strefa.transform.SetParent(go.transform.parent, false);

        // gracz podchodzi do frontu, wiec strefa siedzi na wysokosci gracza, nie u stop szafy
        var r = go.GetComponentsInChildren<Renderer>();
        var b = r[0].bounds;
        foreach (var x in r) b.Encapsulate(x.bounds);
        strefa.transform.position = new Vector3(b.center.x, b.min.y + 1f, b.center.z);

        strefa.AddComponent<WardrobeShop>().radius = promien;
        Undo.CollapseUndoOperations(grupa);

        Selection.activeGameObject = strefa;
        Debug.Log($"Szatnia przeniesiona do {go.name}: {strefa.transform.position}, promien {promien} m. " +
                  $"Usunieto starych: {stare.Length}.");
    }
}
