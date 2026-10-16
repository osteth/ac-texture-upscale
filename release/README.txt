ASHERON'S CALL HD TEXTURES - 2x pack, version 1
================================================

What this is
------------
Every texture used on 3D models (creatures, armor, clothing, weapons, items,
buildings, dungeons, scenery) and the outdoor ground, upscaled to twice the
original resolution with an AI upscaler and packed back into the game's
data files. Interface art and icons are unchanged.

It stays END-OF-RETAIL COMPATIBLE. Unlike custom dat files, which tie you to
one server, this pack changes only textures: the dat version numbers and all
gameplay data are identical to end of retail. Every end-of-retail server sees
a normal retail client, so you install it once and play on any of them. It's
entirely client-side, so servers need no changes.


What's in the zip
-----------------
    HD DATs\                 a complete set of game data files:
        client_portal.dat        HD textures
        client_highres.dat       HD textures
        client_cell_1.dat        unchanged end-of-retail file
        client_local_English.dat unchanged end-of-retail file
    Install HD Textures.bat  and the other .bat files (ThwargLauncher setup)
    hdtex.ps1                the script the .bat files run
    SHA256SUMS.txt           checksums for the four .dat files

Need the original files back? The AC-Retail-DATs download, from the same
place you got this pack, is a clean set of all four end-of-retail dat files.


Requirements
------------
- An end-of-retail Asheron's Call client (the usual install for emulator
  servers), or Vitaeum.
- About 2.6 GB of free disk space.
- Uses more memory than retail: in busy outdoor areas we measured about
  1.6 GB of the retail client's 4 GB limit, versus about 1.2 GB on retail.


Is HD right for your PC?
------------------------
The in-game setting Environment Texture Detail decides how sharp textures
are. HD moves every step of it up one notch:
- Play retail at High or Very High? Use HD.
- Struggling at retail High? HD at Medium costs about the same and looks at
  least as good.
- Play retail at Medium or lower? Stay on retail. HD below Medium looks like
  retail one step up but loads slower.
Landscape Texture Detail (the ground) works the same way.

For Turbine's own high-res versions of about 2,300 textures (including many
weapons and creatures), set Environment Texture Detail to Very High yourself: the
game's overall "Very High" preset only sets it to High.

The website has an interactive version of this, with every step compared:
https://osteth.github.io/ac-texture-upscale/#settings


Install for Vitaeum
-------------------
Vitaeum reads the dat files from a folder you choose, so there's nothing to
copy into your game folder.
1. Extract this whole zip somewhere permanent, for example
   Documents\AC-HD. Vitaeum reads the files from there, so don't delete it.
2. Choose where HD applies:
   - Every character: Settings > RETAIL DAT FILES > CHOOSE FOLDER..., and
     pick the "HD DATs" folder.
   - Some characters only: in Favorites, open a launch row, click CHOOSE...
     next to "DAT files", pick the "HD DATs" folder, then SAVE ROW. Other rows
     keep using the default folder.
To have retail and HD side by side, point the default at a retail set (your
game folder, or the AC-Retail-DATs download) and give the HD rows the
"HD DATs" folder. Each login uses its own row's files.


Install for ThwargLauncher (retail client)
------------------------------------------
1. Close Asheron's Call and ThwargLauncher (check the system tray).
2. Extract this whole zip to any folder.
3. Double-click "Install HD Textures.bat".

The installer keeps your current client_portal.dat and client_highres.dat as
the "retail" set, copies the HD versions in, checks them against their
checksums, and switches you to HD. Both sets are kept in
<AC folder>\hd-textures\. Only those two files change; cell and local are
the same as retail.

It assumes your game is in C:\Turbine\Asheron's Call. If it's somewhere else,
open PowerShell in this folder and add -AcPath to any command, for example:

    .\hdtex.ps1 install -AcPath "D:\Path\To\Asheron's Call"

Switching between HD and retail: with the game and ThwargLauncher closed,
double-click "Use Retail Textures.bat" or "Use HD Textures.bat". Switching is
instant: it just swaps the two files. To see which set is active, run
.\hdtex.ps1 status

Uninstall: close the game and ThwargLauncher, then double-click
"Uninstall HD Textures.bat". It switches you back to your original files and
deletes the hd-textures folder.


Manual install (if you prefer)
------------------------------
1. Close the game.
2. In your Asheron's Call folder, copy client_portal.dat and client_highres.dat
   somewhere safe.
3. Copy those two files from the "HD DATs" folder into your Asheron's Call
   folder, replacing the existing ones.
To undo it, copy your saved originals back (or use the AC-Retail-DATs files).

Verify the files with SHA256SUMS.txt if you like:
    Get-FileHash "HD DATs\client_portal.dat" -Algorithm SHA256


Notes
-----
- First loads are slower. Areas take a moment longer to load the first time
  you visit them, because the textures are bigger.
- If the game crashes with HD, switch to retail to confirm it's the pack, then
  report where it happened (location, and what you were doing) on the GitHub
  issues page.
