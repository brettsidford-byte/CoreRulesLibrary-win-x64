# Core Rules Library

Core Rules Library is a focused Windows viewer for two kinds of HTML content:

- the WebHelp books supplied with **Advanced Dungeons & Dragons: CD-ROM Core Rules 2.0 Expansion**;
- character sheets exported to HTML by Core Rules.

It does not read or modify Core Rules databases or character `.dat` records. Character sheets are displayed exactly from their exported HTML files.

## Features

- Original AD&D 2nd Edition Core Rules books use the legacy Windows WebBrowser
  control (WebView1) for compatibility with their original WebHelp HTML.
- Added Ravenloft books, character sheets, spells and online resources continue
  to use Microsoft Edge WebView2.

- separate Characters, grouped Books and levelled Spells navigation;
- automatic discovery of the installed Core Rules WebHelp library;
- automatic discovery of additional books beneath `WebHelp/Ravenloft`;
- selectable character-sheet folder with recursive HTML discovery;
- title extraction from each character sheet;
- filtering by document title;
- read-only browsing of original and user-added spells by caster type and level;
- embedded access to the Complete Compendium online resource;
- Back, Forward, Start Page and Open in Browser controls;
- 100–300% display scaling through WebView2;
- privately loaded ITC Korinna font applied to books and character sheets;
- collection-specific book typography using optional fonts placed in `Assets/Fonts`;
- read-only operation and remembered folders;
- self-contained 64-bit Windows build.

## Using the application

1. Select the Core Rules installation folder containing `WebHelp`.
2. Select a folder containing exported `.htm` or `.html` character sheets.
3. Choose a book or character from the left panel.

Additional Ravenloft books belong in separate folders beneath `WebHelp/Ravenloft`.
Each book folder must contain `index.htm`, `index.html`, `default.htm` or `default.html`;
the start page may also be inside one additional extracted website folder.
The displayed title comes from the start page's `<title>` element, falling back to the
folder name when necessary.

The selected folders can be changed at any time. To refresh a modified character sheet, select it again or press **Start page**.

## Obtaining a build

Open the repository's **Actions** page and select the latest successful **Windows build**. The `CoreRulesLibrary-v0.9.37` workflow artefact contains two versioned ZIP packages and `SHA256SUMS.txt`:

- `CoreRulesLibrary-win-x64-self-contained-v0.9.37.zip` requires no separate .NET installation.
- `CoreRulesLibrary-win-x64-compact-v0.9.37.zip` uses the installed .NET 8 Desktop Runtime and is substantially smaller.

The compact package requires the [.NET 8 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/download/dotnet/8.0). The application in either package is `CoreRulesLibrary.exe`.
Use `Get-FileHash <zip> -Algorithm SHA256` in PowerShell and compare it with `SHA256SUMS.txt` before extracting a downloaded build.

## Original content

This repository does not currently contain the original rulebooks, artwork, help files or personal character sheets. Users must own and select their own content. A later release may support a separately packaged local book library.

This independent project is not endorsed by or affiliated with Wizards of the Coast, Hasbro, TSR or the Drakein project.
