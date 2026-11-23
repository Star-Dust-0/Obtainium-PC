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
# Fail closed: a supplied tag that cannot be read or resolved must stop the run,
# never silently fall back to the latest stable release.
$tagFile = '\\host.lan\Data\release-tag.txt'
$tagFilePresent = Test-Path $tagFile
$tag = ''
if ($tagFilePresent) { $tag = (Get-Content $tagFile -Raw).Trim() }
if ($tagFilePresent -and -not $tag) { "ERROR: release-tag.txt exists but is empty or unreadable; refusing to fall back to latest"; exit 1 }
if ($tag) {
  try { $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/tags/$tag" }
  catch { "ERROR: could not resolve release tag '$tag': $($_.Exception.Message)"; exit 1 }
  if ($rel.tag_name -ne $tag) { "ERROR: resolved release '$($rel.tag_name)' does not match requested tag '$tag'"; exit 1 }
}
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
"launching at $(Get-Date -Format o)"
try { Start-Process $exe.FullName; "Start-Process returned at $(Get-Date -Format o)" }
catch { "ERROR launching: $($_.Exception.Message)" }
Start-Sleep 40
# Close the Start menu / any first-run shell popup so the app is unobstructed
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys('{ESC}'); Start-Sleep 2
$ws.SendKeys('{ESC}'); Start-Sleep 18
# Close the leftover install console so it cannot cover the app window
Get-Process cmd -ErrorAction SilentlyContinue | ? { $_.MainWindowTitle -match 'Install' } | % { $_.CloseMainWindow() | Out-Null }
Start-Sleep 2
# Bring the app window to the foreground before the screenshots
$appProc = Get-Process | ? { $_.Name -match $ExeMatch -and $_.MainWindowHandle -ne 0 } | select -First 1
if ($appProc) { $ws.AppActivate($appProc.Id) | Out-Null; "activated window: $($appProc.MainWindowTitle)" } else { "WARN: no app window handle" }
Start-Sleep 5
Get-Process | ? { $_.Name -match $ExeMatch } | % { "proc: $($_.Name) $((Get-Item $_.Path).VersionInfo.FileVersion)" }
$fileVer = (Get-Item $exe.FullName).VersionInfo.FileVersion
"exe version: $fileVer"
if ($tag) {
  $wantV = $tag.TrimStart('v','V')
  if ($fileVer -and -not $fileVer.StartsWith($wantV)) { "ERROR: exe version $fileVer does not match requested tag $tag"; exit 1 }
  "exe version matches requested tag"
}
Shot 'app'
Start-Sleep 45
Shot 'app2'
