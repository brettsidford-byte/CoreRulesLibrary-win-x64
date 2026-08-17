# Item database format

Core Rules stores character-builder parts in `Database/Parts.dat` as MFC `CArchive`
object arrays. The supplied file uses runtime class `CPart`, schema 93. It contains
both physical items and unrelated builder data, so treating every record as an item
produces an incorrect catalogue.

The read-only item viewer exposes these collections:

| First record | Count | Viewer category |
| ---: | ---: | --- |
| 1 | 733 | Weapons |
| 734 | 573 | Armour |
| 1,657 | 306 | Equipment |
| 1,963 | 689 | Magical items |
| 2,740 | 1,383 | Treasure and materials |

Collections between these ranges contain proficiencies, racial abilities, languages,
schools and other non-item records and are intentionally skipped.

`ItemDatabaseParser` validates the initial array, runtime class, schema, expected
collection counts and record boundaries. It opens `Parts.dat` with read-only access.
The viewer decodes user-facing costs, XP, weight, capacity, armour class and weapon
statistics. Internal character-builder flags are deliberately omitted. Damage type
codes are expanded to Slashing, Bludgeoning and Piercing, including combined codes
such as `P/S`. Imported records with an embedded custom-help string display that
description. For built-in records, the viewer routes ordinary weapons, armour and
equipment through `Help/Equip.hlp`, and magical items and treasure through
`Help/Magic.hlp`, with the other catalogue as a compatibility fallback. Converted
topics are kept in a local cache and displayed inside the item panel; the application
does not contain or distribute the Core Rules help prose.
