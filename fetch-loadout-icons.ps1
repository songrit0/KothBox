# Auto-fetch loadout gun icons from the shop DB (sv_items.image_url).
#
# Reads the KothBox config's <Loadouts>, takes each loadout's FIRST item id (the gun),
# looks up its image_url in sv_items, downloads the .webp and converts it to
# Assets/KothUI/Icons/loadout_<N>.png (which BuildKothUI bakes into the bundle).
#
# Run this whenever the loadouts change, then re-run "Unturned KothUI / 1. Generate Loadout Panel".
#   powershell -ExecutionPolicy Bypass -File fetch-loadout-icons.ps1
$ErrorActionPreference = "Stop"

$configPath = "D:\SteamLibrary\steamapps\common\U3DS\Servers\Default\Rocket\Plugins\KothBox\KothBox.configuration.xml"
$iconDir    = "C:\Users\MARU\Code Locks\My project\Assets\KothUI\Icons"
$mysqlDll   = "C:\Users\MARU\UnturnedMods\KothBox\lib\MySql.Data.dll"

[xml]$cfg = Get-Content $configPath -Raw
$conn = $cfg.KothConfiguration.Database.ConnectionString
$loadouts = @($cfg.KothConfiguration.Loadouts.Loadout)

# first ItemId of each loadout = the gun
$gunIds = @()
foreach ($l in $loadouts) { $gunIds += [int]($l.ItemId | Select-Object -First 1) }
Write-Host "loadout guns:" ($gunIds -join ", ")

Add-Type -Path $mysqlDll
function Open-Conn($cs) { for ($i=0;$i -lt 8;$i++){ $c=New-Object MySql.Data.MySqlClient.MySqlConnection($cs); try{$c.Open();return $c}catch{try{$c.Dispose()}catch{}} }; throw "db open failed" }

$urls = @{}
$c = Open-Conn $conn
try {
  $inList = ($gunIds -join ",")
  $cmd = $c.CreateCommand(); $cmd.CommandText = "SELECT id, image_url FROM sv_items WHERE id IN ($inList)"
  $r = $cmd.ExecuteReader()
  while ($r.Read()) { $u = $r.GetValue(1); if (-not ($u -is [DBNull])) { $urls[[int]$r.GetValue(0)] = "$u" } }
  $r.Close()
} finally { $c.Close() }

New-Item -ItemType Directory -Force $iconDir | Out-Null
$tmp = "$env:TEMP\kothicons"; New-Item -ItemType Directory -Force $tmp | Out-Null
for ($i=0; $i -lt $gunIds.Count; $i++) {
  $id = $gunIds[$i]
  if (-not $urls.ContainsKey($id)) { Write-Host "skip loadout $i (gun $id has no image_url)"; continue }
  $webp = "$tmp\loadout_$i.webp"; $png = "$iconDir\loadout_$i.png"
  Invoke-WebRequest -Uri $urls[$id] -OutFile $webp -UseBasicParsing
  # Start-Process avoids PowerShell wrapping ffmpeg's stderr banner as a fatal error.
  Start-Process -FilePath "ffmpeg" -ArgumentList @("-y","-i",$webp,$png) -Wait -NoNewWindow -RedirectStandardError "$tmp\ff.log"
  if (Test-Path $png) { Write-Host "OK loadout_$i.png  (gun $id)" } else { Write-Host "FAIL convert loadout $i" }
}
Write-Host "Done. Now run 'Unturned KothUI / 1. Generate Loadout Panel' in Unity."
