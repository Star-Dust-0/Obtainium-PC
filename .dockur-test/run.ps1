# ============================================================================
# windows-vm-test guest script - runs INSIDE the Windows 11 VM at first boot.
# Template: set these three variables per repo, nothing else changes.
# ============================================================================
$Repo       = 'Star-Dust-0/Obtainium-PC'   # GitHub repo with the release
$AssetMatch = 'portable'                # regex on the release asset name
$ExeMatch   = 'ObtainiumPC'            # regex on the exe file/process name
# ============================================================================
$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
function Shot($n){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bm=New-Object System.Drawing.Bitmap $b.Width,$b.Height; $g=[System.Drawing.Graphics]::FromImage($bm); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $bm.Save("\\host.lan\Data\$n.png"); "shot $n $($b.Width)x$($b.Height)" }
Shot 'desktop'
$tag = ''
if (Test-Path '\host.lan\Data\release-tag.txt') { $tag = (Get-Content '\host.lan\Data\release-tag.txt' -Raw).Trim() }
if ($tag) { $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/tags/$tag" }
else { $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" }
"release: $($rel.tag_name) (requested tag: '$tag')"; $rel.assets | % { "asset: $($_.name) $($_.size)" }
$a = $rel.assets | ? { $_.name -match $AssetMatch } | select -First 1
if (-not $a) { "ERROR: no release asset matches '$AssetMatch'"; exit 1 }
Invoke-WebRequest $a.browser_download_url -OutFile C:\app.zip
$sums = $rel.assets | ? { $_.name -eq 'SHA256SUMS.txt' } | select -First 1
if ($sums) {
  Invoke-WebRequest $sums.browser_download_url -OutFile C:\SHA256SUMS.txt
  $line = Get-Content C:\SHA256SUMS.txt | ? { $_ -match [regex]::Escape($a.name) } | select -First 1
  if ($line) {
    $want = ($line -split '\s+')[0].ToLower()
    $got = (Get-FileHash C:\app.zip -Algorithm SHA256).Hash.ToLower()
    "sha256 want=$want"; "sha256 got =$got"
    if ($want -ne $got) { "ERROR: SHA256 mismatch for $($a.name)"; exit 1 }
    "sha256 OK"
  } else { "WARN: asset not listed in SHA256SUMS.txt" }
} else { "WARN: no SHA256SUMS.txt asset; download not hash-verified" }
Expand-Archive C:\app.zip C:\app -Force
$exe = Get-ChildItem C:\app -Recurse -Filter *.exe | ? { $_.Name -match $ExeMatch } | select -First 1
if (-not $exe) { "ERROR: no exe matches '$ExeMatch'"; exit 1 }
"exe: $($exe.FullName)"
Start-Process $exe.FullName
Start-Sleep 40
# Close the Start menu / any first-run shell popup so the app is unobstructed
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys('{ESC}'); Start-Sleep 2
$ws.SendKeys('{ESC}'); Start-Sleep 18
Get-Process | ? { $_.Name -match $ExeMatch } | % { "proc: $($_.Name) $((Get-Item $_.Path).VersionInfo.FileVersion)" }
"exe version: $((Get-Item $exe.FullName).VersionInfo.FileVersion)"
Shot 'app'
Start-Sleep 45
Shot 'app2'
