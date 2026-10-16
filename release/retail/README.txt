ASHERON'S CALL END-OF-RETAIL DAT FILES
======================================

The four original end-of-retail game data files, unchanged:
    Retail DATs\client_portal.dat         (iteration 2072)
    Retail DATs\client_highres.dat        (iteration 497)
    Retail DATs\client_cell_1.dat
    Retail DATs\client_local_English.dat

This is the set that nearly every emulator server expects. It's offered
alongside the AC-HD texture pack so you always have a clean copy to go back
to, or a second set to run side by side with HD.


Use with Vitaeum
----------------
Extract this zip somewhere permanent, for example Documents\AC-Retail, then
pick the "Retail DATs" folder in Vitaeum:
- As the default: Settings > RETAIL DAT FILES > CHOOSE FOLDER...
- For one launch row: Favorites > open the row > CHOOSE... next to
  "DAT files" > SAVE ROW.
With the AC-HD pack's "HD DATs" folder on other rows, each login uses its own
set, so retail and HD characters can play at the same time.


Restore the retail files in your game folder
--------------------------------------------
1. Close Asheron's Call and ThwargLauncher.
2. Copy the four files from "Retail DATs" into your Asheron's Call folder,
   and choose Replace when asked.

Verify the files with SHA256SUMS.txt if you like:
    Get-FileHash "Retail DATs\client_portal.dat" -Algorithm SHA256
