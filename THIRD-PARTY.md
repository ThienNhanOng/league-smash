# Third-party components shipped with Slap Rift

| Component | Author | Licence | Where in the release |
|---|---|---|---|
| BepInEx 5.4.23.5 (with HarmonyX, MonoMod, Mono.Cecil, Unity Doorstop) | BepInEx team (Bepis et al.) | MIT (licence in `BepInEx/SlapRift/licenses/BepInEx-LICENSE.txt`) | `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx/core/` |
| Zstandard 1.5.7 (`libzstd.dll`) | Meta Platforms, Inc. | BSD (licence in `BepInEx/SlapRift/licenses/zstd-LICENSE.txt`) | `BepInEx/SlapRift/libzstd.dll` |
| vgmstream r2117 (with its bundled libvorbis, mpg123, FFmpeg, etc.) | vgmstream contributors | ISC-style, see `BepInEx/SlapRift/vgmstream/COPYING`; bundled libraries keep their own licences | `BepInEx/SlapRift/vgmstream/` |

Not shipped: anything from Slap City or League of Legends. Champion models, animations, textures, art and
audio are read from the player's own League of Legends install while the game runs, and the decoded sounds stay
in the player's local cache.

League of Legends © Riot Games. Slap City © Ludosity. Slap Rift is an unofficial fan project, not endorsed by either.
