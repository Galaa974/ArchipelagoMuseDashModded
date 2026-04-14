using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.PeroTools.Managers;
using Newtonsoft.Json;
using System.Linq;

namespace ArchipelagoMuseDash;

/// <summary>
///     Contains information about the game's songs, in a way that is easier to access for us.
/// </summary>
public class AlbumDatabase {

    public const int CHINESE_LOC_INDEX = 0;
    public const int ENGLISH_LOC_INDEX = 1;
    public const string RANDOM_PANEL_UID = "?";
    private const int starting_music_item_id = 2900000 + 50; //Start ID + Music ID Offset

    // Plage d'IDs réservée aux musiques custom (doit correspondre à ModdedSongs.py)
    private const long CUSTOM_SONG_ID_MIN = 2_960_000;
    private const long CUSTOM_SONG_ID_MAX = 2_999_999;

    private static readonly Dictionary<string, string> _currentNamesToOldNames = new() {
        { "Crimson Nightingale", "Crimson Nightingle" },
        { "Tsukuyomi Ni Naru", "Territory Battles" },
        { "Suito", "Cuidu" },
    };

    public static readonly Dictionary<string, string> UidOverrides = new() {
        { "74-2", "74-6" }
    };

    private Dictionary<string, MusicInfo> _songsByItemName = new();
    private Dictionary<string, List<MusicInfo>> _songsByAlbum = new();

    private Dictionary<string, MusicInfo> _songsByUid = new();
    private readonly Dictionary<long, string> _songIDToUid = new();
    private readonly Dictionary<long, string> _albumIDToAlbumString = new(); //Not Used yet

    // Musiques custom : itemId → MusicInfo (peuplé après que CustomAlbums charge ses musiques)
    private readonly Dictionary<long, MusicInfo> _customSongsByItemId = new();
    // uid → nom Archipelago (ex: "999-5" → "Ninja Re Bang Bang")
    // Utilisé par GetItemNameFromMusicInfo pour retourner le bon nom pour les checks de location
    private readonly Dictionary<string, string> _customSongUidToName = new();
    // Flag pour savoir si Setup() a déjà été appelé (CustomAlbums peut charger avant ou après)
    private bool _setupComplete = false;

    // JSON des custom songs reçu depuis les slot data Archipelago (fallback si pas de fichier local)
    private string _slotDataCustomSongsJson = null;

#if DEBUG
    //For file writing purposes though may be helpful elsewhere
    public readonly Dictionary<string, long> SongUidToId = new();
#endif

    public void Setup() {
        _songsByAlbum.Clear();
        _songsByItemName.Clear();
        _customSongsByItemId.Clear();
        _customSongUidToName.Clear();

        var list = new Il2CppSystem.Collections.Generic.List<MusicInfo>();
        GlobalDataBase.dbMusicTag.GetAllMusicInfo(list);

        _songsByAlbum = new Dictionary<string, List<MusicInfo>>();
        _songsByItemName = new Dictionary<string, MusicInfo>();
        _songsByUid = new Dictionary<string, MusicInfo>();

        var configManager = ConfigManager.instance;
        if (configManager == null)
            throw new Exception("Config Manage was null when trying to load songs.");

        var albumConfig = configManager.GetConfigObject<DBConfigAlbums>();
        var albumLocalisation = configManager.GetConfigObject<DBConfigAlbums>().GetLocal(ENGLISH_LOC_INDEX);

        foreach (var musicInfo in list) {
            if (musicInfo.uid == RANDOM_PANEL_UID || musicInfo.uid.StartsWith("999"))
                continue;

            var albumLocal = albumLocalisation.GetLocalTitleByIndex(albumConfig.GetAlbumInfoByAlbumJsonIndex(musicInfo.albumJsonIndex).listIndex);

            var songName = GetItemNameFromMusicInfo(musicInfo);
            if (!_songsByItemName.TryAdd(songName, musicInfo)) {
                ArchipelagoStatic.ArchLogger.Warning("[Album Database]", $"Duplicate Song Name found. Id: {musicInfo.uid}");
                continue;
            }

            _songsByUid.Add(musicInfo.uid, musicInfo);

            if (_currentNamesToOldNames.TryGetValue(songName, out var oldName))
                _songsByItemName.Add(oldName, musicInfo);

            if (!_songsByAlbum.TryGetValue(albumLocal, out var albumList)) {
                albumList = new List<MusicInfo>();
                _songsByAlbum.Add(albumLocal, albumList);
            }

            albumList.Add(musicInfo);
        }

        // Charge les musiques custom APRÈS que _songsByItemName est peuplé.
        // CustomAlbums peut avoir déjà chargé ses musiques (si son init est antérieur),
        // ou pas encore (auquel cas ReloadCustomSongs() sera appelé plus tard via le patch).
        _setupComplete = true;
        LoadCustomSongs();
    }

    /// <summary>
    ///     Reçoit les custom songs depuis les slot data Archipelago (JSON sérialisé).
    ///     Stocke les données pour les utiliser lors du prochain LoadCustomSongs()
    ///     si aucun fichier custom_songs.json local n'est trouvé.
    /// </summary>
    public void SetSlotDataCustomSongsFromJson(string json) {
        _slotDataCustomSongsJson = json;
        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "Slot data custom songs received.");
    }

    /// <summary>
    ///     Appelé par le patch Harmony sur CustomAlbums après que ses musiques sont chargées.
    ///     Recharge le mapping custom songs → MusicInfo avec les nouvelles MusicInfos disponibles.
    /// </summary>
    public void ReloadCustomSongs() {
        if (!_setupComplete) {
            ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "ReloadCustomSongs called but setup not complete.");
            return;
        }
        if (ArchipelagoStatic.UseModdedSongs == null || !ArchipelagoStatic.UseModdedSongs.Value) {
            ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "UseModdedSongs is disabled, skipping custom songs.");
            return;
        }
        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "ReloadCustomSongs called — scanning for new songs.");
        var list = new Il2CppSystem.Collections.Generic.List<MusicInfo>();
        GlobalDataBase.dbMusicTag.GetAllMusicInfo(list);
        int newCount = 0;
        foreach (var musicInfo in list) {
            if (musicInfo.uid == RANDOM_PANEL_UID || musicInfo.uid.StartsWith("999"))
                continue;
            var songName = GetItemNameFromMusicInfo(musicInfo);
            if (_songsByItemName.TryAdd(songName, musicInfo)) {
                newCount++;
                ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", $"New song: uid='{musicInfo.uid}' name='{songName}'");
            }
            _songsByUid.TryAdd(musicInfo.uid, musicInfo);
        }
        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", $"ReloadCustomSongs: {newCount} new songs. Total: {_songsByItemName.Count}");
        _customSongsByItemId.Clear();
        _customSongUidToName.Clear();
        LoadCustomSongs();
    }

    /// <summary>
    ///     Lit custom_songs.json (ou le JSON reçu depuis les slot data) et construit le mapping
    ///     itemId → MusicInfo pour les musiques moddées.
    ///     Cherche la MusicInfo correspondante dans celles déjà chargées par CustomAlbums,
    ///     en matchant par le champ "name" (= nom Archipelago = nom affiché dans le jeu).
    /// </summary>
    private void LoadCustomSongs() {
        if (ArchipelagoStatic.UseModdedSongs == null || !ArchipelagoStatic.UseModdedSongs.Value)
            return;

        string rawJson = null;
        var jsonPath = FindCustomSongsJson();
        if (jsonPath != null) {
            rawJson = File.ReadAllText(jsonPath);
        } else if (_slotDataCustomSongsJson != null) {
            rawJson = _slotDataCustomSongsJson;
            ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "Using slot data custom songs (no local JSON found).");
        } else {
            ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", "No custom_songs.json found and no slot data, skipping custom songs.");
            return;
        }

        List<CustomSongEntry> entries;
        try {
            entries = JsonConvert.DeserializeObject<List<CustomSongEntry>>(rawJson);
        }
        catch (Exception e) {
            ArchipelagoStatic.ArchLogger.Error("AlbumDatabase", e);
            return;
        }

        // Récupère toutes les MusicInfo de CustomAlbums via reflection
        var customMusicByName = GetCustomAlbumsMusicInfo();
        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", $"LoadCustomSongs: found {customMusicByName.Count} custom MusicInfo via CustomAlbums reflection.");

        var loaded = 0;
        foreach (var entry in entries) {
            if (entry.item_id < CUSTOM_SONG_ID_MIN || entry.item_id > CUSTOM_SONG_ID_MAX) {
                ArchipelagoStatic.ArchLogger.Warning("AlbumDatabase",
                    $"Custom song '{entry.name}' has out-of-range ID {entry.item_id}, skipping.");
                continue;
            }

            // Cherche dans les musiques custom par nom (filename = nom du .mdm sans extension)
            if (!customMusicByName.TryGetValue(entry.name, out var musicInfo) &&
                !customMusicByName.TryGetValue(entry.filename, out musicInfo)) {
                ArchipelagoStatic.ArchLogger.Warning("AlbumDatabase",
                    $"Custom song '{entry.name}' (filename='{entry.filename}') not found in CustomAlbums. Is the .mdm installed?");
                continue;
            }

            _customSongsByItemId[entry.item_id] = musicInfo;
            // Enregistre aussi dans _songsByItemName pour que les locations soient reconnues
            _songsByItemName.TryAdd(entry.name, musicInfo);
            _songsByUid.TryAdd(musicInfo.uid, musicInfo);
            // Mapping uid → nom Archipelago pour GetItemNameFromMusicInfo
            _customSongUidToName[musicInfo.uid] = entry.name;

            ArchipelagoStatic.ArchLogger.Log("AlbumDatabase",
                $"Custom song mapped: '{entry.name}' → uid={musicInfo.uid} id={entry.item_id}");
            loaded++;
        }

        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", $"Loaded {loaded} custom song mapping(s).");
    }

    /// <summary>
    ///     Accède aux musiques chargées par CustomAlbums via reflection.
    ///     CustomAlbums v4.x stocke ses albums dans AlbumManager.LoadedAlbums (Dictionary<int, Album>).
    ///     Chaque Album expose ses pistes via GetMusicInfos() ou une propriété Musics.
    ///     On construit un dictionnaire name→MusicInfo et filename→MusicInfo.
    /// </summary>
    private Dictionary<string, MusicInfo> GetCustomAlbumsMusicInfo() {
        var result = new Dictionary<string, MusicInfo>(StringComparer.OrdinalIgnoreCase);
        try {
            // Trouve l'assembly CustomAlbums
            var customAlbumsAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "CustomAlbums");
            if (customAlbumsAsm == null) {
                ArchipelagoStatic.ArchLogger.Warning("AlbumDatabase", "CustomAlbums assembly not found.");
                return result;
            }

            // Cherche AlbumManager
            var albumManagerType = customAlbumsAsm.GetType("CustomAlbums.Managers.AlbumManager");
            if (albumManagerType == null) {
                ArchipelagoStatic.ArchLogger.Warning("AlbumDatabase", "AlbumManager type not found in CustomAlbums.");
                return result;
            }

            // Cherche LoadedAlbums (static property ou field)
            object loadedAlbums = null;
            var prop = albumManagerType.GetProperty("LoadedAlbums",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            if (prop != null)
                loadedAlbums = prop.GetValue(null);
            else {
                var field = albumManagerType.GetField("LoadedAlbums",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic);
                if (field != null) loadedAlbums = field.GetValue(null);
            }

            if (loadedAlbums == null) {
                var members = albumManagerType.GetMembers(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic);
                var memberNames = string.Join(", ", members.Select(m => m.Name));
                ArchipelagoStatic.ArchLogger.Warning("AlbumDatabase", $"LoadedAlbums not found. AlbumManager members: {memberNames}");
                return result;
            }

            // Itère sur le dictionnaire
            var dictType = loadedAlbums.GetType();
            var values = dictType.GetProperty("Values")?.GetValue(loadedAlbums) as System.Collections.IEnumerable;
            if (values == null) {
                ArchipelagoStatic.ArchLogger.Warning("AlbumDatabase", "Could not get Values from LoadedAlbums.");
                return result;
            }

            foreach (var album in values) {
                var albumType = album.GetType();
                try {
                    var uidProp = albumType.GetProperty("Uid");
                    var infoProp = albumType.GetProperty("Info");

                    var uid = uidProp?.GetValue(album)?.ToString() ?? "";

                    // Lit le titre depuis Info.Name (ex: "CORONA")
                    string songName = "";
                    if (infoProp != null) {
                        var info = infoProp.GetValue(album);
                        if (info != null) {
                            var nameProp = info.GetType().GetProperty("Name");
                            songName = nameProp?.GetValue(info)?.ToString() ?? "";
                        }
                    }

                    // AlbumName = "album_Ninja Re Bang Bang" → strip prefix → "Ninja Re Bang Bang"
                    var albumNameProp2 = albumType.GetProperty("AlbumName");
                    var rawAlbumName = albumNameProp2?.GetValue(album)?.ToString() ?? "";
                    var mdmFilename = rawAlbumName.StartsWith("album_")
                        ? rawAlbumName.Substring(6)
                        : rawAlbumName;

                    if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(songName))
                        continue;

                    // Cherche la MusicInfo dans GlobalDataBase par uid (ex: "999-2")
                    if (_songsByUid.TryGetValue(uid, out var musicInfo) && musicInfo != null) {
                        ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", $"CustomAlbums song via uid: name='{songName}' uid='{uid}'");
                        result.TryAdd(songName, musicInfo);
                        // Index aussi par le nom du fichier .mdm pour le matching fallback
                        if (!string.IsNullOrEmpty(mdmFilename))
                            result.TryAdd(mdmFilename, musicInfo);
                    }
                    // Les UIDs custom sont de la forme "999-X" — cherche toutes les variantes
                    else {
                        var baseIndex = uid.Contains("-") ? int.Parse(uid.Split('-')[0]) : -1;
                        if (baseIndex == 999) {
                            var list2 = new Il2CppSystem.Collections.Generic.List<MusicInfo>();
                            GlobalDataBase.dbMusicTag.GetAllMusicInfo(list2);
                            foreach (var mi in list2) {
                                if (mi.uid == uid) {
                                    ArchipelagoStatic.ArchLogger.Log("AlbumDatabase", $"CustomAlbums song via scan: name='{songName}' uid='{uid}'");
                                    result.TryAdd(songName, mi);
                                    if (!string.IsNullOrEmpty(mdmFilename))
                                        result.TryAdd(mdmFilename, mi);
                                    _songsByUid.TryAdd(uid, mi);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    ArchipelagoStatic.ArchLogger.Error("AlbumDatabase", ex);
                }
            }
        }
        catch (Exception e) {
            ArchipelagoStatic.ArchLogger.Error("AlbumDatabase", e);
        }
        return result;
    }

    private static string FindCustomSongsJson() {
        const string filename = "custom_songs.json";

        var candidates = new List<string>();

        var gameRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
        if (gameRoot != null) {
            candidates.Add(Path.Combine(gameRoot, filename));
            candidates.Add(Path.Combine(gameRoot, "UserData", filename));
        }

        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), filename));

        foreach (var candidate in candidates) {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public void LoadMusicList(Stream dataTextStream) {
        //This section is to help improve compatibility between versions
        using var sr = new StreamReader(dataTextStream);
        while (!sr.EndOfStream) {
            var line = sr.ReadLine();
            if (string.IsNullOrEmpty(line))
                continue;

            var index = line.LastIndexOf('|');
            var trackUid = line[..index];
            var itemId = long.Parse(line[(index + 1)..]);
            _songIDToUid[itemId] = trackUid;
#if DEBUG
            SongUidToId[trackUid] = itemId;
#endif
        }
    }

    public bool TryGetOldName(string newName, out string oldName) {
        return _currentNamesToOldNames.TryGetValue(newName, out oldName);
    }
    public bool TryGetMusicInfo(string itemName, out MusicInfo info) {
        return _songsByItemName.TryGetValue(itemName, out info);
    }
    public bool TryGetMusicInfoFromUid(string uid, out MusicInfo info) {
        return _songsByUid.TryGetValue(uid, out info);
    }

    public MusicInfo GetMusicInfo(string itemName) {
        return _songsByItemName[itemName];
    }

    public IEnumerable<MusicInfo> GetAllMusic() {
        return _songsByUid.Values;
    }

    public bool TryGetAlbum(string itemName, out List<MusicInfo> infos) {
        return _songsByAlbum.TryGetValue(itemName, out infos);
    }

    public List<MusicInfo> GetAlbum(string itemName) {
        return _songsByAlbum[itemName];
    }

    public string GetItemNameFromMusicInfo(MusicInfo musicInfo) {
        // Pour les chansons custom (uid "999-X"), retourne le nom Archipelago depuis le json
        // plutôt que le Info.Name du jeu qui peut être en japonais garbled
        if (musicInfo.uid.StartsWith("999") && _customSongUidToName.TryGetValue(musicInfo.uid, out var customName))
            return customName;

        var localisedSongName = ArchipelagoStatic.SongNameChanger.GetSongName(musicInfo);
        return $"{localisedSongName}";
    }

    public string GetItemNameFromUid(string uid) {
        if (!_songsByUid.TryGetValue(uid, out var musicInfo))
            return "";

        return GetItemNameFromMusicInfo(musicInfo);
    }

    public bool TryGetSongFromItemId(long itemId, out MusicInfo info) {
        info = null;

        // Vérifie d'abord si c'est une musique custom (plage 2960000+)
        if (itemId >= CUSTOM_SONG_ID_MIN && itemId <= CUSTOM_SONG_ID_MAX)
            return _customSongsByItemId.TryGetValue(itemId, out info);

        // Sinon, chemin normal via MuseDashData.txt
        if (!_songIDToUid.TryGetValue(itemId, out var uid))
            return false;

        if (UidOverrides.TryGetValue(uid, out var replacementUid))
            uid = replacementUid;

        return _songsByUid.TryGetValue(uid, out info);
    }

    public long GetItemIdForSong(MusicInfo info) {
        var pair = _songIDToUid.FirstOrDefault(x => x.Value == info.uid);
        return pair.Value != null ? pair.Key : long.MaxValue;
    }

    public string GetLocalisedSongNameForMusicInfo(MusicInfo musicInfo) {
        var configManager = ConfigManager.instance;
        var songLocal = configManager.GetConfigObject<DBConfigALBUM>(musicInfo.albumJsonIndex).GetLocal().GetLocalAlbumInfoByIndex(musicInfo.listIndex);
        return songLocal.name;
    }

    public string GetLocalisedAlbumNameForMusicInfo(MusicInfo musicInfo) {
        var configManager = ConfigManager.instance;
        var albumConfig = configManager.GetConfigObject<DBConfigAlbums>();
        var albumLocalisation = configManager.GetConfigObject<DBConfigAlbums>().GetLocal();
        var albumLocal = albumLocalisation.GetLocalTitleByIndex(albumConfig.GetAlbumInfoByAlbumJsonIndex(musicInfo.albumJsonIndex).listIndex);
        return albumLocal;
    }

    // Classe interne pour désérialiser custom_songs.json
    private class CustomSongEntry {
        public string name { get; set; }
        public string filename { get; set; }
        public long item_id { get; set; }
        public int? easy { get; set; }
        public int? hard { get; set; }
        public int? master { get; set; }
        public bool streamer_mode { get; set; } = true;
    }
}
