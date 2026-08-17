# Changelog

## 0.1.1 - 2026-08-17

- Improved close-range aiming with a muzzle-origin raycast, limited center-screen correction, and protection against backward shots around nearby obstacles.
- Added per-player `ShotUseTime`, including instant semi-automatic firing when set to `0`.
- Added per-player `AimCorrectionDegrees`, with `0` disabling aim correction.
- Added the host-synchronized `AddToLootPool` option, enabled by default.
- Updated the package icon and expanded the installation and configuration documentation.

## 0.1.0 - 2026-08-17

- Initial public release.
- Added host-synchronized configurable Arrow Blowgun uses, defaulting to three shots.
- Shows the inventory durability bar when the Arrow Blowgun has multiple uses.
- Added a loaded arrow visual at the blowgun muzzle.
