# Slap Rift

Six League of Legends champions join Slap City's roster: Darius, Gangplank, Yasuo, Yone, Jinx and Fiora,
with their real models, animations, voice lines and ability sounds read from your own League of Legends install.

## What you get

- A new row of six champion slots on Slap City's character select screen, with each champion's League icon,
  and their League loading-screen art in your player box and the match HUD.
- Each champion plays on a Slap City fighter's moveset and hitboxes; its four specials are its League abilities
  with their League mechanics, and its League passive is always on:
  - **Darius** (Business Casual Man): passive Hemorrhage, hits stack a bleed and 5 stacks give Noxian Might
    (+30% damage). Q Decimate hits harder and heals at the blade's edge, W Crippling Strike slows,
    E Apprehend pulls and slows, R Noxian Guillotine deals more per bleed stack and executes at 100%+.
  - **Gangplank** (Masked Ruby): passive Trial by Fire, his first hit after 5 s burns and speeds him up.
    Q Parrrley can crit, W Remove Scurvy cleanses and heals, E Powder Keg blasts and slows, R Cannon Barrage slows.
  - **Yasuo** (Ultra Fishbunjin 3000): passive Flow, moving builds a shield that halves the next hit.
    Q Steel Tempest knocks up on every third cast, W Wind Wall blocks projectiles, E Sweeping Blade dashes and
    stacks damage, R Last Breath hits airborne targets harder and holds them in the air.
  - **Yone** (Goddess of Explosions): passive Way of the Hunter, every second hit is stronger.
    Q Mortal Steel knocks up on every third cast, W Spirit Cleave shields, E Soul Unbound dashes and repeats
    part of its damage 3 s later, R Fate Sealed pulls and stuns.
  - **Jinx** (Princess Remedy): passive Get Excited!, she speeds up when someone she hit is knocked out.
    Q Switcheroo! blasts, W Zap! slows, E Flame Chompers! roots, R Super Mega Death Rocket! hits harder the
    higher the target's damage.
  - **Fiora** (Ittle Dew): passive Duelist's Dance, every 4th hit on a target strikes a vital and heals.
    Q Lunge dashes, W Riposte parries the next hit and stuns the attacker, E Bladework slows and empowers her next
    two hits, R Grand Challenge raises her damage and makes every hit heal for 6 s.
- Every special plays the champion's League animation, ability sound and voice line. No cooldowns or mana:
  it stays a fast platform fighter.
- Local multiplayer: up to 4 players on one PC (controllers or keyboard), any mix of champions, Slap City
  fighters and CPUs, in every local mode. Champions are turned off in online play so online matches stay fair.

## What you need

- Slap City (Steam) and League of Legends installed. League only has to be installed; it is never started or
  modified, and nothing from it is copied into this mod: the champions are read from your own install each time
  you play. The first start decodes the champions' sounds into `%LOCALAPPDATA%\SlapRift\cache` (about 30 seconds,
  once).

## How it works (for modders)

- BepInEx 5 plugin (`src/`), Harmony patches into Slap City's roster, skins, HUD and combat code.
- Champions are hidden skins (`lol_<name>`) of their base fighters, picked from cloned roster slots.
- League files are read in place from `Game/DATA/FINAL/Champions/*.wad.client` (WAD v3, zstd, sub-chunks):
  `.skn`/`.skl` meshes and skeletons, `.anm` animations (uncompressed v4/v5 and compressed v1), `.tex` textures,
  Wwise banks and `.wpk` voice packs (decoded with vgmstream).
- The design lives in `sheets/*.json`; `tools/gen.py` turns them into `src/Generated/Sheets.g.cs` and
  `tools/preflight.py` checks every cell, every cross-sheet reference, every League file and sound event,
  and every hook before a build.
- The League install is found from `%LOCALAPPDATA%\SlapRift\settings.ini` (`league=`, written by Melty),
  the `SLAPRIFT_LEAGUE` variable, the Riot Client's install record, or the default folders.

Built with AI assistance (Claude) using the universal-modder toolkit. See THIRD-PARTY.md for credits.
League of Legends and its champions are Riot Games' property; Slap City is Ludosity's. This is an unofficial fan mod.
