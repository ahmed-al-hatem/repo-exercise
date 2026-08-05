$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactParent = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$artifactDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactParent "BleFinder-win-x64"))
$projectPath = Join-Path $repositoryRoot "src/BleFinder.App/BleFinder.App.csproj"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Application project not found: $projectPath"
}

$actualParent = [System.IO.Path]::GetDirectoryName($artifactDirectory)
$isExpectedParent = [string]::Equals(
    $actualParent,
    $artifactParent,
    [System.StringComparison]::OrdinalIgnoreCase)
$isExpectedLeaf = [string]::Equals(
    [System.IO.Path]::GetFileName($artifactDirectory),
    "BleFinder-win-x64",
    [System.StringComparison]::Ordinal)

if (-not $isExpectedParent -or -not $isExpectedLeaf) {
    throw "Refusing to clean an unexpected artifact directory: $artifactDirectory"
}

if (Test-Path -LiteralPath $artifactDirectory) {
    $artifactItem = Get-Item -LiteralPath $artifactDirectory -Force
    $isReparsePoint = ($artifactItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    if (-not $artifactItem.PSIsContainer -or $isReparsePoint) {
        throw "Refusing to clean an artifact path that is not a regular directory: $artifactDirectory"
    }

    Remove-Item -LiteralPath $artifactDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactParent -Force | Out-Null
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

Push-Location -LiteralPath $repositoryRoot
try {
    & dotnet publish src/BleFinder.App/BleFinder.App.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
        -o artifacts/BleFinder-win-x64

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$publishedExecutable = Join-Path $artifactDirectory "BleFinder.App.exe"
$distributionExecutable = Join-Path $artifactDirectory "BleFinder.exe"

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable not found: $publishedExecutable"
}

Rename-Item -LiteralPath $publishedExecutable -NewName "BleFinder.exe"

$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $artifactDirectory -Recurse -File |
        Where-Object { $_.Extension -in ".dll", ".pdb" }
)
if ($forbiddenFiles.Count -ne 0) {
    $forbiddenPaths = ($forbiddenFiles.FullName -join [Environment]::NewLine)
    throw "Distribution contains DLL or PDB files:$([Environment]::NewLine)$forbiddenPaths"
}

$executables = @(Get-ChildItem -LiteralPath $artifactDirectory -Recurse -File -Filter "*.exe")
if ($executables.Count -ne 1 -or $executables[0].FullName -ne $distributionExecutable) {
    $executablePaths = ($executables.FullName -join [Environment]::NewLine)
    throw "Expected exactly one BleFinder.exe, found $($executables.Count):$([Environment]::NewLine)$executablePaths"
}

$hash = Get-FileHash -LiteralPath $distributionExecutable -Algorithm SHA256
Write-Host "Portable executable: $distributionExecutable"
Write-Host "SHA256: $($hash.Hash)"
