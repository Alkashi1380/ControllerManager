param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "Echec de dotnet ($LASTEXITCODE) : $($args -join ' ')"
    }
}

function Clear-PublishDirectory([string]$path) {
    $directory = [IO.DirectoryInfo]::new($path)
    $rootPrefix = $repoRoot.TrimEnd('\') + '\'
    if (-not $directory.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Dossier de publication hors du projet : $path"
    }
    for ($entry = $directory; $entry.FullName -ne $repoRoot; $entry = $entry.Parent) {
        if ($entry.Exists -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Dossier de publication redirige : $($entry.FullName)"
        }
    }
    if ($directory.Exists) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
}

Push-Location -LiteralPath $repoRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Le SDK .NET est introuvable dans le PATH.'
    }
    if (-not $Version) {
        $project = [xml](Get-Content -LiteralPath 'ControllerManager.csproj' -Raw)
        $Version = $project.Project.PropertyGroup.Version
        if ($Version -notmatch '^\d+\.\d+\.\d+$') {
            throw 'Version invalide dans ControllerManager.csproj.'
        }
    }

    $publishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'
    $installerOutDir = Join-Path $repoRoot 'artifacts\installer'
    $zipPath = Join-Path $installerOutDir "ControllerManager-$Version-portable-win-x64.zip"
    $setupPath = Join-Path $installerOutDir "ControllerManager-Setup-$Version.exe"

    Invoke-DotNet test 'ControllerManager.Tests\ControllerManager.Tests.csproj' -c Release --nologo
    Invoke-DotNet publish 'ControllerManager.csproj' -c Release -r win-x64 --self-contained false "-p:Version=$Version" -o 'Release_Finale'
    Clear-PublishDirectory $publishDir
    Invoke-DotNet publish 'ControllerManager.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $publishDir
    New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
    $outputs = @($zipPath)

    if (-not $SkipInstaller) {
        $iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
        if (-not $iscc) {
            $iscc = @(
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
                "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
                "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
            ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        }
        if ($iscc) {
            & $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" 'installer\ControllerManager.iss'
            if ($LASTEXITCODE -ne 0) {
                throw "Echec de la compilation Inno Setup ($LASTEXITCODE)."
            }
            $outputs += $setupPath
        } else {
            Write-Host 'Inno Setup introuvable. Le ZIP portable est disponible.'
        }
    }

    $lines = foreach ($file in $outputs) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($file))"
    }
    $sumsPath = Join-Path $installerOutDir 'SHA256SUMS.txt'
    [IO.File]::WriteAllText($sumsPath, (($lines -join "`n") + "`n"), [Text.Encoding]::ASCII)
    Write-Host "Publication locale : $repoRoot\Release_Finale\ControllerManager.exe"
    $outputs | ForEach-Object { Write-Host $_ }
    Write-Host $sumsPath
}
finally {
    Pop-Location
}
