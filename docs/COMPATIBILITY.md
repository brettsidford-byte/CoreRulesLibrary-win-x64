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
`WebHelp/Ravenloft/`. Each book must have its own folder containing one of the supported
start-page names. Nested extracted website folders are searched recursively; when more
than one start page exists, the nearest one to the book folder is used. Supporting
images, stylesheets and chapter pages may use any layout referenced by the book's HTML.

For a Van Richten collection, `van_richtens_cover.png` is used while the collection
landing page is active. Individual guide pages use `vr01_cover.png` through
`vr09_cover.png`, matching the `vr01_` through `vr09_` HTML prefixes. If a supplied
PNG is absent, the viewer falls back to the guide's existing `_00` HTML cover page.

## Safety

The viewer does not parse or modify `Chars/*.dat`, `index.chr`, databases, HTML files or help resources. It stores only the selected folder paths and display scale in the user's local application settings.

## HTML engine

Original AD&D 2nd Edition WebHelp books use the Windows WebBrowser (WebView1) engine
for compatibility with their legacy HTML and navigation. Ravenloft books, character
sheets, spell descriptions and online resources use Microsoft WebView2. Styling is
injected only in memory; source files remain unchanged.

## Optional local fonts

The application privately loads every `.otf` and `.ttf` file placed in `Assets/Fonts`
beside the executable. AD&D 2nd Edition books use Book Antiqua body text with
University Roman Std headings. Ravenloft books use ITC Korinna body text with Honda
headings. Missing families fall back to commonly available serif fonts; the HTML source
files are never modified.

Legacy pages that explicitly specify `<FONT FACE="Friz Quadrata Bold">` or
`<FONT FACE="Friz Quadrata">` use the corresponding packaged bold or regular Friz
asset. `<FONT FACE="quadrat-serial-xbold">` uses the packaged Quadrat Serial XBold
asset. Matching is case-insensitive. Font size alone does not trigger substitution.
