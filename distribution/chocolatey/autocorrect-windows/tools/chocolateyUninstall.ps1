$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'autocorrect-windows'
  softwareName   = 'AutoCorrect*'
  fileType       = 'exe'
  silentArgs     = '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-'
  validExitCodes = @(0)
}

[array]$key = Get-UninstallRegistryKey -SoftwareName $packageArgs.softwareName
if ($key.Count -eq 1) {
  $packageArgs['file'] = $key[0].UninstallString
  Uninstall-ChocolateyPackage @packageArgs
}
elseif ($key.Count -eq 0) {
  Write-Warning 'AutoCorrect not found — already uninstalled?'
}
else {
  Write-Warning "Multiple AutoCorrect uninstall entries found ($($key.Count))."
}
