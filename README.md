# ArrowBlowgun

ArrowBlowgun adds a networked blowgun variant that applies PEAK's arrow injury instead of the vanilla dart afflictions.

The item is cloned from the current vanilla blowgun at runtime, so it keeps the original model, icon, hold pose, and loot configuration. It has three uses and the vanilla one-second use time by default, and its shot feedback matches the arrow trap.

[Thunderstore package page](https://thunderstore.io/c/peak/p/jie65535/ArrowBlowgun/)

![Arrow Blowgun in game](Image.png)

## Requirements

- BepInExPack for PEAK

All players in a multiplayer lobby must have the mod installed.

## Installation

- Install `ArrowBlowgun` with a Thunderstore-compatible mod manager; or
- Install BepInExPack for PEAK and place `com.github.jie65535.ArrowBlowgun.dll` in `BepInEx/plugins`.

Use the same mod version on every multiplayer client.

## Behavior

- Registers a separate `Arrow Blowgun` item without changing the vanilla blowgun.
- Registers directly with PEAK's item database and current Photon prefab pool without third-party libraries.
- Fires an immediate 80-meter raycast from the actual blowgun muzzle, correcting toward the center-screen aim point by at most 35 degrees by default.
- Keeps the muzzle direction when the aim point lies behind it, preventing backward shots around close obstacles.
- Uses the arrow trap's four-layer shot sound, muzzle puff, and smoke trail.
- Displays a loaded vanilla arrow at the blowgun muzzle to distinguish it from the dart variant.
- Embeds arrows in terrain until the current scene is unloaded, matching the vanilla trap behavior.
- Applies the built-in arrow injury and a small knockback when a character is hit.
- Synchronizes shot feedback, wall arrows, arrow injury, knockback, and item uses through Photon.
- Uses three shots by default instead of the vanilla blowgun's single shot.
- Displays the remaining uses in the inventory durability bar when configured above one use.
- Supports per-player shot use time and aim correction settings.
- Can be removed from random loot pools by the room creator without disabling the item.
- Inherits the vanilla blowgun's loot pools and rarity.

## Configuration

BepInEx creates `BepInEx/config/com.github.jie65535.ArrowBlowgun.cfg` after the mod is loaded once.

| Setting | Default | Range | Scope | Description |
| --- | ---: | ---: | --- | --- |
| `[Balance] Uses` | `3` | `1+` | Room | Shots before the Arrow Blowgun is consumed. |
| `[Balance] ShotUseTime` | `1` | `0-10` | Local player | Seconds the primary action must be held before firing. `0` fires immediately. |
| `[Balance] AimCorrectionDegrees` | `35` | `0-90` | Local player | Maximum angle the shot may turn from the muzzle toward the center-screen aim point. `0` disables correction. |
| `[Spawning] AddToLootPool` | `true` | `true`/`false` | Room | Includes the item in the vanilla blowgun's random loot pools. |

Example configuration for instant, gun-like firing while retaining the default aim correction:

```ini
[Balance]
Uses = 3
ShotUseTime = 0
AimCorrectionDegrees = 35

[Spawning]
AddToLootPool = true
```

`ShotUseTime = 0` produces an instant semi-automatic shot. Holding the button still fires only once; automatic fire is not implemented.

Restart the game after changing a value. `ShotUseTime` and `AimCorrectionDegrees` are applied from each player's local configuration. In multiplayer, the room creator's `Uses` and `AddToLootPool` values are synchronized to every player and become room rules; guests' values for those settings are restored after leaving the room.

Disabling `AddToLootPool` only removes the Arrow Blowgun from random loot selection. The item remains registered and can still be created by other mods or development tools.

## Development

Build from the repository root:

```powershell
dotnet build .\ArrowBlowgun.slnx -c Debug
dotnet build .\ArrowBlowgun.slnx -c Release
```

The project targets PEAK `2.1.a` build `0657e527f`.

The MIT license applies to the source code. Embedded PEAK audio remains the property of its respective owners.
