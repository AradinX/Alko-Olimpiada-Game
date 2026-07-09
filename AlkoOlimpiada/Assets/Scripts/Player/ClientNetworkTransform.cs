using Unity.Netcode.Components;

// ponytail: autorytet właściciela (klient steruje swoją pozycją) — OK dla gry ze znajomymi,
// server-authoritative dopiero gdyby cheatowanie było problemem
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
