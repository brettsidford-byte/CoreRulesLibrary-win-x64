# Spell database format

Core Rules stores spell records in two MFC `CArchive` object arrays:

- `Database/Spells.dat` contains the original spell catalogue;
- `UserDbas/SpellsU.dat` contains imported and user-created spells.

Both supplied files use runtime class `CSpellsOb`, schema 88. The core file contains
two arrays (480 wizard records and 453 priest records). The supplied user file contains
2,573 records followed by an empty-array terminator.

Each object contains, in order:

1. spell name;
2. Never Ban Cantrip, Reversible, Wizard Spell and Priest Spell Boolean values;
3. level;
4. area of effect, casting time, components, critical, duration, knockdown, range,
   saving throw, sensory and subtlety strings;
5. numeric help-topic identifier;
6. description;
7. schools and spheres arrays.

Strings use the variable-length ANSI `CString` representation and Windows-1252 text.
The original database normally stores an empty description and a help-topic identifier;
user records store their description directly in `SpellsU.dat`.

`SpellDatabaseParser` opens files with read-only access, validates object tags, schema,
counts, Boolean fields, string sizes and record boundaries, and reports malformed data
with the byte offset at which parsing failed.
