ASHERON'S CALL HD TEXTURES - 2x pack, version 1
================================================

What this is
------------
Every texture used on 3D models (creatures, armor, clothing, weapons, items,
buildings, dungeons, scenery) and the outdoor ground, upscaled to twice the
original resolution with an AI upscaler and packed back into the game's
data files. Interface art and icons are unchanged.

It only replaces two files in your Asheron's Call folder:
    client_portal.dat
    client_highres.dat

It stays END-OF-RETAIL COMPATIBLE. Unlike custom dat files, which tie you to
one server, this pack changes only textures: the dat version numbers and all
gameplay data are identical to end of retail. Every end-of-retail server sees
a normal retail client, so you install it once and play on any of them, with
no swapping files back and forth. It's entirely client-side, so servers need
no changes, and you can switch back to retail at any time.


Requirements
------------
- An end-of-retail Asheron's Call client (the usual install for emulator servers).
- About 2.3 GB of free disk space on the drive where the game is installed.
- Uses more memory than retail: in busy outdoor areas we measured about
  1.6 GB of the client's 4 GB limit, versus about 1.2 GB on retail.


Install
-------
1. Close Asheron's Call and ThwargLauncher (check the system tray).
2. Extract this whole zip to any folder.
3. Double-click "Install HD Textures.bat".

The installer keeps your current files as the "retail" set, copies the HD
files in, checks them against their checksums, and switches you to HD. Both
sets are kept in <AC folder>\hd-textures\.

It assumes your game is in C:\Turbine\Asheron's Call. If it's somewhere else,
open PowerShell in this folder and add -AcPath to any command, for example:

    .\hdtex.ps1 install -AcPath "D:\Path\To\Asheron's Call"


Switching between HD and retail
-------------------------------
With the game and ThwargLauncher closed, double-click:
    Use Retail Textures.bat   - back to the original textures
    Use HD Textures.bat       - back to HD
Switching is instant: it just swaps the two files.
To see which set is active:  .\hdtex.ps1 status


Uninstall
---------
Close the game and ThwargLauncher, then double-click "Uninstall HD Textures.bat".
It switches you back to your original files and deletes the hd-textures folder.


Manual install (if you prefer)
------------------------------
1. Close the game.
2. In your Asheron's Call folder, copy client_portal.dat and client_highres.dat
   somewhere safe.
3. Copy the two .dat files from this zip into your Asheron's Call folder,
   replacing the existing ones.
To undo it, copy your saved originals back.

Verify the files with SHA256SUMS.txt if you like:
    Get-FileHash client_portal.dat -Algorithm SHA256


Notes
-----
- High-res textures: keep the game's high-resolution texture option turned on,
  because part of the pack lives in client_highres.dat.
- First loads are slower. Areas take a moment longer to load the first time
  you visit them, because the textures are bigger.
- If the game crashes after installing, uninstall to confirm it's the pack,
  then report where it happened (location, and what you were doing) to the
  server team.
