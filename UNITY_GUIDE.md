# KothBox — Unity Workshop bundle guide (Phase 8)

The plugin runs fully without any bundle (dome = stacked effect rings, loadout pick = `/jkoth <index>`).
The **loadout panel + HUD are now generated for you** by `unity/Editor/BuildKothUI.cs` — you only
hand-author the dome shell shader. Build the bundle, publish to Steam Workshop, and put the resulting
EffectAsset ids into `KothBox.configuration.xml` (`LoadoutUIEffectId`, `HudEffectId`, `DomeRingEffectId`).

> Local bundles do **not** sync on a dedicated server — they MUST be published to the Steam Workshop
> and the server subscribed (same lesson as CodeLock2 / KnockdownUI).

## 0. Generate the UI prefabs (loadout panel + HUD) — automated
Copy `unity/Editor/BuildKothUI.cs` into your Unturned-SDK Unity project at `Assets/Editor/`, then run:
- **Unturned KothUI / 1. Generate Loadout Panel** → `Assets/KothUI/Loadout/Effect.prefab`
- **Unturned KothUI / 2. Generate HUD** → `Assets/KothUI/Hud/Effect.prefab`

Both are tagged into `kothui.masterbundle` with the exact element names the plugin drives
(`Loadout_0..5` + `Loadout_N_Name`, `KothClose`, `Koth_PickTitle`, `Koth_Rank/MyTime/Countdown`).
Wrap each prefab in an `EffectAsset` (.dat, unique ID+GUID), Master-Bundle, publish, subscribe the server.
**The click wiring is already done** in the plugin via `EffectManager.onEffectButtonClicked` — no extra code.

## Prereqs
- Unturned + the official **Unturned Editor** (Unturned Dedicated Server tools / Unturned3 Unity 2021/2022 project).
- The same Unity version the server engine reports (currently 2022.3.x is fine; old 2021 bundles still load).

## 1. Dome shell prefab (EFFECT) — automated
Run **Unturned KothUI / 3. Generate Dome (white + green)**. It generates:
- `Koth/Dome` shader (double-sided transparent fresnel — visible inside+outside, no collider/physics),
- white + green materials,
- `DomeWhite/Effect.prefab` + `DomeGreen/Effect.prefab` (built-in Sphere, **no Blender model needed**),

all tagged into `kothui.masterbundle`. Wrap each prefab in an `EffectAsset` (.dat, unique ID+GUID).
Set `DomeWarmupEffectId` (white) + `DomeActiveEffectId` (green) in config.

- **Baked radius**: the sphere is baked at `DomeRadius` (default 50) — set your box to the SAME radius
  (`/setkothbox arena 50`). For another size, change `DomeRadius` in `BuildKothUI.cs` and re-run.
- If the dome renders **pink** in-game, add `Koth/Dome` to *Project Settings > Graphics > Always Included Shaders*
  (master-bundle shader stripping), then rebuild.
- The plugin re-triggers the dome ~1s so the short-lifetime effect looks continuous and late joiners see it.
  Author the `EffectAsset` lifetime ~1.5s.

## 2. Loadout pick UI (EFFECT, UI)
- Canvas with a button per loadout (match `Loadouts` order in config).
- Each button name = `Loadout_0`, `Loadout_1`, … and is **clickable** (the plugin reads clicks via a
  Harmony postfix on `PlayerUI.ReceiveEffectClicked`, same pattern as SortInventory/GameMenu).
- Plugin sends it with `EffectManager.sendUIEffect(LoadoutUIEffectId, key, tc, true)` on `/jkoth`
  (and on death). Wire the button names back to `JoinEvent(player, index)` in `HarmonyPatches.cs`.

## 3. HUD (EFFECT, UI)
- Labels: `Rank`, `MyTime`, `Countdown`. Plugin pushes text with `EffectManager.sendUIEffectText(...)`
  each tick (mirror the Knockdown revive-HUD update pattern).

## 4. Publish
- Master Bundle the assets, publish to the Steam Workshop, subscribe the server to the item.
- Copy each `EffectAsset` ID into the config:
  `DomeRingEffectId`, `LoadoutUIEffectId`, and (when you add it) a `HudEffectId`.

## 5. Wire-up — ALREADY DONE in the plugin
- Clicks: `KothUI.cs OnUIClick` via `EffectManager.onEffectButtonClicked` (`Loadout_N` → join or respawn, `KothClose` → close).
- `/jkoth` with no arg opens the picker when `LoadoutUIEffectId != 0`; `RespawnInDome` shows it on death.
- HUD pushed each tick in `TickActive` (`UpdateHuds`), cleared on event end.
- You only need to: build+publish the bundle and set the three effect ids in config.
