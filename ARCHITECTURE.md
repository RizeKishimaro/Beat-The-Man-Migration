# Beat the Man — Architecture

Core architecture — classify every new asset/system as one of these five before building.

Source of truth: Trello "🧬 ARCHITECTURE" card — https://trello.com/c/flVvpCb2/52-%F0%9F%A7%AC-architecture-object-character-building-node
This file should always mirror that card. If they ever disagree, Trello wins — update this file to match.

Last synced: 2026-08-05

Each type has a MASTER layer (shared Prefab/atom) + INDEPENDENT layer (per-instance overrides).

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

## Before building
Ask: "Object, Character, Building, Node, or Vehicle?" Then build as a Prefab in the matching folder (Items/, Characters/, Environment/, Vehicles/).
