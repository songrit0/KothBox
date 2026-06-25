# KothBox — King of the Hill dome PVP event

RocketMod plugin for Unturned U3DS. Admins place dome zones; players fight inside a
two-stage event (warmup -> active); the winner is whoever accumulates the most time
inside the dome (or first to reach the winning time). Rewards pay out in the server's
`sv_coins` currency. Modeled on RestoreMonarchy's KOTH Box, plus arena extras.

## Build
```
powershell -ExecutionPolicy Bypass -File build.ps1   # -> bin\KothBox.dll
```
Requires VS2022 Roslyn csc + Unturned/RocketMod DLLs at the paths in `build.ps1`.

## Deploy
1. Copy `bin\KothBox.dll` to `<server>\Rocket\Plugins\KothBox\KothBox.dll`.
2. Ensure **`MySql.Data.dll`** is in `<server>\Rocket\Libraries\` (the shop/GameMenu plugins already need it).
3. Start once -> edit `KothBox.configuration.xml`:
   - `Database.ConnectionString` = the same DB the shop uses (e.g. `s203_unturned`), `ShopPrefix` = `sv_`.
   - `Loadouts` = the guns/gear players pick from.
   - `RewardTiers` = coins/XP/items per finishing rank.
   - `DomeRingEffectId` = a workshop/effect id for the dome visual (0 = invisible but playable).
4. Restart.

## Commands
| Command | Perm | What |
|---------|------|------|
| `/setkothbox <name> <radius>` | `kothbox.admin` | Create/move a dome at your position |
| `/deletekothbox <name>` | `kothbox.admin` | Delete a dome |
| `/kothboxes` | `kothbox.admin` | List domes |
| `/previewkothbox <name>` | `kothbox.admin` | Render the dome ring once |
| `/startkoth <name>` | `kothbox.admin` | Start an event |
| `/stopkoth` | `kothbox.admin` | Stop + restore everyone |
| `/reloadkothboxes` | `kothbox.admin` | Reload dome data |
| `/jkoth [loadoutIndex]` | — | Join during warmup (stashes inventory, warps in) |
| `/claimkoth` | — | Claim pending rewards |
| `/kothtop [n]` | — | Leaderboard |

## How it works
- **Join** (`/jkoth`): inventory is serialized to `stash/<steamid>.dat` **before** being cleared,
  position saved to `kothstate.xml`, then the player is warped into the dome with the chosen loadout.
- **During**: time inside the dome accrues per player; stepping out deals `DomeDamage` + the dome
  boundary blocks cross-boundary gunfire. Dying inside re-spawns you in the dome with your loadout.
- **End / stop / disconnect**: everyone is warped back to their saved spot and their stash restored.
  If the server crashes mid-event, the next reconnect restores the player automatically.
- **Rewards**: ranked by accrued time; coins credited to `sv_coins` (background thread), items/XP/vehicle
  delivered on `/claimkoth`.

## Persistence files (`Rocket\Plugins\KothBox\`)
`kothboxes.dat` domes · `kothstate.xml` live participants · `stash/*.dat` stashed inventories ·
`pendingrewards.xml` unclaimed rewards · `kothleaderboard.xml` standings.

## UI
- Loadout-pick panel + HUD prefabs are **generated** by `unity/Editor/BuildKothUI.cs` (see `UNITY_GUIDE.md`);
  click handling + HUD push are already wired in the plugin (`KothUI.cs`).
- Set `LoadoutUIEffectId`, `HudEffectId`, `DomeRingEffectId` in config once the bundle is published.
- Until then: `/jkoth <index>` picks a loadout by number, dome shows the ring fallback — fully playable.

## Status / TODO
- Build + publish the `kothui.masterbundle` (loadout panel + HUD) + a dome-shell effect to Steam Workshop.
- `MeowCoins` reward field is unused (no separate currency table confirmed) — only `Coins` pays out.
