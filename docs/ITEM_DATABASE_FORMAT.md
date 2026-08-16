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
The current viewer displays the item name and recoverable textual values. Numeric
`CPart` fields remain deliberately unlabelled until their meanings can be verified.
