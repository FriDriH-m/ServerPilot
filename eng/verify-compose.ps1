param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory)][string]$CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "Command '$CommandName' failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments)][string[]]$ComposeArguments)

    & docker compose --project-name $projectName @ComposeArguments
    Assert-NativeCommandSucceeded "docker compose $($ComposeArguments -join ' ')"
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Wait-ForHealthyApi {
    param([Parameter(Mandatory)][int]$Port)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    $baseUri = "http://127.0.0.1:$Port/health"
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $livenessResponse = Invoke-WebRequest -UseBasicParsing `
                -Uri "$baseUri/live" `
                -TimeoutSec 5
            $readinessResponse = Invoke-WebRequest -UseBasicParsing `
                -Uri "$baseUri/ready" `
                -TimeoutSec 5
            if ($livenessResponse.StatusCode -eq 200 -and $readinessResponse.StatusCode -eq 200) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 1
    }

    throw "API did not become live and ready at $baseUri within 90 seconds."
}

function Assert-MigrationsApplied {
    $tables = & docker compose --project-name $projectName exec -T postgres `
        psql -U serverpilot -d serverpilot -tAc '\dt'
    Assert-NativeCommandSucceeded "docker compose exec postgres psql"
    if (-not ($tables -match "__EFMigrationsHistory")) {
        throw "The EF Core migration history table was not created."
    }
}

function Start-AndVerifyCompose {
    param([switch]$Build)

    $arguments = @("up", "--detach")
    if ($Build) {
        $arguments += "--build"
    }

    Invoke-Compose @arguments
    Wait-ForHealthyApi -Port $apiPort
    Assert-MigrationsApplied
}

$projectName = "serverpilot-verify-$([Guid]::NewGuid().ToString('N'))"
$apiPort = Get-FreeTcpPort
do {
    $postgresPort = Get-FreeTcpPort
}
while ($postgresPort -eq $apiPort)
$previousPostgreSqlPassword = [Environment]::GetEnvironmentVariable(
    "POSTGRES_PASSWORD",
    [EnvironmentVariableTarget]::Process)
$previousJwtSigningKey = [Environment]::GetEnvironmentVariable(
    "JWT_SIGNING_KEY",
    [EnvironmentVariableTarget]::Process)
$previousApiHostPort = [Environment]::GetEnvironmentVariable(
    "SERVERPILOT_API_HOST_PORT",
    [EnvironmentVariableTarget]::Process)
$previousPostgreSqlHostPort = [Environment]::GetEnvironmentVariable(
    "SERVERPILOT_POSTGRES_HOST_PORT",
    [EnvironmentVariableTarget]::Process)

try {
    $postgresBytes = [byte[]]::new(24)
    $jwtBytes = [byte[]]::new(48)
    $randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomNumberGenerator.GetBytes($postgresBytes)
        $randomNumberGenerator.GetBytes($jwtBytes)
    }
    finally {
        $randomNumberGenerator.Dispose()
    }
    [Environment]::SetEnvironmentVariable(
        "POSTGRES_PASSWORD",
        [Convert]::ToBase64String($postgresBytes),
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "JWT_SIGNING_KEY",
        [Convert]::ToBase64String($jwtBytes),
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "SERVERPILOT_API_HOST_PORT",
        $apiPort.ToString([Globalization.CultureInfo]::InvariantCulture),
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "SERVERPILOT_POSTGRES_HOST_PORT",
        $postgresPort.ToString([Globalization.CultureInfo]::InvariantCulture),
        [EnvironmentVariableTarget]::Process)

    Invoke-Compose config --quiet
    Start-AndVerifyCompose -Build:(-not $SkipBuild)
    Invoke-Compose down --volumes --remove-orphans
    Start-AndVerifyCompose
}
catch {
    & docker compose --project-name $projectName logs --no-color
    throw
}
finally {
    & docker compose --project-name $projectName down --volumes --remove-orphans
    [Environment]::SetEnvironmentVariable(
        "POSTGRES_PASSWORD",
        $previousPostgreSqlPassword,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "JWT_SIGNING_KEY",
        $previousJwtSigningKey,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "SERVERPILOT_API_HOST_PORT",
        $previousApiHostPort,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "SERVERPILOT_POSTGRES_HOST_PORT",
        $previousPostgreSqlHostPort,
        [EnvironmentVariableTarget]::Process)
}

Write-Host "ServerPilot Compose verification completed successfully."
