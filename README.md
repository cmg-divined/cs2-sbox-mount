# Counter-Strike 2 mount for s&box

A mount that lets s&box load stuff from your Counter-Strike 2 install: maps, player models, weapons, props, materials, sounds and animations. I made it because I wanted CS2's maps and models in s&box looking the way they do in the game, and so I could mess around with them in the editor and in Sandbox.

It's a mount and nothing more. Nothing from CS2 is in this repo. When s&box asks for a model or a map, the mount reads it straight out of the VPK files in your own Steam install of CS2 and converts it on the spot. So you need to own the game and have it installed, and if you uninstall CS2 the content goes with it.

It never launches CS2, never touches your CS2 install (the game files are only ever opened for reading), and never talks to Valve's servers or your Steam account. Please don't use it to rip assets and reupload them, or to publish s&box packages with CS2 content in them.

It's also not a CS2 remake. There are no rounds, economy, grenades, bots, damage or multiplayer, and I'm not planning to add them. The player controller is there so you have something to walk the maps with and look at the weapons.

If anyone at Valve wants this taken down or changed, open an issue and I'll do it.

## What you can do with it

In the editor or in Sandbox:

- Spawn any of the game's ~39,000 models from the asset browser: agents, weapons, knives, gloves and map props, with their body groups and skins. Characters come with their ragdolls from the game files.
- Use the game's ~20,000 sounds and ~22,000 sound events, including the per-surface footstep and bullet impact sounds.
- Play the game's animation graphs on its models with the `CS2 Animator` component.

Maps:

- Every map in your install shows up (44 in mine), and loads through the `CS2 Map` component or straight from the asset browser. You get the geometry, collision (including player and grenade clips), props, entities, the baked lighting (lightmaps, light probes and reflections), the sun and lights, fog, post processing and the 3D skybox.
- The CS2 shaders are rewritten for s&box, using the game's own compiled shaders as the reference, and run at CS2's high shader quality by default.
- Loaded into another game like Sandbox, a map brings its own CS2 post processing and swaps out the camera's tonemapping and bloom while it's loaded, then puts them back.
- I've tested de_dust2 by far the most. The other maps load but I haven't gone over them as closely.

Playing:

- Drop a `CS2 Map` in a scene and hit play, or add a `CS2 Player` yourself. You spawn on a random team at one of its spawn points as the map's default agent, with an M4A4 or AK-47 and a knife.
- Movement uses CS2's speeds, acceleration and crouch/walk values. Recoil follows each gun's real spray pattern. Shots leave the right impact sound and bullet hole for whatever you hit.
- The first person arms and weapons use the game's own animation graphs, so deploys, reloads and inspects are the real ones, including spamming inspect and reload on the knives. You can take the silencer on and off on the M4A1-S and USP-S.
- Controls: WASD to move, Space to jump, Ctrl to crouch, Shift to walk, 3 for the rifle, 4 for the knife, Q for the last weapon, mouse buttons to shoot or swing, R to reload, F to inspect, C for third person.

Skins:

- The `CS2 Player` inspector lets you pick the gun for each team, the knife and the gloves, plus a paint kit, float, wear, pattern seed and StatTrak counter for each. Changes show up straight away while you're playing.
- Finishes are put together the same way the game does it, so the same paint kit, float and seed should give you the same look.
- This is purely visual and purely local. It has nothing to do with your CS2 inventory, the Steam Market or trading, and it can't give you anything in the actual game.

## Building it

You need Counter-Strike 2 and s&box installed, plus the .NET 10 SDK.

1. Clone the repo.
2. Open `cs2mount.sbproj` in the s&box editor once, so the editor compiles the shaders in `Assets/shaders`. Do this again after any s&box update: s&box won't load shaders compiled by an older version, and every CS2 material shows up as a red checkerboard.
3. Run `Build\build-mount.cmd`. It finds s&box through the `FACEPUNCH_ENGINE` environment variable, or falls back to the default Steam folder. If yours is somewhere else: `dotnet build Build\MountPackage\cs2.csproj -c Release -p:SboxPath="D:\your\sbox"`. The build stops if a shader isn't compiled and warns if one looks out of date.
4. Copy `Build\mount\Sandbox.Mounting.CS2` into your s&box `mount` folder, so you end up with `sbox\mount\Sandbox.Mounting.CS2\Sandbox.Mounting.CS2.dll`.
5. Launch s&box and turn on the Counter-Strike 2 mount.

Keep the folder name. s&box only reads component settings from assemblies whose names start with `Sandbox.`, so under any other name the maps load with all their lighting and post processing missing.

If you want to work on the mount itself, open `cs2mount.sbproj` in the editor instead, and take the built copy out of `sbox\mount` first. Otherwise you'll have two mounts called "cs2" and the installed one wins.

## Where things are

- `Editor/CS2Mount` is the mount: the file format readers and the loaders that turn CS2's models, materials, textures, maps and sounds into s&box ones.
- `Code/CS2` has the components the maps, players and weapons use: lighting, post processing, the 3D skybox, the animation graph runtime, the player, sounds and skins.
- `Assets/shaders` has the CS2 shaders.
- `Build` has the project and script that package all of it for `sbox\mount`.

## Known issues

- Weapons only work on the `CS2 Player`. You can't equip them in other games like Sandbox yet.
- No spread, penetration or damage, no bullet tracers, and the impact particles aren't ported (you get the sounds and the decals).
- Some finish types aren't done yet: sticker placement, case hardened patterns, patina, and the glitter and iridescent finishes.
- s&box draws one 3D skybox per scene, and can't rotate it. None of the current maps need that, but a map that did would show its skybox unrotated.
- Loading a lot of maps in one editor session eats video memory, and it doesn't all come back (that's on the s&box side). Restart the editor every few maps.
- CS2 updates can change the file formats or shaders and break things now and then.

## Credits

- Valve, for Counter-Strike 2 and Source 2. All the game content is theirs.
- Facepunch, for s&box and its mount API.
- [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) (MIT). The file format readers follow its work, the animation graph runtime in `Code/CS2/Animation` is ported from it with its licence kept next to it, and Source 2 Viewer's decompiles of CS2's shaders were the reference for mine.
- [Esoterica](https://github.com/BobbyAnguelov/Esoterica) by Bobby Anguelov (MIT), which the animation graph runtime comes from.
- [meshoptimizer](https://github.com/zeux/meshoptimizer) by Arseny Kapoulkine (MIT), for the vertex and index decoding, ported via ValveResourceFormat.
- [LZ4](https://github.com/lz4/lz4) and [Zstandard](https://github.com/facebook/zstd), whose compression formats the decoders are written from.

## Legal

Counter-Strike, Valve and Source are trademarks of Valve Corporation, and s&box is Facepunch Studios'. This is a fan project and isn't affiliated with or endorsed by either of them. It doesn't include any Valve assets, and it's not for commercial use.
