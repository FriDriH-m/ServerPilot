param(
    [switch]$Ci,
    [switch]$SkipDockerBuild
)

$ErrorActionPreference = "Stop"

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory)][string]$CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "Command '$CommandName' failed with exit code $LASTEXITCODE."
    }
}

docker info
Assert-NativeCommandSucceeded "docker info"

dotnet tool restore
Assert-NativeCommandSucceeded "dotnet tool restore"

dotnet restore ServerPilot.slnx
Assert-NativeCommandSucceeded "dotnet restore"

dotnet build ServerPilot.slnx --configuration Release --no-restore
Assert-NativeCommandSucceeded "dotnet build"

dotnet format ServerPilot.slnx --verify-no-changes --no-restore
Assert-NativeCommandSucceeded "dotnet format"

$unitTestArguments = @(
    "test",
    "tests/ServerPilot.UnitTests/ServerPilot.UnitTests.csproj",
    "--configuration", "Release",
    "--no-build",
    "--no-restore"
)
$integrationTestArguments = @(
    "test",
    "tests/ServerPilot.IntegrationTests/ServerPilot.IntegrationTests.csproj",
    "--configuration", "Release",
    "--no-build",
    "--no-restore"
)
if ($Ci) {
    New-Item -ItemType Directory -Force -Path artifacts/test-results/unit | Out-Null
    New-Item -ItemType Directory -Force -Path artifacts/test-results/integration | Out-Null
    $unitTestArguments += @(
        "--logger", "trx;LogFileName=unit-tests.trx",
        "--results-directory", "artifacts/test-results/unit"
    )
    $integrationTestArguments += @(
        "--logger", "trx;LogFileName=integration-tests.trx",
        "--results-directory", "artifacts/test-results/integration"
    )
}

dotnet @unitTestArguments
Assert-NativeCommandSucceeded "dotnet test unit"

dotnet @integrationTestArguments
Assert-NativeCommandSucceeded "dotnet test integration"

$previousConnectionString = [Environment]::GetEnvironmentVariable(
    "ConnectionStrings__PostgreSql",
    [EnvironmentVariableTarget]::Process)
try {
    [Environment]::SetEnvironmentVariable(
        "ConnectionStrings__PostgreSql",
        "Host=localhost;Port=5432;Database=serverpilot_model_check;Username=serverpilot;Password=model-check-only",
        [EnvironmentVariableTarget]::Process)
    dotnet ef migrations has-pending-model-changes `
        --project src/ServerPilot.Infrastructure `
        --startup-project src/ServerPilot.Infrastructure `
        --configuration Release `
        --no-build
    Assert-NativeCommandSucceeded "dotnet ef migrations has-pending-model-changes"
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ConnectionStrings__PostgreSql",
        $previousConnectionString,
        [EnvironmentVariableTarget]::Process)
}

$previousPostgreSqlPassword = [Environment]::GetEnvironmentVariable(
    "POSTGRES_PASSWORD",
    [EnvironmentVariableTarget]::Process)
$previousJwtSigningKey = [Environment]::GetEnvironmentVariable(
    "JWT_SIGNING_KEY",
    [EnvironmentVariableTarget]::Process)
try {
    [Environment]::SetEnvironmentVariable(
        "POSTGRES_PASSWORD",
        "verify-only-postgresql-password",
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "JWT_SIGNING_KEY",
        "verify-only-jwt-signing-key-with-32-bytes",
        [EnvironmentVariableTarget]::Process)

    docker compose config --quiet
    Assert-NativeCommandSucceeded "docker compose config"

    if (-not $SkipDockerBuild) {
        docker compose build api
        Assert-NativeCommandSucceeded "docker compose build api"
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "POSTGRES_PASSWORD",
        $previousPostgreSqlPassword,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "JWT_SIGNING_KEY",
        $previousJwtSigningKey,
        [EnvironmentVariableTarget]::Process)
}

Write-Host "ServerPilot verification completed successfully."
