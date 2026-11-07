$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

$packageArgs = @{
  packageName    = 'autocorrect'
  fileType       = 'exe'
  url64bit       = 'https://github.com/BADATCODING12345678910/AutoCorrect-Windows/releases/download/v0.2.23/AutoCorrectSetup.exe'
  checksum64     = 'ad9c7280852a00a7b7006666c645ed01e2705c8cc533a70ff697e39c33a2a240'
  checksumType64 = 'sha256'
  silentArgs     = '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
