# AC HD texture pack manager.
#   .\hdtex.ps1 install     first-time setup: keeps your current textures as "retail", switches to HD
#   .\hdtex.ps1 hd          switch to HD textures      (instant)
#   .\hdtex.ps1 retail      switch to retail textures  (instant)
#   .\hdtex.ps1 status      show which set is active
#   .\hdtex.ps1 uninstall   switch to retail and remove the HD files
# Add -AcPath "D:\Path\To\Asheron's Call" if the game isn't in C:\Turbine\Asheron's Call.
#
# Both sets live in <AC folder>\hd-textures\ (hd\ and retail\). Switching moves two files between that folder
# and the AC folder, which on the same drive is just a rename, so it takes no time and needs no extra space.

param(
    [Parameter(Position = 0)][ValidateSet("install", "hd", "retail", "status", "uninstall")][string]$Action = "status",
    [string]$AcPath = "C:\Turbine\Asheron's Call"
)

$ErrorActionPreference = "Stop"
$pack  = $PSScriptRoot
$files = "client_portal.dat", "client_highres.dat"
$store = Join-Path $AcPath "hd-textures"
$state = Join-Path $store "active.txt"
$hdSha = @{
    "client_portal.dat"  = "1509a08871f1ed068dfd61495b839e659d2768bb10e9f4f0302a50f91ff67b9f"
    "client_highres.dat" = "a80db1fc5b7533092007920abe1dfa414ae766ad976d361d7f8d89cf742658e0"
}

function Fail($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }
function Active { if (Test-Path $state) { (Get-Content $state -Raw).Trim() } else { "" } }
function Require-Closed { if (Get-Process acclient, ThwargLauncher -ErrorAction SilentlyContinue) { Fail "Close Asheron's Call and ThwargLauncher first (check the system tray), then try again." } }
function Move-Set($from, $to) { foreach ($f in $files) { Move-Item (Join-Path $from $f) (Join-Path $to $f) -Force } }
function Switch-To($target) {
    $current = Active
    if ($current -eq $target) { Write-Host "Already using $target textures."; return }
    $other = Join-Path $store $target
    foreach ($f in $files) { if (-not (Test-Path (Join-Path $other $f))) { Fail "The $target set is missing $f in $other." } }
    Move-Set $AcPath (Join-Path $store $current)
    Move-Set $other $AcPath
    Set-Content $state $target -NoNewline
    Write-Host "Now using $target textures." -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $AcPath "acclient.exe"))) { Fail "acclient.exe not found in '$AcPath'. Add -AcPath ""<your AC folder>""." }

switch ($Action) {
    "status" {
        if (-not (Active)) { Write-Host "HD textures are not installed in $AcPath." } else { Write-Host "Using $(Active) textures ($AcPath)." }
    }
    "install" {
        Require-Closed
        if (Active) { Write-Host "Already installed (using $(Active)). Use 'hd' or 'retail' to switch."; exit 0 }
        foreach ($f in $files) { if (-not (Test-Path (Join-Path $pack $f))) { Fail "Missing $f next to this script. Extract the whole zip first." } }
        foreach ($f in $files) {
            if ((Get-FileHash (Join-Path $AcPath $f) -Algorithm SHA256).Hash.ToLower() -eq $hdSha[$f]) { Fail "$f in your AC folder is already the HD version, so there's no retail copy to keep. Restore your original $f first." }
        }
        $exe = [IO.File]::ReadAllBytes((Join-Path $AcPath "acclient.exe")); $pe = [BitConverter]::ToInt32($exe, 0x3c)
        if (-not ([BitConverter]::ToUInt16($exe, $pe + 22) -band 0x20)) { Write-Host "Warning: this acclient.exe is not large-address-aware (2 GB limit). Busy areas may run out of memory." -ForegroundColor Yellow }

        New-Item -ItemType Directory -Force (Join-Path $store "hd"), (Join-Path $store "retail") | Out-Null
        foreach ($f in $files) {
            Write-Host "Copying HD $f into place (this takes a minute) ..."
            Copy-Item (Join-Path $pack $f) (Join-Path $store "hd\$f") -Force
            if ((Get-FileHash (Join-Path $store "hd\$f") -Algorithm SHA256).Hash.ToLower() -ne $hdSha[$f]) { Fail "$f did not copy correctly. Your game files were not changed; try again." }
        }
        Set-Content $state "retail" -NoNewline   # current files are the retail set
        Switch-To "hd"
        Write-Host "Installed. Switch any time with 'Use Retail Textures.bat' / 'Use HD Textures.bat'."
    }
    "hd"     { Require-Closed; if (-not (Active)) { Fail "Not installed yet. Run 'Install HD Textures.bat' first." }; Switch-To "hd" }
    "retail" { Require-Closed; if (-not (Active)) { Fail "Not installed yet, so you're already on your own textures." }; Switch-To "retail" }
    "uninstall" {
        Require-Closed
        if (-not (Active)) { Write-Host "HD textures are not installed."; exit 0 }
        Switch-To "retail"
        Remove-Item $store -Recurse -Force
        Write-Host "Uninstalled. Your original textures are in place." -ForegroundColor Green
    }
}
