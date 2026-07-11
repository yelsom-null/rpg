# Third-party assets

The models and texture atlases under `Assets/Elderholt/Resources/KayKit/` are
from **KayKit** game asset packs by **Kay Lousberg** (https://kaylousberg.com),
licensed **CC0 1.0 Universal** (public domain — no attribution required, given
anyway because they're great):

- **KayKit — Medieval Hexagon Pack** (`Resources/KayKit/Nature/`)
  trees, boulders + `hexagons_medieval.png` atlas
  https://github.com/KayKit-Game-Assets/KayKit-Medieval-Hexagon-Pack
- **KayKit — Dungeon Remastered** (`Resources/KayKit/Props/`)
  barrels, crates, chest, keg, coin stack, torch, table, pillar + `dungeon_texture.png` atlas
  https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered
- **KayKit — Resource Bits 1.0 FREE** (`Resources/KayKit/Resource/`)
  metal nuggets & bar stacks, stone chunks/bricks, wood logs/planks, textiles
  + `resource_bits_texture.png` atlas (from the pack's Unity-ready FBX set)
  https://kaylousberg.itch.io/resource-bits

Full license texts: `LICENSE-KayKit-Hexagon.txt`, `LICENSE-KayKit-Dungeon.txt`
in this folder.

All loads go through `ThornmereWorld.TryModel(...)`, which falls back to the
procedural placeholder geometry if a model is missing — the pack is an optional
visual layer, not a dependency.
