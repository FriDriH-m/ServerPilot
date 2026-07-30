param(
    [string]$OutputDirectory = "",
    [switch]$SkipArchive
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory)][string]$CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "Command '$CommandName' failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot "agent/win-x64"
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Agent package output must be a child of '$artifactsRoot'."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

$applicationDirectory = Join-Path $resolvedOutput "app"
$scriptsDirectory = Join-Path $resolvedOutput "scripts"
New-Item -ItemType Directory -Path $applicationDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $scriptsDirectory -Force | Out-Null

$projectPath = Join-Path $repositoryRoot "src/ServerPilot.Agent/ServerPilot.Agent.csproj"
dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $applicationDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugSymbols=false `
    -p:DebugType=None
Assert-NativeCommandSucceeded "dotnet publish ServerPilot.Agent"

$managementScript = Join-Path $PSScriptRoot "Manage-AgentService.ps1"
$parseTokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $managementScript,
    [ref]$parseTokens,
    [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -ne 0) {
    $messages = $parseErrors | ForEach-Object { $_.Message }
    throw "Agent service management script has parse errors: $($messages -join '; ')"
}

Copy-Item -LiteralPath $managementScript -Destination $scriptsDirectory
Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "docs/windows-agent-service.md") `
    -Destination (Join-Path $resolvedOutput "README.md")

if (-not $SkipArchive) {
    $archivePath = "$resolvedOutput.zip"
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -Path (Join-Path $resolvedOutput "*") -DestinationPath $archivePath
    Write-Host "Agent package archive: $archivePath"
}

Write-Host "Agent package directory: $resolvedOutput"
