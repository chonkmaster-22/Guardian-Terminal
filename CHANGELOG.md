# Changelog

## 0.5.0 - languages, GameBanana downloads, many mods at once

**Languages**
- Guardian Terminal speaks English, Español, Français, Deutsch, Italiano, Português (Brasil), 日本語 and 简体中文
  (Settings > Language, or Automatic to follow Windows). Mod notes, merge results and errors are translated too.

**GameBanana, inside the app**
- Browse, search, sort and filter Breath of the Wild Switch mods by category, see each mod's files and install them
  without opening the website. Paste a GameBanana link to open that mod. Installed mods are marked.
- Mods GameBanana marks as mature are hidden unless you turn them on in Settings.

**Many mods at once**
- Import many: finds mods in emulators (Ryujinx, yuzu and the like) and on a Switch SD card, or installs a whole
  folder of downloads. A mod that fails doesn't stop the rest; one summary at the end.
- Already installed? Replace, keep both or skip, once for all of them if you like.
- Select several mods (Ctrl+click, Shift+click, Ctrl+A) to turn on / off, move or uninstall them together.
- Filters above the list: All, On, Off, Updates, Needs a look.

**Big mods**
- Archives inside archives are unpacked; downloads with several versions ask which ones to install.
- .7z and .rar unpack even without 7-Zip installed (Windows' own tar).
- Merging keeps within a memory budget, so many mods changing the same big packs can't run the PC out of memory.

## 0.4.0 - the first public release

Guardian Terminal (until now "Savage Wind Mod Manager") goes public.

**New window**
- A sidebar with Mods, Profiles, GameBanana, Saves, Tools, Settings and About.
- Mods are cards with their pictures, on / off switches and load-order numbers. Drag to reorder, search with Ctrl+F.
- A details panel: what a mod changes, what it shares with your other mods (merged, or which mod wins), notes.
- Merge is a small button next to Play; Play unmodded is next to it.
- A first-run welcome with the credits, and an About page.
- A new Guardian emblem and app icon.

**Merging**
- Texts (`.msbt`) are merged message by message: two mods' item names and dialogue both stay. This takes care of
  most of the conflicts between big mods (from 1,066 down to 51 for Relics of the Past + the randomizer).
- Texts from mods made for older game versions get 1.9.0's messages back.
- Files are processed several at a time, biggest first: merging Relics of the Past and the randomizer takes about
  11 seconds instead of 42.
- Big files are compared a piece at a time instead of being read whole; a file that fails to merge falls back to
  the mod's own copy, and the rest of the build goes on.
- The merge report lists every conflict; a summary feeds the details panel.

**New**
- DLC switch: turn the DLC on or off for Savage Wind.
- Save backups: by themselves before a game with a different set of mods, or by hand; restore in two clicks.
- Savage Wind setup: install or update it from its own GitHub releases; copy saves to a new version.
- Update checks (daily, optional): Guardian Terminal, Savage Wind, and mods installed from GameBanana.
- The GameBanana page is part of the window; mods from there get their pictures and authors.
- Profiles can be shared as `.gtprofile` files.
- Installing a mod checks there's room for it first and shows how far unpacking got.

## 0.3.3
- Map files and spawn tables (and other BYML files several mods change) are merged entry by entry, so Relics of
  the Past's enemies spawn alongside the randomizer's changes.

## 0.3.2
- The game's progress flags and the save format are merged flag by flag; the actor list actor by actor.

## 0.3.1
- Mods made for game version 1.6.0: their copies of the sound links, effect links and HUD layout (which crash
  1.9.0) are left out and the game's own are kept.

## 0.3.0
- Profiles, Merge, Empty merge output, Export to SD card, install from GameBanana, the loading screen.

## 0.2.0
- The DLC's files (Savage Wind Alpha 4).

## 0.1.0
- First version: Switch mods merged into Savage Wind's RomFS overlay.
