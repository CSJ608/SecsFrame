[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release",

    [switch]$SkipArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$packageRoot = Join-Path $artifactsRoot "demo-package"
$buildRoot = Join-Path $artifactsRoot "demo-package-build"
$archivePath = Join-Path $artifactsRoot "SecsFrame-Demos-net8.0.zip"

function Invoke-DemoPublish {
    param(
        [Parameter(Mandatory)]
        [string]$Project,

        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [string]$BuildOutput
    )

    & dotnet publish $Project `
        --configuration $Configuration `
        --nologo `
        --output $Output `
        -p:UseAppHost=false `
        "-p:BaseOutputPath=$BuildOutput/"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $packageRoot | Out-Null

Invoke-DemoPublish `
    -Project (Join-Path $repositoryRoot "demo/SecsFrame.DemoLauncher/SecsFrame.DemoLauncher.csproj") `
    -Output (Join-Path $packageRoot "launcher") `
    -BuildOutput (Join-Path $buildRoot "launcher")
Invoke-DemoPublish `
    -Project (Join-Path $repositoryRoot "demo/SecsFrame.CommunicationDemo/SecsFrame.CommunicationDemo.csproj") `
    -Output (Join-Path $packageRoot "communication") `
    -BuildOutput (Join-Path $buildRoot "communication")
Invoke-DemoPublish `
    -Project (Join-Path $repositoryRoot "demo/SecsFrame.GuidedDemo/SecsFrame.GuidedDemo.csproj") `
    -Output (Join-Path $packageRoot "guided") `
    -BuildOutput (Join-Path $buildRoot "guided")

Copy-Item -LiteralPath (Join-Path $repositoryRoot "demo/package/start-demos.cmd") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot "demo/package/start-demos.sh") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot "demo/package/README.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $packageRoot

$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the package source commit."
}
$workingTreeChanges = @(& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the package working tree state."
}

$manifest = [ordered]@{
    Format = "SecsFrame-DemoPackage/1"
    TargetFramework = "net8.0"
    Commit = $commit
    Dirty = $workingTreeChanges.Count -gt 0
    Applications = @(
        [ordered]@{
            Name = "SecsFrame.CommunicationDemo"
            DefaultUrl = "http://127.0.0.1:5080"
        },
        [ordered]@{
            Name = "SecsFrame.GuidedDemo"
            DefaultUrl = "http://127.0.0.1:5081"
        }
    )
}
$manifest |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $packageRoot "package.json") -Encoding utf8

if (-not $SkipArchive) {
    Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Host "Created $archivePath"
}

Write-Host "Published Demo package to $packageRoot"
