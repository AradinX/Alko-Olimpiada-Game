using Unity.Netcode;
using UnityEngine;

// Garderoba gracza: bitmaska założonych ciuchów, replikowana po sieci.
// Domyślnie 0 = goła postać (widoczne tylko "body" z GuyWardrobe.fbx).
// Meshe ubrań i mapowanie item->mesh wypełnia ProjectBootstrap.SetupWardrobe;
// przełącza się je w Szatni na hubie (WardrobeShop). Wybór trzymamy
// w PlayerPrefs, żeby strój przeżył restart gry.
public class PlayerOutfit : NetworkBehaviour
{
    public string[] itemNames;   // pozycje w szatni (bit maski = indeks itemu)
    public int[] itemSlot;       // slot itemu (Głowa/Strój/Spodnie/Pas/Buty) — jedna rzecz na slot
    public GameObject[] pieces;  // meshe ubrań w modelu (Body/Guy)
    public int[] pieceItem;      // item włączający dany mesh (Buty/Niewolnik mają 2 meshe)

    public NetworkVariable<uint> Mask = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        Mask.OnValueChanged += (_, m) => Apply(m);
        if (IsOwner)
        {
            // maska z PlayerPrefs może pochodzić sprzed slotów — zostaw pierwszą rzecz na slot
            uint m = (uint)PlayerPrefs.GetInt("outfit", 0);
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < itemNames.Length; i++)
                if ((m & (1u << i)) != 0 && !seen.Add(Slot(i))) m &= ~(1u << i);
            Mask.Value = m;
        }
        Apply(Mask.Value);

        // smoke test (jak -autosprint itp.): po 12 s załóż zbroję i buty, zaloguj stan
        if (IsOwner && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autooutfit") >= 0)
            Invoke(nameof(AutoOutfit), 12f);
    }

    void AutoOutfit()
    {
        Toggle(0); Toggle(11); // Zbroja + Buty (różne sloty — mają współistnieć)
        int on = 0;
        foreach (var p in pieces) if (p != null && p.activeSelf) on++;
        Debug.Log($"[Outfit] auto: maska={Mask.Value} aktywnychMeshy={on}");
    }

    public bool Worn(int item) => (Mask.Value & (1u << item)) != 0;

    // brak/za krótki itemSlot w prefabie nie może wysadzić Toggle —
    // item bez wpisu dostaje unikalny slot (bez ekskluzywności)
    int Slot(int i) => itemSlot != null && i < itemSlot.Length ? itemSlot[i] : ~i;

    // wołane tylko u właściciela (WardrobeShop); założenie zdejmuje resztę z tego slotu
    public void Toggle(int item)
    {
        uint m = Mask.Value ^ (1u << item);
        if ((m & (1u << item)) != 0)
            for (int i = 0; i < itemNames.Length; i++)
                if (i != item && Slot(i) == Slot(item)) m &= ~(1u << i);
        Mask.Value = m;
        PlayerPrefs.SetInt("outfit", (int)m);
    }

    void Apply(uint m)
    {
        for (int i = 0; i < pieces.Length; i++)
            if (pieces[i] != null)
                pieces[i].SetActive((m & (1u << pieceItem[i])) != 0);
        if (m != 0) Debug.Log($"[Outfit] gracz {OwnerClientId}: maska={m}"); // ślad replikacji do smoke testów
    }
}
