# Arcane Wars special-site minimap marker

`AWTechMiniMapIcon.png` in Arcane Wars 0.82.1.8 is a fully transparent 7x7
texture (SHA256 `9A9A9AC990CCEA88D994A4CA1DE0FEF4B3323B481CF42D43EA86B35F64C12169`).
Eight special-site definitions, including `AW_shadow_sepulcher`, reference it.
Consequently a captured site can remain invisible even though its owner and
world model are correct. This asset fault is not specific to an undead owner.

The replacement is an unchanged copy of the mod's existing 11x11
`MarkerLairTechMapIcon.png`, SHA256
`98C205EC66B3C2A1E102BF3E845368732EAE7E32410276E61150B0016013DCED`.
It keeps the game's normal owner tint and visibility rules. No building,
economy, ownership or simulation definition is changed.

Ship this resource only in Arcane Wars `common-ui`, above the base mod in
installer order. Do not put it in the shared `release-assets` directory: the
Vanilla/Immortals builder consumes that directory too. The existing installer
ownership/backup mechanism restores the base-mod texture when the overlay is
removed. `tools/PrepareArcaneMinimap.py` builds a local package candidate from
an existing verified common-ui package without changing catalogs or publishing.

This is an existing game/mod resource, not a newly licensed launcher graphic.
