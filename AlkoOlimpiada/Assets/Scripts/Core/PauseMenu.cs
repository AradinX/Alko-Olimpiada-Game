using Unity.Netcode;
using UnityEngine;

// Ustawienia lokalne gracza — trzymane w PlayerPrefs, czytane bezpośrednio przez
// PlayerController (czułość), Sfx (głośność) i DrunkSystem (bujanie — dostępność, GDD 10).
public static class GameSettings
{
    public static float MouseSens = PlayerPrefs.GetFloat("sens", 1f);
    public static float SfxVol = PlayerPrefs.GetFloat("sfx", 1f);
    public static float Sway = PlayerPrefs.GetFloat("sway", 1f);

    public static void Save()
    {
        PlayerPrefs.SetFloat("sens", MouseSens);
        PlayerPrefs.SetFloat("sfx", SfxVol);
        PlayerPrefs.SetFloat("sway", Sway);
        PlayerPrefs.Save();
    }
}

// Panel ustawień widoczny po ESC (PlayerController odblokowuje wtedy kursor).
// Rozłączenie jest w boksie ConnectionUI obok. ponytail: IMGUI jak reszta UI.
public class PauseMenu : MonoBehaviour
{
    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient || Cursor.lockState == CursorLockMode.Locked) return;
        if (WardrobeShop.Open) return; // kursor odblokowała szatnia, nie ESC

        GUILayout.BeginArea(new Rect(Screen.width / 2f - 150f, Screen.height / 2f - 110f,
            300f, 220f), GUI.skin.box);
        GUILayout.Label("USTAWIENIA   (ESC = powrót do gry)");
        GUILayout.Space(4);
        GUILayout.Label($"Czułość myszy: {GameSettings.MouseSens:0.00}");
        GameSettings.MouseSens = GUILayout.HorizontalSlider(GameSettings.MouseSens, 0.2f, 2.5f);
        GUILayout.Space(4);
        GUILayout.Label($"Głośność efektów: {GameSettings.SfxVol:P0}");
        GameSettings.SfxVol = GUILayout.HorizontalSlider(GameSettings.SfxVol, 0f, 1f);
        GUILayout.Space(4);
        GUILayout.Label($"Bujanie ekranu po pijaku: {GameSettings.Sway:P0}");
        GUILayout.Label("(zmniejsz przy chorobie lokomocyjnej)", GUI.skin.label);
        GameSettings.Sway = GUILayout.HorizontalSlider(GameSettings.Sway, 0f, 1f);
        if (GUI.changed) GameSettings.Save();
        GUILayout.EndArea();
    }
}
