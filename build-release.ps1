[CmdletBinding()]
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectDirectory = $PSScriptRoot
$outputDirectory = Join-Path $projectDirectory "dist"
$resolvedOutput = [System.IO.Path]::GetFullPath($outputDirectory)
$resolvedProject = [System.IO.Path]::GetFullPath($projectDirectory)

if (-not $resolvedOutput.StartsWith($resolvedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录必须位于项目目录内。"
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

dotnet publish (Join-Path $projectDirectory "Snapshot.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --source "https://api.nuget.org/v3/index.json" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $resolvedOutput

if ($LASTEXITCODE -ne 0) {
    throw "发布失败，dotnet publish 返回代码 $LASTEXITCODE。"
}

Copy-Item -LiteralPath (Join-Path $projectDirectory "README.md") `
    -Destination (Join-Path $resolvedOutput "使用说明.md")

Write-Output "Snapshot 已发布到：$resolvedOutput"
