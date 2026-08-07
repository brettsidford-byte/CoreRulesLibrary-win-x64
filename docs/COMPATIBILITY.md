# Compatibility policy

## Supported documents

- Core Rules 2.0 Expansion WebHelp pages and their relative hyperlinks.
- Character sheets exported by Core Rules as `.htm` or `.html`.
- Images and other resources referenced through relative paths beside those documents.

## Library layout

The selected book-library folder must contain `WebHelp/`. Character sheets may be kept in any separate folder; its subfolders are searched recursively.

## Safety

The viewer does not parse or modify `Chars/*.dat`, `index.chr`, databases, HTML files or help resources. It stores only the selected folder paths and display scale in the user's local application settings.

## HTML engine

The current test build uses the Windows HTML browser control for compatibility with the original HTML 3.2 documents. Styling is injected only in memory to apply ITC Korinna and the selected scale; source files remain unchanged.
