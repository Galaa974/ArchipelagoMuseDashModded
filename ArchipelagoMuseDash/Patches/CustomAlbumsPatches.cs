using HarmonyLib;
using Il2Cpp;

namespace ArchipelagoMuseDash.Patches;

/// <summary>
///     Patche MusicTagManager.RefreshDBDisplayMusics pour détecter quand
///     CustomAlbums a fini de charger ses musiques et les injecter dans AlbumDatabase.
/// </summary>
[HarmonyPatch(typeof(MusicTagManager), "RefreshDBDisplayMusics")]
public static class CustomAlbumsPatches {

    private static bool _reloaded = false;

    [HarmonyPostfix]
    public static void RefreshDBDisplayMusicsPatch() {
        if (_reloaded)
            return;

        if (!ArchipelagoStatic.SessionHandler.IsLoggedIn)
            return;

        _reloaded = true;
        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "CustomAlbums detected via RefreshDBDisplayMusics — reloading custom songs.");
        ArchipelagoStatic.AlbumDatabase.ReloadCustomSongs();
    }

    public static void ResetReloadFlag() {
        _reloaded = false;
    }
}
