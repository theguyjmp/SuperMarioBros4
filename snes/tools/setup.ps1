# Downloads the SNES toolchain into snes\tools (not kept in git): cc65 (ca65/ld65 assembler+linker) and Mesen2 (emulator, headless tests).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Test-Path cc65\bin\ca65.exe)) {
    Invoke-WebRequest 'https://sourceforge.net/projects/cc65/files/cc65-snapshot-win64.zip/download' -OutFile cc65.zip -UserAgent 'Wget'
    Expand-Archive cc65.zip cc65 -Force; Remove-Item cc65.zip
}
if (-not (Test-Path mesen\Mesen.exe)) {
    Invoke-WebRequest 'https://github.com/SourMesen/Mesen2/releases/download/2.1.1/Mesen_2.1.1_Windows.zip' -OutFile mesen.zip
    Expand-Archive mesen.zip mesen -Force; Remove-Item mesen.zip
    '{}' | Set-Content mesen\settings.json -Encoding ascii   # portable mode, skips the first-run wizard
}
Write-Host 'SNES toolchain ready.'
