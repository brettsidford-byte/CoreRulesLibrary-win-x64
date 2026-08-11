# Compatibility policy

## Supported documents

- Core Rules 2.0 Expansion WebHelp pages and their relative hyperlinks.
- Character sheets exported by Core Rules as `.htm` or `.html`.
- Images and other resources referenced through relative paths beside those documents.

## Library layout

The selected book-library folder must contain `WebHelp/`. Character sheets may be kept in any separate folder; its subfolders are searched recursively.

`Domains of Dread` may be added beneath `WebHelp/` in a folder whose name contains
`Domains of Dread` (spaces, hyphens and additional suffixes are accepted). Its start
page may be `index.html`, `index.htm`, `default.html` or `default.htm`.

Additional Ravenloft books are discovered as immediate subfolders of
`WebHelp/Ravenloft/`. Each book must have its own folder with one of the supported
start-page names directly inside it. Supporting images, stylesheets and chapter pages
may use any subfolder layout referenced by the book's HTML.

## Safety

The viewer does not parse or modify `Chars/*.dat`, `index.chr`, databases, HTML files or help resources. It stores only the selected folder paths and display scale in the user's local application settings.

## HTML engine

All books, character sheets, spell descriptions and online resources use Microsoft
WebView2. Styling is injected only in memory to apply the optional packaged fonts;
scaling uses WebView2's native zoom and source files remain unchanged.

## Optional local fonts

The application privately loads every `.otf` and `.ttf` file placed in `Assets/Fonts`
beside the executable. AD&D 2nd Edition books use Book Antiqua body text with
University Roman Std headings. Ravenloft books use ITC Korinna body text with Honda
headings. Missing families fall back to commonly available serif fonts; the HTML source
files are never modified.
