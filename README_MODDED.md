# Muse Dash — Modded Songs & Archipelago Setup Guide

This guide explains how to include your custom `.mdm` songs in an Archipelago multiworld session, so they appear as randomized locations alongside the base game's tracklist.

---

## Prerequisites

Before starting, make sure you have the following:

- **[MelonLoader](https://melonwiki.xyz/)** installed for Muse Dash
- **[CustomAlbums](https://github.com/MDMods/CustomAlbums)** MelonLoader mod installed (v4.1.9 or later)
- Your `.mdm` song files placed in the `Custom_Albums` folder inside your Muse Dash directory
- The following files from this release:
  - `musedash_modded.apworld`
  - `generate_custom_songs.exe`
  - This mod's `.dll` placed in your `Muse Dash/Mods/` folder

---

## Step 1 — Install the modded apworld

In your Archipelago installation, open the `custom_worlds/` folder and:

1. **Delete** `musedash.apworld` (the original one)
2. **Add** `musedash_modded.apworld` in its place

> ⚠️ Do **not** keep both files at the same time. Having both will cause a conflict and break world generation.

---

## Step 2 — Generate your `custom_songs.json`

This file tells Archipelago which of your custom songs to include as locations.

1. Run `generate_custom_songs.exe`
2. When prompted, select your `Custom_Albums` folder

> **Not sure where `Custom_Albums` is?**
> In Steam: right-click Muse Dash → **Manage** → **Browse local files**. The folder is there.

The tool will generate a `custom_songs.json` file in the same folder as the `.exe`. Keep this file handy for the next step.

---

## Step 3 — Before generating the multiworld

### If you are the host

1. Place `custom_songs.json` in your **Archipelago root folder** (the same folder as `ArchipelagoGenerate.exe`)
2. Generate your multiworld as usual

### If someone else is hosting

Send the host:
- Your `.yaml` player file
- `musedash_modded.apworld` and a link to this guide (so they can replace their apworld — Step 1)
- Your `custom_songs.json` → they place it in their Archipelago root folder before generating

---

## Step 4 — In-game setup

When connecting to the Archipelago server from Muse Dash:

1. Click **Show Archipelago Login** from the main menu
2. Fill in the server address, slot name, and password if needed
3. **Check the "Enable Modded Songs" box** before logging in

> ⚠️ If this box is left unchecked, custom songs will be completely ignored — they won't appear in the song list even if they are part of the randomizer logic.

Once logged in, your custom songs will appear in the song select alongside the base game tracks, filtered according to your current display mode (Unlocked / Unplayed / Hinted / All in Logic).

---

## Troubleshooting

**My custom songs don't show up in the song list**
- Make sure "Enable Modded Songs" was checked before logging in
- Make sure the `.mdm` files are in your `Custom_Albums` folder and are loaded by CustomAlbums
- Check `MelonLoader/Latest.log` for any errors mentioning `AlbumDatabase` or `CustomAlbums`

**I get generation errors**
- Make sure only `musedash_modded.apworld` is in your `custom_worlds/` folder, not the original
- Make sure `custom_songs.json` is in the Archipelago root folder before generating

**Songs are visible but can't be played / aren't tracked**
- Verify the song names in `custom_songs.json` exactly match the names displayed by CustomAlbums in-game
- Re-run `generate_custom_songs.exe` to regenerate a fresh `custom_songs.json`
