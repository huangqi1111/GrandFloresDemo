# NPK frame exports

These PNGs were decoded locally from the DNF `NeoplePack_Bill` image packs. Each pack folder contains individual PNG frames and a `frames.json` file with source frame, block, dimensions, anchor offsets, and original byte offsets.

`NpkSpriteImportSettings.cs` imports the exported textures as point-filtered, uncompressed Unity sprites. The first runtime replacement uses the seven goblin walk frames copied to `Assets/Resources/DNF60/Sprites/Goblin`.

The current extractor supports the 32-bit RGBA and 16-bit ARGB1555 frame formats. Some swordman equipment frames use another encoding and are reported as skipped or failed; those packs need additional format work before they should be used in game.
