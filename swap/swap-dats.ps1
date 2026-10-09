# Swaps the texture dats inside the default install (C:\Turbine\Asheron's Call) between retail and a dev set,
# so the client, Decal and ThwargLauncher always run from their default locations.
#   .\swap-dats.ps1 status | dev | live
# Only client_portal.dat and client_highres.dat are swapped (the only files the texture pipeline changes).
# Everything is on C:, so swaps are instant renames. Switching to live verifies the retail fingerprints.

param([ValidateSet("status", "dev", "live")][string]$To = "status")

$ac      = "C:\Turbine\Asheron's Call"
$sets    = "C:\Users\ostet\ac-decomp\datsets"
$retail  = Join-Path $sets "retail"      # retail files live here while dev is active
$dev     = Join-Path $sets "dev"         # dev files live here while retail is active
$state   = Join-Path $sets "active.txt"
$files   = "client_portal.dat", "client_highres.dat"
$retailMd5 = @{ "client_portal.dat" = "2C89662A44FCDC2A3C31FE8F6677D265"; "client_highres.dat" = "538F63783CFA80D322375E56C439AF99" }
$retailSize = @{ "client_portal.dat" = 926941184; "client_highres.dat" = 133169152 }

New-Item -ItemType Directory -Force $retail, $dev | Out-Null
$active = if (Test-Path $state) { (Get-Content $state -Raw).Trim() } else { "live" }

function Stop-IfRunning {
    $p = Get-Process acclient, ThwargLauncher -ErrorAction SilentlyContinue
    if ($p) { Write-Host "Close these first: $(($p.Name | Sort-Object -Unique) -join ', ')" -ForegroundColor Yellow; exit 1 }
}
function Move-Set($from, $to) { foreach ($f in $files) { Move-Item (Join-Path $from $f) (Join-Path $to $f) -ErrorAction Stop } }

if ($To -eq "status") {
    Write-Host "Active dat set in ${ac}: $active"
    foreach ($f in $files) { $i = Get-Item (Join-Path $ac $f); "  {0,-20} {1,12:N0} bytes  {2}" -f $f, $i.Length, $(if ($i.Length -eq $retailSize[$f]) { "retail size" } else { "modified" }) }
    exit 0
}
if ($To -eq $active) { Write-Host "Already on $active."; exit 0 }
Stop-IfRunning

if ($To -eq "dev") {
    foreach ($f in $files) {
        if ((Get-Item (Join-Path $ac $f)).Length -ne $retailSize[$f]) { Write-Host "$f in $ac is not retail size; refusing to stash it as retail." -ForegroundColor Red; exit 1 }
        if (-not (Test-Path (Join-Path $dev $f))) { Write-Host "Missing dev file: $dev\$f" -ForegroundColor Red; exit 1 }
    }
    Move-Set $ac $retail
    Move-Set $dev $ac
    Set-Content $state "dev" -NoNewline
    Write-Host "Dev dats are now in $ac. Retail dats are held in $retail." -ForegroundColor Green
}
else {
    foreach ($f in $files) { if (-not (Test-Path (Join-Path $retail $f))) { Write-Host "Missing held retail file: $retail\$f" -ForegroundColor Red; exit 1 } }
    Move-Set $ac $dev
    Move-Set $retail $ac
    Set-Content $state "live" -NoNewline
    Write-Host "Verifying retail dats..."
    $ok = $true
    foreach ($f in $files) {
        $h = (Get-FileHash (Join-Path $ac $f) -Algorithm MD5).Hash
        if ($h -ne $retailMd5[$f]) { $ok = $false; Write-Host "  $f does NOT match retail ($h). Restore from C:\Users\ostet\ac-decomp\dats." -ForegroundColor Red }
        else { Write-Host "  $f matches retail" }
    }
    if ($ok) { Write-Host "Retail dats restored and verified." -ForegroundColor Green } else { exit 1 }
}
