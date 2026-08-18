using UnityEngine;

// Ręcznie strojona poza naczynia w garści: jak leży na stojąco i jak ma wyglądać
// na szczycie animacji picia (żeby kufel nie wchodził postaci w twarz). Siedzi na
// dziecku HandBottle, więc każde naczynie ma własne ustawienie, a chwyt (FollowBone)
// zostaje jeden. Wartości zapisuje menu Alko/Naczynie: zapisz pozę — patrz
// VesselPoseTuning i scena Naczynia_Tuning.
public class VesselPose : MonoBehaviour
{
    public Vector3 idlePosition, idleRotation;
    public Vector3 drinkPosition, drinkRotation;

    // drink = DrunkSystem.DrinkPose (0 stoi, 1 szczyt łyku)
    public void Apply(float drink) => transform.SetLocalPositionAndRotation(
        Vector3.Lerp(idlePosition, drinkPosition, drink),
        Quaternion.Slerp(Quaternion.Euler(idleRotation), Quaternion.Euler(drinkRotation), drink));
}
