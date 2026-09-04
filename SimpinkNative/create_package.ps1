$ErrorActionPreference = 'Stop'
$PublishDir = "bin\Release\net8.0-windows\win-x64\publish"
$PackageName = "Simpink_Portable_Win64"
$PackageDir = ".\$PackageName"
$ZipFile = ".\$PackageName.zip"

Write-Host "Creating package directory..."
if (Test-Path $PackageDir) { Remove-Item -Recurse -Force $PackageDir }
New-Item -ItemType Directory -Path $PackageDir | Out-Null

Write-Host "Copying Simpink.exe and dependencies..."
Copy-Item -Path "$PublishDir\*" -Destination $PackageDir -Recurse -Force

Write-Host "FFmpeg will be downloaded automatically by the app on first recording."

Write-Host "Waiting for file handles to be released..."
Start-Sleep -Seconds 3

Write-Host "Compressing to $ZipFile..."
if (Test-Path $ZipFile) { Remove-Item -Force $ZipFile }
Compress-Archive -Path "$PackageDir\*" -DestinationPath $ZipFile

Write-Host "Cleaning up staging directory..."
Remove-Item -Recurse -Force $PackageDir

Write-Host "Done! Package is ready at $ZipFile"
