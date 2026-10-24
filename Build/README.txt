Counter-Strike 2 mount for s&box
================================

Install: copy this "Sandbox.Mounting.CS2" folder into s&box's mount folder, so it reads
    sbox/mount/Sandbox.Mounting.CS2/Sandbox.Mounting.CS2.dll
    sbox/mount/Sandbox.Mounting.CS2/Assets/shaders/...
then start s&box (or the Sandbox game) and enable the Counter-Strike 2 mount. Counter-Strike 2 must be installed
through Steam. (The name has to start with "Sandbox.": s&box only reads the components' settings from assemblies
named that way, and without them the maps load unlit.)

What's in it: the mount (models, materials, textures, maps, sounds and sound events, surfaces, decals, weapons and
paint kits from CS2's VPKs) and the components its maps and players use (CS2 Map, CS2 Map Lighting, CS2 Post
Processing, CS2 Player, CS2 Animator...), plus the shaders its materials and weapon finishes use.

Don't keep it installed while working on the cs2mount project in the editor: both register a mount called "cs2",
the installed one loads first and the project's (with your latest changes) is refused. Remove the folder from
sbox/mount before opening the project.

Rebuild from the project with Build/build-mount.cmd (the editor compiles the shaders it copies; the build stops if
one isn't compiled and warns if one may be out of date).
