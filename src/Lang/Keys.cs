namespace GuardianTerminal.Lang
{
    // Every English text Guardian Terminal shows, made by tools\l10n.ps1 from the source (new ones at the end, so
    // the translations, one per line in this order, stay lined up). Don't edit by hand.
    internal static class Keys
    {
        public static readonly string[] All =
        {
            "({0} of them in the DLC)",  // 0
            "{1} game files changed by {0} mod",  // 1
            "{1} game files changed by {0} mods",  // 2
            "{0} pack merged",  // 3
            "{0} packs merged",  // 4
            "{0} file merged entry by entry",  // 5
            "{0} files merged entry by entry",  // 6
            "{0} conflict",  // 7
            "{0} conflicts",  // 8
            "no mods are on",  // 9
            "Checking your mods...",  // 10
            "mods ready",  // 11
            "Reading {0}...",  // 12
            "Merging mod files... {0}/{1} ({2}%)",  // 13
            "Updating the resource size table...",  // 14
            "Clearing the Breath of the Wild mods on the SD card...",  // 15
            "Savage Wind closed before the game appeared.",  // 16
            "Savage Wind closed before the game appeared ({0}).",  // 17
            "Savage Wind hasn't made any progress for {0} minutes: it may be stuck.",  // 18
            "exit code {0}",  // 19
            "exit code {0}, a crash",  // 20
            "Savage Wind stopped with an error: {0}",  // 21
            "Close Savage Wind first: it's using the DLC's files.",  // 22
            "savagewind.exe wasn't found.",  // 23
            "Savage Wind hasn't set up your game files in this folder yet. Start Savage Wind once and finish its setup, then come back.",  // 24
            "Downloading Savage Wind {0}... {1}% of {2:N0} MB",  // 25
            "Downloading Savage Wind {0}... {1:N0} MB",  // 26
            "{0} already exists and isn't empty: choose another folder.",  // 27
            "Extracting Savage Wind {0}...",  // 28
            "Extracting Savage Wind {0}... {1}%",  // 29
            "The Savage Wind download has no savagewind.exe in it.",  // 30
            "Close Savage Wind first: it's using the saves.",  // 31
            "GameBanana didn't return a mod page for {0}.",  // 32
            "The file isn't there anymore: {0}",  // 33
            "Unpacking {0}...",  // 34
            "Looking for the mod's game files...",  // 35
            "This is a Tears of the Kingdom mod, not a Breath of the Wild one.",  // 36
            "This is a Wii U (Cemu) mod. Savage Wind runs the Switch version of the game, so it needs Switch mods (look for a romfs folder).",  // 37
            "No game files were found in it (no romfs folder). It may not be a Breath of the Wild Switch mod.",  // 38
            "Copying {0}...",  // 39
            "Checking {0} against your game files...",  // 40
            "Its files are in the Wii U format. Savage Wind runs the Switch version, so this mod won't work. Look for a Switch version of it.",  // 41
            "{0} of its files change the DLC, including its world map. Your DLC is set up in Savage Wind, so they load too.",  // 42
            "{0} of its files change the DLC. Your DLC is set up in Savage Wind, so they load too.",  // 43
            "Most of this mod is made for the DLC ({0} DLC files, including its world map changes). The DLC isn't set up in this Savage Wind, so only its {1} base-game files would load, which can cause bugs or crashes. Set up both DLC in Savage Wind (Alpha 4 or later) to use it; the DLC files are kept until then.",  // 44
            "Most of this mod is made for the DLC ({0} DLC files). The DLC isn't set up in this Savage Wind, so only its {1} base-game files would load, which can cause bugs or crashes. Set up both DLC in Savage Wind (Alpha 4 or later) to use it; the DLC files are kept until then.",  // 45
            "{0} DLC files are kept but not used: the DLC isn't set up in this Savage Wind.",  // 46
            "{0} files are in folders the game doesn't have, and Savage Wind can only add files to folders that exist. They're left out.",  // 47
            "Changes core game packs ({0}). They're merged into your own game files, so 1.9.0 content stays, but mods made for older game versions can still cause problems.",  // 48
            "Made for an older game version: its {0} would crash your {1} game files, so your game's own are used instead. Everything else in it loads, but some of its sounds, effects or HUD changes may be missing.",  // 49
            "Includes code patches (exefs / .ips), which Savage Wind doesn't support. They're ignored.",  // 50
            "This BCML package keeps some of its changes in BCML's merge logs ({0}), which this manager can't apply yet: those parts won't show in the game. Its files themselves load.",  // 51
            "This BCML package keeps some of its changes in BCML's merge logs, which this manager can't apply yet: those parts won't show in the game. Its files themselves load.",  // 52
            "BCML package: everything in it loads (its own resource sizes are used too).",  // 53
            "{0} of its files are exactly the same as your game's, so they're skipped.",  // 54
            "{0} non-game files (readme, logs, Version.txt...) are ignored.",  // 55
            "Nothing in it can be loaded by Savage Wind.",  // 56
            "Unpacking {0}... {1}%",  // 57
            "7-Zip couldn't unpack {0}: {1}",  // 58
            "The zip file couldn't be read: {0}",  // 59
            "Unpacking {0}... {1} files",  // 60
            "{0} couldn't be unpacked ({1}). Installing 7-Zip (free, 7-zip.org) may help.",  // 61
            "Unpacking {0} files needs 7-Zip (free, 7-zip.org). Install it, or unpack the mod yourself and use Add mod folder.",  // 62
            "There isn't enough free space on {0} to unpack {1}: it needs about {2:N1} GB and {3:N1} GB is free. Free up some space and try again.",  // 63
            "Added by hand: check it in the build report after you merge.",  // 64
            "Switch SD card ({0})",  // 65
            "Mods from the SD card",  // 66
            "Checking {0} on GameBanana...",  // 67
            "That file isn't a Guardian Terminal profile.",  // 68
            "Guardian Terminal is already open.",  // 69
            "Something went wrong:",  // 70
            "Details were saved to logs\\crash.txt.",  // 71
            "Choose a version",  // 72
            "Use this one",  // 73
            "Cancel",  // 74
            "All",  // 75
            "Already installed",  // 76
            "{0} is already installed. Replace it with this one (it keeps its place and on / off in every profile), keep both, or skip it?",  // 77
            "Do the same for the other mods already installed",  // 78
            "Replace",  // 79
            "Keep both",  // 80
            "Skip",  // 81
            "Import mods",  // 82
            "Mods found on this PC: in emulators (Ryujinx, yuzu and the like) and on a Switch SD card. Tick the ones to install, or add a folder of mod downloads.",  // 83
            "Add a folder of mods...",  // 84
            "Install",  // 85
            "with DLC files",  // 86
            "No emulator or SD card mods were found on this PC. Add a folder of mod downloads instead (each .zip, .7z, .rar, .bnp and mod folder in it is installed).",  // 87
            "A folder with mod downloads (.zip, .7z, .rar, .bnp) or mod folders in it",  // 88
            "No mods were found in that folder.",  // 89
            "Install ({0})",  // 90
            "Breath of the Wild Switch mods: find one, pick a file, install it. No browser needed.",  // 91
            "Search GameBanana, or paste a mod's link",  // 92
            "Most liked",  // 93
            "Newest",  // 94
            "Recently updated",  // 95
            "Previous",  // 96
            "Next",  // 97
            "Best match",  // 98
            "All categories",  // 99
            "Loading mods from GameBanana...",  // 100
            "Couldn't reach GameBanana: {0}",  // 101
            "No Breath of the Wild Switch mods match “{0}”.",  // 102
            "No mods here.",  // 103
            "Page {0} of {1}  ·  {2} mods",  // 104
            "Download and install",  // 105
            "Pick a mod to see its files.",  // 106
            "Loading...",  // 107
            "Couldn't get that mod from GameBanana: {0}",  // 108
            "by {0}",  // 109
            "installed",  // 110
            "scan ok",  // 111
            "scan: {0}",  // 112
            "This mod is for {0}. Savage Wind needs Breath of the Wild SWITCH mods: it probably won't work.",  // 113
            "No description.",  // 114
            "Download and reinstall",  // 115
            "GameBanana's malware scan of this file says: \"{0}\". Download it anyway?",  // 116
            "Download",  // 117
            "The download failed: {0}",  // 118
            "{0} views",  // 119
            "Starting Breath of the Wild",  // 120
            "With your mods: {0}.",  // 121
            "This screen closes when the game appears.",  // 122
            "Hide this screen",  // 123
            "{0}:{1:00}  -  the first start after an update or a new mod can take a minute or two.",  // 124
            "Starting Savage Wind",  // 125
            "Loading the game code",  // 126
            "Reading the game data",  // 127
            "Loading your mods",  // 128
            "Loading the DLC (if it's set up)",  // 129
            "Starting the game",  // 130
            "Savage Wind couldn't start the game",  // 131
            "The end of Savage Wind's log:",  // 132
            "If it only happens with mods on, turn them off one at a time to find the one causing it.",  // 133
            "Close",  // 134
            "Open the full log",  // 135
            "Keep waiting",  // 136
            "Looking for it...",  // 137
            "Turn the DLC (The Master Trials and The Champions' Ballad) on or off for Savage Wind",  // 138
            "Merge",  // 139
            "Merge the mods that are on into the files Savage Wind loads, now (Play does it by itself when something changed)",  // 140
            "Play unmodded",  // 141
            "Start Savage Wind without any mods (your mods stay as they are)",  // 142
            "Play",  // 143
            "Ready. Add a mod to begin: Add mod, the GameBanana page, or drop a mod file on the window.",  // 144
            "Looking for Savage Wind...",  // 145
            "Savage Wind needs its game files set up: start it once and pick your own game files (1.9.0).",  // 146
            "Savage Wind ({0}) needs its game files set up before mods can be played.",  // 147
            "Start its setup",  // 148
            "Savage Wind wasn't found. Open Settings to install it or to show Guardian Terminal where it is.",  // 149
            "Savage Wind wasn't found on this PC. Guardian Terminal needs it to play your mods.",  // 150
            "Set up Savage Wind",  // 151
            "Not found: open Settings",  // 152
            "Game files {0}",  // 153
            "DLC: not set up",  // 154
            "DLC: off",  // 155
            "DLC: on",  // 156
            "First set up Savage Wind: open Settings to install it, or to show Guardian Terminal where it is.",  // 157
            "Reading your game files...",  // 158
            "Couldn't read the game files: {0}",  // 159
            "Checking your mods against your game files...",  // 160
            "Couldn't check the mods: {0}",  // 161
            "Ready.",  // 162
            "Switch the DLC off?",  // 163
            "Savage Wind then starts without The Master Trials and The Champions' Ballad. Nothing is deleted: its folders are renamed, and switching it back on is instant.",  // 164
            "These mods use the DLC, so their DLC parts are left out while it's off (they may not work right):",  // 165
            "A save that used DLC items may miss them until the DLC is back on.",  // 166
            "Switch the DLC off",  // 167
            "Couldn't switch the DLC on: {0}",  // 168
            "Couldn't switch the DLC off: {0}",  // 169
            "The DLC is on. Your mods are merged again for it when you press Play.",  // 170
            "The DLC is off. Your mods are merged again for it when you press Play.",  // 171
            "Checking for updates...",  // 172
            "Guardian Terminal {0} is out (you have {1}).",  // 173
            "Get it on GitHub",  // 174
            "Savage Wind {0} is out (you have {1}).",  // 175
            "Install it",  // 176
            "Everything's up to date.",  // 177
            "Updates found: see the notice at the top.",  // 178
            "Guardian Terminal is still working. Close it anyway?",  // 179
            "{0} of your mods has a newer version on GameBanana: look for \"Update\" on it.",  // 180
            "{0} of your mods have a newer version on GameBanana: look for \"Update\" on them.",  // 181
            "Profiles: separate sets of mods, each with its own on / off and load order",  // 182
            "Search your mods  (Ctrl+F)",  // 183
            "Cards with pictures, or compact rows",  // 184
            "Add mod",  // 185
            "Mod files (.zip, .7z, .rar, .bnp)...",  // 186
            "Mod folder (already unpacked)...",  // 187
            "Import many: emulators, SD card, a folder of mods...",  // 188
            "Browse GameBanana",  // 189
            "{0} moved to place {1}. Later mods win when two change the same file.",  // 190
            "On",  // 191
            "Off",  // 192
            "Updates",  // 193
            "Needs a look",  // 194
            "Turn on ({0})",  // 195
            "Turn off ({0})",  // 196
            "Move to the top (load first)",  // 197
            "Move to the bottom (load last: wins)",  // 198
            "Uninstall ({0})...",  // 199
            "Turn off",  // 200
            "Turn on",  // 201
            "Move up (load earlier)",  // 202
            "Move down (load later: wins)",  // 203
            "Move to the top",  // 204
            "Move to the bottom",  // 205
            "Open its folder",  // 206
            "Open its GameBanana page",  // 207
            "Find it on the GameBanana page",  // 208
            "Select all (Ctrl+A)",  // 209
            "Uninstall...",  // 210
            "These probably won't work with Savage Wind:",  // 211
            "Turn them on anyway?",  // 212
            "Turn mods on",  // 213
            "Press Play to use them (they're merged then).",  // 214
            "Moved to the top: loaded first (other mods win over them).",  // 215
            "Moved to the bottom: loaded last (they win over the others).",  // 216
            "Uninstall these {0} mods?",  // 217
            "They're deleted from Guardian Terminal and taken out of every profile. Your original downloads aren't touched.",  // 218
            "Uninstall mods",  // 219
            "Some files couldn't be deleted: close Savage Wind and try again.",  // 220
            "Add a mod file",  // 221
            "Find mods on GameBanana",  // 222
            "Add your first mod",  // 223
            "Drop a mod's .zip, .7z, .rar or .bnp anywhere on this window, or pick one.\nBreath of the Wild Switch mods work (the ones with a romfs folder).",  // 224
            "Update",  // 225
            "A newer file is on its GameBanana page: open it there to install it",  // 226
            "Uninstall it (from every profile)",  // 227
            "To the top",  // 228
            "To the bottom",  // 229
            "Uninstall",  // 230
            "off in this profile",  // 231
            "Needs DLC",  // 232
            "Wii U mod",  // 233
            "Nothing to load",  // 234
            "{0} overridden",  // 235
            "{0} on, {1} off",  // 236
            "Ctrl+click adds or removes one, Shift+click a range.",  // 237
            "No mods yet",  // 238
            "No mod matches",  // 239
            "version {0}",  // 240
            "installed {0}",  // 241
            "On in “{0}”",  // 242
            "Off in “{0}”",  // 243
            "place {0} of {1}",  // 244
            "A newer file is on its GameBanana page ({0}). Press Update to open it there.",  // 245
            "It has its own size table.",  // 246
            "With your other mods (last merge)",  // 247
            "... (all in Tools > Merge report)",  // 248
            "ready",  // 249
            "ready, see notes",  // 250
            "needs the DLC",  // 251
            "Play ({0})",  // 252
            "No mods are on: starts the game as it is (F5)",  // 253
            "This mod probably won't work with Savage Wind.",  // 254
            "Turn it on anyway?",  // 255
            "{0} is on.",  // 256
            "{0} is off.",  // 257
            "Press Play to use it (it's merged then).",  // 258
            "New profile...",  // 259
            "Duplicate this profile...",  // 260
            "Manage profiles...",  // 261
            "Name of the profile:",  // 262
            "There's already a profile called “{0}”.",  // 263
            "Duplicate the profile",  // 264
            "New profile",  // 265
            "{0} (copy)",  // 266
            "Made the profile “{0}”, a copy of the last one.",  // 267
            "Made the profile “{0}”: turn mods on for it.",  // 268
            "Add a mod",  // 269
            "Mod downloads (*.zip;*.7z;*.rar;*.bnp)|*.zip;*.7z;*.rar;*.bnp|All files (*.*)|*.*",  // 270
            "Choose the mod's folder (the one with romfs in it, or above it)",  // 271
            "{0} of {1}: ",  // 272
            "Adding {0}...",  // 273
            "Nothing was installed.",  // 274
            "Press Play to use them.",  // 275
            "{0} couldn't be installed.",  // 276
            "Couldn't be installed:",  // 277
            "Installed, but left off (they probably won't work with Savage Wind):",  // 278
            "Skipped (already installed):",  // 279
            "Installing mods",  // 280
            "Choose what to install",  // 281
            "{0} has more than one version of the game files in it. Tick the ones to install: each becomes a mod of its own (most of the time you want one).",  // 282
            "Pick the newer file of {0} and install it: Guardian Terminal asks to replace the old one.",  // 283
            "Uninstall {0}?",  // 284
            "It's deleted from Guardian Terminal and taken out of every profile. Your original download isn't touched.",  // 285
            "Uninstall mod",  // 286
            "Couldn't delete all of its files ({0}). Close Savage Wind if it's running and try again.",  // 287
            "Uninstalled {0}.",  // 288
            "Close Savage Wind first: it's using the merged files.",  // 289
            "Merging...",  // 290
            "Merge failed.",  // 291
            "Merging the profile “{0}” failed:",  // 292
            "Merge failed",  // 293
            "No mods are on, so the merge output is empty: Play starts the game as it is.",  // 294
            "Merged: {0}.",  // 295
            "Warnings:",  // 296
            "The profile “{0}” is merged.",  // 297
            "Open the merge report?",  // 298
            "Merge done",  // 299
            "Empty merge output",  // 300
            "Merge output emptied. The next Merge or Play builds it again.",  // 301
            "Couldn't empty the merge output: {0}",  // 302
            "No mods are on in this profile: nothing to export.",  // 303
            "Export to SD card",  // 304
            "More than one drive has an atmosphere folder. Which one is your Switch's SD card?",  // 305
            "Choose your Switch's SD card (plug it in with hekate's USB mode or a card reader). No drive with an atmosphere folder was found.",  // 306
            "Export the profile “{0}” to {1} for your modded Switch?",  // 307
            "It goes to {0}.",  // 308
            "The Breath of the Wild mods already there are deleted first:",  // 309
            "The merge is made for your game files ({0}): the Switch needs the same versions.",  // 310
            "Exporting to the SD card...",  // 311
            "Export failed.",  // 312
            "Exporting to the SD card failed:",  // 313
            "Exported to {0}: {1}.",  // 314
            "Safely eject the card before you put it back in the Switch.",  // 315
            "Savage Wind is already running. Close it first.",  // 316
            "Getting your mods ready...",  // 317
            "Couldn't get the mods ready.",  // 318
            "Merging the mods failed, so the game wasn't started:",  // 319
            "Backing up your saves first...",  // 320
            "Your saves couldn't be backed up ({0}). Play anyway?",  // 321
            "Save backup",  // 322
            "Couldn't start Savage Wind: {0}",  // 323
            "Savage Wind couldn't be started:",  // 324
            "Savage Wind is starting without mods...",  // 325
            "Savage Wind is starting with mods ({0})...",  // 326
            "Savage Wind is running without mods.",  // 327
            "Savage Wind is running with mods ({0}).",  // 328
            "Savage Wind couldn't start the game.",  // 329
            "Savage Wind opened its own setup screen: finish it there, then press Play again.",  // 330
            "Savage Wind closed with an error ({0}). If it happens with mods on, turn them off one at a time to find the one causing it.",  // 331
            "Last game: Savage Wind loaded the mods ({0}).",  // 332
            "Savage Wind closed.",  // 333
            "{0} mod turned on.",  // 334
            "{0} mods turned on.",  // 335
            "{0} mod turned off.",  // 336
            "{0} mods turned off.",  // 337
            "Uninstalled {0} mod.",  // 338
            "Uninstalled {0} mods.",  // 339
            "{0} file",  // 340
            "{0} files",  // 341
            "{0} mod selected",  // 342
            "{0} mods selected",  // 343
            "{0} file changes the game ({1} replaced, {2} new).",  // 344
            "{0} files change the game ({1} replaced, {2} new).",  // 345
            "{0} of them is in the DLC.",  // 346
            "{0} of them are in the DLC.",  // 347
            "{0} file shared with {1} is merged piece by piece: both mods' changes are in.",  // 348
            "{0} files shared with {1} are merged piece by piece: both mods' changes are in.",  // 349
            "{0} file also changed by {1}: this mod's is used.",  // 350
            "{0} files also changed by {1}: this mod's are used.",  // 351
            "{0} file also changed by {1}: the one from {2} is used.",  // 352
            "{0} files also changed by {1}: the ones from {2} are used.",  // 353
            "Starts Savage Wind with the {0} mod that is on (F5)",  // 354
            "Starts Savage Wind with the {0} mods that are on (F5)",  // 355
            "Profile “{1}”: {0} mod on.",  // 356
            "Profile “{1}”: {0} mods on.",  // 357
            "Installed {0} mod.",  // 358
            "Installed {0} mods.",  // 359
            "{0} file is changed by more than one mod in a way that can't be merged: the one lower in the list is used (each mod's details show which).",  // 360
            "{0} files are changed by more than one mod in a way that can't be merged: the one lower in the list is used (each mod's details show which).",  // 361
            "Profiles",  // 362
            "Separate sets of mods: each profile has its own mods on and off, and its own load order. The mods themselves are shared.",  // 363
            "Switch to it",  // 364
            "New",  // 365
            "Duplicate",  // 366
            "Rename",  // 367
            "Delete",  // 368
            "Import...",  // 369
            "Share (export)...",  // 370
            "No mods on",  // 371
            "current",  // 372
            "Rename the profile",  // 373
            "Renamed “{0}” to “{1}”.",  // 374
            "At least one profile must always exist.",  // 375
            "Delete profile",  // 376
            "Delete the profile “{0}” (the current one)?",  // 377
            "Only the profile goes (which mods are on, and their order): the mods stay installed.",  // 378
            "Deleted the profile “{0}”.",  // 379
            "Share the profile “{0}”",  // 380
            "Guardian Terminal profile",  // 381
            "Saved the profile as {0}. It lists the mods and where to get them (no mod files are in it).",  // 382
            "Couldn't save the profile: {0}",  // 383
            "Import a profile",  // 384
            "All files",  // 385
            "Couldn't import the profile: {0}",  // 386
            "Imported the profile “{0}”: {1} of its mods are installed here.",  // 387
            "The profile “{0}” uses mods that aren't installed here:",  // 388
            "Install them (GameBanana page, Add mod), then import the profile again to put them in its order.",  // 389
            "Open their GameBanana pages now?",  // 390
            "Saves",  // 391
            "Copies of your Savage Wind saves, so a mod (or a randomizer) can never cost you your progress. The newest 15 are kept.",  // 392
            "Back up now",  // 393
            "Restore",  // 394
            "Backups folder",  // 395
            "Savage Wind's save folder",  // 396
            "Savage Wind has no save folder yet.",  // 397
            "Back up my saves by themselves before I play with a different set of mods",  // 398
            "Savage Wind isn't set up yet.",  // 399
            "Savage Wind's saves: {0}     Backups: {1}",  // 400
            "No backups yet",  // 401
            "Press Back up now, or play: one is made before a game with a different set of mods.",  // 402
            "before playing unmodded",  // 403
            "by hand",  // 404
            "before restoring {0}",  // 405
            "Savage Wind has no saves yet.",  // 406
            "Backing up your saves...",  // 407
            "Couldn't back up the saves: {0}",  // 408
            "Your saves are backed up.",  // 409
            "Restore saves",  // 410
            "Put back the saves from {0} ({1})?",  // 411
            "The saves there now are backed up first, so nothing is lost.",  // 412
            "Restoring your saves...",  // 413
            "Couldn't restore the saves: {0}",  // 414
            "Your saves from {0} are back.",  // 415
            "Delete the backup from {0}? It can't be undone.",  // 416
            "Delete backup",  // 417
            "Couldn't delete it: {0}",  // 418
            "Tools",  // 419
            "Merge now",  // 420
            "Merges the mods that are on into the files Savage Wind loads. Play does it by itself when something changed; this is to check a set of mods without starting the game.",  // 421
            "Merge report",  // 422
            "What the last merge did: the files merged piece by piece, the ones only one mod could win, resource sizes, warnings.",  // 423
            "Open",  // 424
            "There's no merge report yet: press Merge first.",  // 425
            "Deletes the merged files (your mods stay). The next Play or Merge makes them again.",  // 426
            "Empty",  // 427
            "The same merge for a modded Switch (Atmosphere): written to its SD card, for the same game version.",  // 428
            "Export...",  // 429
            "Check for updates",  // 430
            "Guardian Terminal, Savage Wind and your mods from GameBanana. Nothing is installed by itself.",  // 431
            "Check now",  // 432
            "Desktop shortcut",  // 433
            "A “Savage Wind (with mods)” shortcut on your desktop: it merges if needed and starts the game with the current profile's mods.",  // 434
            "Make it",  // 435
            "Folders",  // 436
            "Where Guardian Terminal keeps things: installed mods, the merged files Savage Wind loads, logs.",  // 437
            "Mods",  // 438
            "Merged files",  // 439
            "The merge output is empty: press Merge first.",  // 440
            "Logs",  // 441
            "Clear temporary files",  // 442
            "Downloaded mod archives and unpacking leftovers. Your installed mods stay.",  // 443
            "Clear...",  // 444
            "Start Savage Wind with your mods, from Guardian Terminal",  // 445
            "Made a “Savage Wind (with mods)” shortcut on your desktop.",  // 446
            "Couldn't make the shortcut: {0}",  // 447
            "There are no temporary files.",  // 448
            "Delete {0} of temporary files?",  // 449
            "That's the downloaded mod archives (downloads\\) and unpacking leftovers. Your installed mods stay; you'd need to download a mod again to reinstall it.",  // 450
            "Cleared {0} of temporary files.",  // 451
            "Couldn't clear everything: {0}",  // 452
            "Settings",  // 453
            "Install or update...",  // 454
            "Find it...",  // 455
            "Its log",  // 456
            "Savage Wind hasn't written a log yet.",  // 457
            "The Master Trials and The Champions' Ballad, set up in Savage Wind. Switching it off renames Savage Wind's DLC folders (nothing is deleted); mods' DLC parts are left out while it's off.",  // 458
            "Once a day: Guardian Terminal (its GitHub page), Savage Wind (its GitHub releases) and your mods from GameBanana. You're only told: nothing is installed by itself.",  // 459
            "Mod pictures",  // 460
            "Show the mods as cards with their pictures (from GameBanana), or as compact rows.",  // 461
            "Language",  // 462
            "The language Guardian Terminal speaks. It changes when Guardian Terminal starts again.",  // 463
            "Automatic (Windows' language)",  // 464
            "Mature mods on GameBanana",  // 465
            "Show the mods GameBanana marks as mature (18+) on the GameBanana page. They're hidden while this is off.",  // 466
            "Guardian Terminal's folder",  // 467
            "Your mods, profiles, merged files, save backups and settings are all kept here. Move the whole folder to move Guardian Terminal.",  // 468
            "Guardian Terminal will speak {0} after it starts again. Restart it now?",  // 469
            "(version {0})",  // 470
            "Game files {0}  ·  {1}  ·  saves in {2}",  // 471
            "no DLC set up",  // 472
            "DLC switched off",  // 473
            "Its game files aren't set up yet: press Start its setup, pick your own 1.9.0 game files there, then come back (Guardian Terminal checks again when you open it).",  // 474
            "Not found. Install it (Guardian Terminal downloads the newest release from Savage Wind's GitHub page), or find savagewind.exe if you have it.",  // 475
            "Savage Wind is set up. Ready to play your mods.",  // 476
            "Find savagewind.exe",  // 477
            "Programs",  // 478
            "Using Savage Wind in {0}.",  // 479
            "Install Savage Wind first, or find it.",  // 480
            "Savage Wind is open: set up your game files there (your own 1.9.0 dump). Come back here when it's done.",  // 481
            "Looking up Savage Wind's newest release...",  // 482
            "Couldn't reach Savage Wind's GitHub page: {0}",  // 483
            "No Windows release of Savage Wind was found on its GitHub page.",  // 484
            "Savage Wind {0} (a pre-release), from Savage Wind's own GitHub page ({1}): {2}.",  // 485
            "Savage Wind {0}, from Savage Wind's own GitHub page ({1}): {2}.",  // 486
            "You have {0}.",  // 487
            "It goes in a new folder of its own. Then start it once: its setup asks for your own game files (version 1.9.0) and builds the game code (about 15 minutes, once).",  // 488
            "Choose where to put it (not Program Files: it keeps your game files, saves and settings in its folder).",  // 489
            "Install Savage Wind",  // 490
            "Where to put Savage Wind {0} (a folder named after it is made inside)",  // 491
            "Program Files isn't a good place: Savage Wind needs to write its game files and saves in its folder. Choose another folder.",  // 492
            "Downloading Savage Wind {0}...",  // 493
            "Couldn't install Savage Wind: {0}",  // 494
            "Savage Wind {0} is in {1}.",  // 495
            "Copy your saves from the Savage Wind you have ({0}) to the new one?",  // 496
            "(They're copied: the old ones stay.)",  // 497
            "Couldn't copy the saves: {0}",  // 498
            "Savage Wind {0} is installed. Start it once to set up your game files, then come back here.",  // 499
            "Version {0}  ·  a mod manager for Savage Wind",  // 500
            "Guardian Terminal on GitHub",  // 501
            "Savage Wind on GitHub",  // 502
            "Show the welcome screen",  // 503
            "Credits",  // 504
            "{0} is made by {1}, its owner.",  // 505
            "{0} is the PC port of Breath of the Wild that Guardian Terminal plays your mods with. It's made by {1}, and built on LAS Recomp by peiikai. All the credit for running the game goes to them: report the game's bugs on Savage Wind's page, and support it.",  // 506
            "Revivedx and the Savage Wind Recomp contributors",  // 507
            "Thanks to the mod makers, and to BCML and TKMM, whose ideas (merging, profiles, Merge) this follows.",  // 508
            "Fan-made and free, under the MIT License. Not affiliated with Nintendo, the Savage Wind project, BCML or TKMM. You need your own copy of the game: Guardian Terminal never includes or downloads any game files.",  // 509
            "{0} mod on: {1}",  // 510
            "{0} mods on: {1}",  // 511
            "before playing “{1}” ({0} mod)",  // 512
            "before playing “{1}” ({0} mods)",  // 513
            "Savage Wind wasn't found.",  // 514
            "Open Guardian Terminal to set it up.",  // 515
            "Savage Wind is already running.",  // 516
            "Getting the mods ready failed:",  // 517
            "Open Guardian Terminal for details.",  // 518
            "Starting Savage Wind...",  // 519
            "Welcome to Guardian Terminal",  // 520
            "Breath of the Wild Switch mods, merged and played in Savage Wind.",  // 521
            "{0} is made by {1}.",  // 522
            "It plays your mods in {0}, the Breath of the Wild PC port made by {1} (built on LAS Recomp by peiikai). They made running the game possible; Guardian Terminal takes care of your mods. Fan-made and free: not affiliated with Nintendo or the Savage Wind project.",  // 523
            "How it works",  // 524
            "Savage Wind, with your own game files",  // 525
            "Guardian Terminal finds it, or installs the newest release from its GitHub page (Settings). Its setup asks for your own 1.9.0 game files.",  // 526
            "Add mods",  // 527
            "From the GameBanana page, or drop a mod's .zip / .7z / .rar / .bnp on the window. Switch mods (with a romfs folder).",  // 528
            "Press Play",  // 529
            "The mods that are on are merged into a copy for Savage Wind: your game files are never changed, and your saves are backed up first.",  // 530
            "Check for updates once a day",  // 531
            "Get started",  // 532
            "sound links",  // 533
            "effect links",  // 534
            "HUD layout",  // 535
            "About",  // 536
        };
    }
}
