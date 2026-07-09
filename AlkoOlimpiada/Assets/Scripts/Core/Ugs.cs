using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

// Jedno wspólne logowanie do Unity Gaming Services — Vivox i Relay wołają to samo,
// żeby nie ścigały się o SignInAnonymously ("player is already signing in").
public static class Ugs
{
    static Task task;

    public static Task EnsureSignedIn() => task ??= DoAsync();

    static async Task DoAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                // -profile X: osobne anonimowe konto per instancja (testy na 1 maszynie)
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-profile");
                if (i >= 0 && i + 1 < args.Length) options.SetProfile(args[i + 1]);
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch
        {
            task = null; // pozwól spróbować ponownie
            throw;
        }
    }
}
