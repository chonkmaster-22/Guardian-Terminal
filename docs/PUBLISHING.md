# Publishing Guardian Terminal on GitHub

A checklist for chonkmaster-22 (the owner): putting the project on GitHub and making its downloads.
Everything here is done in your browser or in GitHub Desktop; nothing is published until you press the buttons.

## 1. Before you start

- [ ] The emblem: put the picture you chose in `assets\emblem.png` (black and white, square works best) and run
      `build.ps1` again. The app icon and the window's emblem are made from it. **Only use a picture you're allowed
      to use** (your own, or one whose author lets anyone use it, with credit if they ask for it): a public
      repository with someone else's art can be taken down. Without the file, the drawn Guardian is used.
- [ ] Check that the folder has no personal files: no `mods`, `overlay`, `save-backups`, `config.json`,
      game files or mod downloads. `.gitignore` keeps these out of the repository, but look anyway.
- [ ] The links in the app and the README point to `github.com/chonkmaster-22/guardian-terminal`. If you name the
      repository differently, change `Repository` and `ProjectPage` in `src\Updates.cs` and the links in
      `README.md`, `docs\README-release.txt` and `docs\release-notes.md`.

## 2. Make the repository

1. Sign in on [github.com](https://github.com) and open [github.com/new](https://github.com/new).
2. **Repository name:** `guardian-terminal`. **Description:** *A mod manager for Savage Wind, the Breath of
   the Wild PC port.* Choose **Public**.
3. Leave "Add a README", ".gitignore" and "license" **unchecked**: the project has its own.
4. Press **Create repository**.

## 3. Upload the project

The easy way is **[GitHub Desktop](https://desktop.github.com)** (free):

1. Install it and sign in with your GitHub account.
2. **File ▸ Add local repository**, choose `Documents\Guardian Terminal`. It says it isn't a repository yet:
   press **create a repository** there, then **Create repository**.
3. In the bottom-left, write a summary like `Guardian Terminal 0.4.0` and press **Commit to main**.
4. Press **Publish repository**, keep the name `guardian-terminal`, **uncheck "Keep this code private"**, and
   publish.

## 4. Make the first release (the download players get)

The repository builds its own releases (`.github/workflows/release.yml`). In GitHub Desktop:

1. **History** tab, right-click the newest commit, **Create Tag...**, name it `v0.4.0`, **Create Tag**.
2. Press **Push origin**: the tag goes up with it.
3. On GitHub, the **Actions** tab shows the build. In about two minutes the **Releases** page has
   *Guardian Terminal v0.4.0* with `GuardianTerminal-v0.4.0-win-x64.zip` and its checksum.

If you'd rather do it by hand: run `package.ps1`, then on GitHub open **Releases ▸ Draft a new release**, make the
tag `v0.4.0`, paste `docs\release-notes.md` as the description, attach the two files from `dist\`, and publish.

## 5. After publishing

- On the repository page, press the gear next to **About**. Add the description, and topics like
  `zelda`, `breath-of-the-wild`, `botw`, `mod-manager`, `savage-wind`.
- Keep **Issues** on (Settings ▸ General ▸ Features): the bug report form is ready.
- Share the link to the **Releases** page, not to the code: players want the zip.

## 6. Later versions

1. Change the version in `src\AssemblyInfo.cs` (all three lines), say what's new at the top of `CHANGELOG.md`.
2. Commit, then make a tag with the new version (`v0.4.1`, ...) and push it, as in step 4.
3. Players with "Check for updates" on are told about it the next day.
