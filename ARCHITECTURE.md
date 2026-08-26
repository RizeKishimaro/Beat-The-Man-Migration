# Beat the Man — Architecture

Core architecture — classify every new asset/system as one of these five before building.

Source of truth: Trello "🧬 ARCHITECTURE" card — https://trello.com/c/flVvpCb2/52-%F0%9F%A7%AC-architecture-object-character-building-node
This file should always mirror that card. If they ever disagree, Trello wins — update this file to match.

Last synced: 2026-08-26

Each type has a MASTER layer (shared Prefab/atom) + INDEPENDENT layer (per-instance overrides).

## Standing Principle — Library Project (added 2026-08-07)
Beat the Man is not just a standalone game, it is the core reusable engine/library that future games will branch off and depend on. Every system must be fully documented, atomized, and reusable beyond this specific game — no undocumented code, no one-off shortcuts. Each Master layer (per the Object/Character/Building/Node/Vehicle classification) is a permanent library component other projects will inherit via branch. Documentation and reusability are first-class requirements on every ticket.

## OBJECT
Has physics, CAN be picked up/carried.
- Master: physics, pick up/drop
- Independent: shape, interactions
- Ex: Item_Crate_Base

## CHARACTER
Has physics, animated, moves under own control (legs/body).
- Master: physics, animation, general shape
- Independent: clothing, size, colour, hair
- Ex: Character_Base, future NPCs

## BUILDING
Static, defines a space, has attachments (doors/windows).
- Master: door/window attach, walls collapsing on entry
- Independent: shape, colour, door count, lighting
- Ex: CafeRoom

## NODE
Static, NO physics/gravity, CANNOT be picked up, accepts compatible Objects, triggers response.
- Master: accept/reject logic
- Independent: shape, colour, which objects accepted
- Ex: coffee machine, storage rack

## VEHICLE (added 2026-08-03)
Static until entered, then player-driven; wheels/parts animate from input. NOT a Character (no legs/AI movement).
- Master: enter/exit interact, drive input → movement, wheel steer/spin
- Independent: shape, colour, speed/handling, wheel positions
- Ex: Meshy-imported car

## Key distinctions
- Object vs Node: Object is carried + affected by gravity. Node is fixed, no gravity, never carried.
- Character vs Vehicle: Character moves itself. Vehicle is inert until a Character enters and drives it.

## Ties to Atomic structure
Each Master = one reusable Prefab (🧩 Atoms pattern). Independent variations = overrides on instances, not new Prefabs.

## Spatial / Grid (added 2026-08-05)
ALL placement — items, furniture, buildings, stations — snaps to ONE shared universal grid (cell size + coordinate system defined once, reused everywhere). Being built as part of the Carry/drop + grid slot system ticket:
https://trello.com/c/hMZwfwM8/6-carry-drop-grid-slot-system

Building/Node placement must register against this same grid, not a separate one.

## Scale (added 2026-08-15, ticket 135)
ONE real-world scale convention, everywhere: **1 Blender unit = 1 metre = 1 Unity unit.**
Blender scenes stay METRIC with `scale_length = 1.0`; FBX exports use `apply_unit_scale` +
`FBX_SCALE_ALL`; Unity importers stay at `useFileScale = 1`, `globalScale = 1`.

Multi-part assets (the F1 car and its chassis/body/wheels) are exported from ONE master
file at that scale with a shared origin, so parts are inherently sized correctly relative
to each other. **Never resize an individual part** — a part that looks wrong-sized is
almost always the whole asset being wrong-sized.

If an assembled asset needs to be bigger or smaller for gameplay or art-direction reasons,
that is ONE multiplier applied ONCE on the top-level assembled parent/prefab in Unity —
never per-part, never baked into individual meshes. F1 car: `F1_Car` root, **×3**
(whole car 2.88 × 2.15 × 3.82 m). Parts stay at local scale 1 underneath it.

Note the F1 model is a stylised kart, not F1-proportioned — real F1 dimensions are NOT a
valid scale target for it (matching them needs ×1.36 to ×4.60 depending on the axis).

## Before building
Ask: "Object, Character, Building, Node, or Vehicle?" Then build as a Prefab in the matching folder (Items/, Characters/, Environment/, Vehicles/).
