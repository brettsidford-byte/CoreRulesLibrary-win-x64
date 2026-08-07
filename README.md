# Core Rules Library

Core Rules Library is a focused Windows viewer for two kinds of HTML content:

- the WebHelp books supplied with **Advanced Dungeons & Dragons: CD-ROM Core Rules 2.0 Expansion**;
- character sheets exported to HTML by Core Rules.

It does not read or modify Core Rules databases or character `.dat` records. Character sheets are displayed exactly from their exported HTML files.

## Features

- separate Characters, grouped Books and levelled Spells navigation;
- automatic discovery of the installed Core Rules WebHelp library;
- selectable character-sheet folder with recursive HTML discovery;
- title extraction from each character sheet;
- filtering by document title;
- read-only browsing of original and user-added spells by caster type and level;
- Back, Forward, Start Page and Open in Browser controls;
- 100–200% display scaling;
- privately loaded ITC Korinna font applied to books and character sheets;
- read-only operation and remembered folders;
- self-contained 64-bit Windows build.

## Using the application

1. Select the Core Rules installation folder containing `WebHelp`.
2. Select a folder containing exported `.htm` or `.html` character sheets.
3. Choose a book or character from the left panel.

The selected folders can be changed at any time. To refresh a modified character sheet, select it again or press **Start page**.

## Obtaining a build

Open the repository's **Actions** page, select the latest successful **Windows build**, and download the `CoreRulesLibrary-win-x64` artefact. The application is `CoreRulesLibrary.exe`.

## Original content

This repository does not currently contain the original rulebooks, artwork, help files or personal character sheets. Users must own and select their own content. A later release may support a separately packaged local book library.

This independent project is not endorsed by or affiliated with Wizards of the Coast, Hasbro, TSR or the Drakein project.
