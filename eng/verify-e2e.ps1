param(
    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory)][string]$CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "Command '$CommandName' failed with exit code $LASTEXITCODE."
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-RandomSecret {
    param([Parameter(Mandatory)][int]$ByteCount)

    $bytes = [byte[]]::new($ByteCount)
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $generator.Dispose()
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments)][string[]]$ComposeArguments)

    & docker compose --project-name $projectName @ComposeArguments
    Assert-NativeCommandSucceeded "docker compose $($ComposeArguments -join ' ')"
}

function Invoke-ApiRequest {
    param(
        [Parameter(Mandatory)][ValidateSet("GET", "POST")][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int[]]$ExpectedStatus,
        [string]$AccessToken,
        [AllowNull()][object]$Body
    )

    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        $headers.Authorization = "Bearer $AccessToken"
    }

    $parameters = @{
        Uri = "$apiBaseUrl$Path"
        Method = $Method
        Headers = $headers
        UseBasicParsing = $true
        TimeoutSec = 15
    }
    if ($PSBoundParameters.ContainsKey("Body")) {
        $parameters.ContentType = "application/json"
        $parameters.Body = ConvertTo-Json $Body -Compress
    }

    $content = $null
    try {
        $response = Invoke-WebRequest @parameters
        $statusCode = [int]$response.StatusCode
        $content = $response.Content
    }
    catch {
        if ($null -eq $_.Exception.Response) {
            throw
        }

        $statusCode = [int]$_.Exception.Response.StatusCode
        $content = if ($null -eq $_.ErrorDetails) {
            $null
        }
        else {
            $_.ErrorDetails.Message
        }
    }

    if ($ExpectedStatus -notcontains $statusCode) {
        throw "HTTP $Method $Path returned $statusCode; expected $($ExpectedStatus -join ', '). Body: $content"
    }

    $parsedBody = if ([string]::IsNullOrWhiteSpace($content)) {
        $null
    }
    else {
        $content | ConvertFrom-Json
    }

    return [pscustomobject]@{
        StatusCode = $statusCode
        Body = $parsedBody
    }
}

function Wait-ForApiReady {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastFailure = "No response received."
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest `
                -UseBasicParsing `
                -Uri "${apiBaseUrl}health/ready" `
                -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            $lastFailure = $_.Exception.Message
        }

        Start-Sleep -Seconds 1
    }

    throw "API did not become ready within $TimeoutSeconds seconds. Last failure: $lastFailure"
}

function Wait-ForResult {
    param(
        [Parameter(Mandatory)][scriptblock]$Probe,
        [Parameter(Mandatory)][scriptblock]$Satisfied,
        [Parameter(Mandatory)][string]$Description
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastFailure = "The condition has not been observed."
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $value = & $Probe
            if (& $Satisfied $value) {
                return $value
            }
        }
        catch {
            $lastFailure = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for $Description. Last failure: $lastFailure"
}

function Get-OwnedAgent {
    param(
        [Parameter(Mandatory)][string]$AccessToken,
        [Parameter(Mandatory)][string]$Name
    )

    $response = Invoke-ApiRequest `
        -Method GET `
        -Path "api/agents" `
        -ExpectedStatus 200 `
        -AccessToken $AccessToken
    return @($response.Body) |
        Where-Object { $_.Name -eq $Name } |
        Select-Object -First 1
}

function Get-ServerInstance {
    param(
        [Parameter(Mandatory)][string]$AccessToken,
        [Parameter(Mandatory)][string]$ServerInstanceId
    )

    return (Invoke-ApiRequest `
        -Method GET `
        -Path "api/server-instances/$ServerInstanceId" `
        -ExpectedStatus 200 `
        -AccessToken $AccessToken).Body
}

function Get-CommandHistory {
    param(
        [Parameter(Mandatory)][string]$AccessToken,
        [Parameter(Mandatory)][string]$ServerInstanceId
    )

    return (Invoke-ApiRequest `
        -Method GET `
        -Path "api/server-instances/$ServerInstanceId/commands?limit=20" `
        -ExpectedStatus 200 `
        -AccessToken $AccessToken).Body
}

function Wait-ForCompletedCommand {
    param(
        [Parameter(Mandatory)][string]$AccessToken,
        [Parameter(Mandatory)][string]$ServerInstanceId,
        [Parameter(Mandatory)][string]$CommandId
    )

    return Wait-ForResult `
        -Description "ServerCommand $CommandId to complete" `
        -Probe {
            $history = Get-CommandHistory `
                -AccessToken $AccessToken `
                -ServerInstanceId $ServerInstanceId
            return @($history.Items) |
                Where-Object { $_.Id -eq $CommandId } |
                Select-Object -First 1
        } `
        -Satisfied {
            param($command)
            if ($null -ne $command -and $command.Status -eq "Failed") {
                throw "ServerCommand $CommandId failed with $($command.ErrorCode)."
            }

            return $null -ne $command -and $command.Status -eq "Completed"
        }
}

function Start-TestAgent {
    param([AllowNull()][string]$InstallationToken)

    $script:agentRunNumber++
    $standardOutput = Join-Path $runDirectory "agent-$script:agentRunNumber.stdout.log"
    $standardError = Join-Path $runDirectory "agent-$script:agentRunNumber.stderr.log"
    $previousValues = @{}
    $configuration = @{
        Agent__ApiBaseUrl = $apiBaseUrl
        Agent__Name = $agentName
        Agent__InstallationToken = $InstallationToken
        Agent__HeartbeatIntervalSeconds = "1"
        Agent__CommandPollingIntervalSeconds = "1"
        Agent__ProcessReconciliationIntervalSeconds = "1"
    }

    try {
        foreach ($entry in $configuration.GetEnumerator()) {
            $previousValues[$entry.Key] = [Environment]::GetEnvironmentVariable(
                $entry.Key,
                [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable(
                $entry.Key,
                $entry.Value,
                [EnvironmentVariableTarget]::Process)
        }

        return Start-Process `
            -FilePath $agentExecutable `
            -WorkingDirectory $agentOutputDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput $standardOutput `
            -RedirectStandardError $standardError `
            -PassThru
    }
    finally {
        foreach ($entry in $previousValues.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                $entry.Key,
                $entry.Value,
                [EnvironmentVariableTarget]::Process)
        }
    }
}

function Stop-TestAgent {
    param([AllowNull()][System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force
            $null = $Process.WaitForExit(10000)
        }
    }
    catch [System.InvalidOperationException] {
        # The process already exited while cleanup was starting.
    }
    finally {
        $Process.Dispose()
    }
}

function Stop-TestFixture {
    param(
        [AllowNull()][object]$ProcessId,
        [Parameter(Mandatory)][string]$ExpectedExecutablePath
    )

    if ($null -eq $ProcessId) {
        return
    }

    $resolvedProcessId = [int]$ProcessId

    try {
        $process = Get-Process -Id $resolvedProcessId -ErrorAction Stop
        try {
            if ([string]::Equals(
                    $process.Path,
                    $ExpectedExecutablePath,
                    [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $process.Id -Force
                $null = $process.WaitForExit(10000)
            }
        }
        finally {
            $process.Dispose()
        }
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
        # The fixture already exited or was stopped through the API.
    }
}

$isWindowsPlatform = $PSVersionTable.PSEdition -eq "Desktop" -or
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows)
if (-not $isWindowsPlatform) {
    throw "The full MVP E2E verification requires Windows because the Agent uses DPAPI."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$agentOutputDirectory = Join-Path $repositoryRoot "src/ServerPilot.Agent/bin/Release/net10.0"
$agentExecutable = Join-Path $agentOutputDirectory "ServerPilot.Agent.exe"
$fixtureOutputDirectory = Join-Path $repositoryRoot "tests/ServerPilot.ProcessFixture/bin/Release/net10.0"
$fixtureExecutable = Join-Path $fixtureOutputDirectory "ServerPilot.ProcessFixture.exe"
$credentialDirectory = Join-Path (
    [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "ServerPilot"
$credentialPath = Join-Path $credentialDirectory "agent-credential.dat"
if (Test-Path -LiteralPath $credentialPath) {
    throw "E2E verification will not replace the existing Agent credential at '$credentialPath'. Use a disposable Windows user profile."
}

$projectName = "serverpilot-e2e-$([Guid]::NewGuid().ToString('N'))"
$runDirectory = Join-Path ([IO.Path]::GetTempPath()) $projectName
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$apiPort = Get-FreeTcpPort
do {
    $postgresPort = Get-FreeTcpPort
}
while ($postgresPort -eq $apiPort)
$apiBaseUrl = "http://127.0.0.1:$apiPort/"
$agentName = "ServerPilot E2E $([Guid]::NewGuid().ToString('N'))"
$ownerEmail = "owner-$([Guid]::NewGuid().ToString('N'))@example.test"
$otherEmail = "other-$([Guid]::NewGuid().ToString('N'))@example.test"
$ownerPassword = "E2e!$([Guid]::NewGuid().ToString('N'))"
$otherPassword = "E2e!$([Guid]::NewGuid().ToString('N'))"
$agentProcess = $null
$fixtureProcessId = $null
$agentRunNumber = 0
$credentialCreatedByTest = $false
$succeeded = $false

$environmentNames = @(
    "POSTGRES_PASSWORD",
    "JWT_SIGNING_KEY",
    "SERVERPILOT_API_HOST_PORT",
    "SERVERPILOT_POSTGRES_HOST_PORT"
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable(
        $name,
        [EnvironmentVariableTarget]::Process)
}

try {
    [Environment]::SetEnvironmentVariable(
        "POSTGRES_PASSWORD",
        (New-RandomSecret -ByteCount 24),
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "JWT_SIGNING_KEY",
        (New-RandomSecret -ByteCount 48),
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "SERVERPILOT_API_HOST_PORT",
        $apiPort.ToString([Globalization.CultureInfo]::InvariantCulture),
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "SERVERPILOT_POSTGRES_HOST_PORT",
        $postgresPort.ToString([Globalization.CultureInfo]::InvariantCulture),
        [EnvironmentVariableTarget]::Process)

    dotnet build ServerPilot.slnx --configuration Release
    Assert-NativeCommandSucceeded "dotnet build"
    Assert-Condition (Test-Path -LiteralPath $agentExecutable) "Agent executable was not built."
    Assert-Condition (Test-Path -LiteralPath $fixtureExecutable) "Process fixture was not built."

    Invoke-Compose up --detach --build
    Wait-ForApiReady

    $registeredOwner = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/auth/register" `
        -ExpectedStatus 201 `
        -Body @{ Email = $ownerEmail; Password = $ownerPassword }).Body
    $owner = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/auth/login" `
        -ExpectedStatus 200 `
        -Body @{ Email = $ownerEmail; Password = $ownerPassword }).Body
    Assert-Condition `
        ($registeredOwner.UserId -eq $owner.UserId) `
        "Registration and login returned different users."

    $installationToken = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/agent-installation-tokens" `
        -ExpectedStatus 201 `
        -AccessToken $owner.AccessToken).Body.Token
    $agentProcess = Start-TestAgent -InstallationToken $installationToken
    $agent = Wait-ForResult `
        -Description "Agent registration and Online heartbeat" `
        -Probe { Get-OwnedAgent -AccessToken $owner.AccessToken -Name $agentName } `
        -Satisfied {
            param($value)
            return $null -ne $value -and $value.Status -eq "Online"
        }
    Assert-Condition (Test-Path -LiteralPath $credentialPath) "Agent credential was not persisted."
    $credentialCreatedByTest = $true

    $server = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/server-instances" `
        -ExpectedStatus 201 `
        -AccessToken $owner.AccessToken `
        -Body @{
            AgentId = $agent.Id
            Name = "Harmless E2E process"
            ExecutablePath = $fixtureExecutable
            Arguments = ""
            WorkingDirectory = $fixtureOutputDirectory
            ProcessName = "ServerPilot.ProcessFixture"
        }).Body

    $firstStart = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/server-instances/$($server.Id)/commands/start" `
        -ExpectedStatus 201 `
        -AccessToken $owner.AccessToken).Body
    Wait-ForCompletedCommand `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id `
        -CommandId $firstStart.Id | Out-Null
    $running = Wait-ForResult `
        -Description "ServerInstance Running state with a PID" `
        -Probe { Get-ServerInstance -AccessToken $owner.AccessToken -ServerInstanceId $server.Id } `
        -Satisfied {
            param($value)
            return $value.Status -eq "Running" -and $null -ne $value.LastProcessId
        }
    $fixtureProcessId = [int]$running.LastProcessId
    Assert-Condition ($null -ne (Get-Process -Id $fixtureProcessId -ErrorAction SilentlyContinue)) `
        "The reported process ID is not running."

    $secondStart = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/server-instances/$($server.Id)/commands/start" `
        -ExpectedStatus 201 `
        -AccessToken $owner.AccessToken).Body
    Wait-ForCompletedCommand `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id `
        -CommandId $secondStart.Id | Out-Null
    $afterRepeatedStart = Get-ServerInstance `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id
    Assert-Condition `
        ($afterRepeatedStart.LastProcessId -eq $fixtureProcessId) `
        "Repeated StartServer changed the process ID."

    $beforeAgentRestartReport = [DateTimeOffset]$afterRepeatedStart.LastStatusReportedAt
    Stop-TestAgent -Process $agentProcess
    $agentProcess = $null
    Assert-Condition ($null -ne (Get-Process -Id $fixtureProcessId -ErrorAction SilentlyContinue)) `
        "The managed process did not survive the Agent restart."
    $agentProcess = Start-TestAgent -InstallationToken $null
    $afterAgentRestart = Wait-ForResult `
        -Description "Agent restart process-state recovery" `
        -Probe { Get-ServerInstance -AccessToken $owner.AccessToken -ServerInstanceId $server.Id } `
        -Satisfied {
            param($value)
            return $value.Status -eq "Running" -and
                $value.LastProcessId -eq $fixtureProcessId -and
                [DateTimeOffset]$value.LastStatusReportedAt -gt $beforeAgentRestartReport
        }

    $beforeApiRestartHeartbeat = [DateTimeOffset](Get-OwnedAgent `
        -AccessToken $owner.AccessToken `
        -Name $agentName).LastSeenAt
    Invoke-Compose stop api
    Start-Sleep -Seconds 10
    Assert-Condition (-not $agentProcess.HasExited) `
        "Agent exited during temporary API unavailability."
    Assert-Condition ($null -ne (Get-Process -Id $fixtureProcessId -ErrorAction SilentlyContinue)) `
        "The managed process exited during temporary API unavailability."
    Invoke-Compose start api
    Wait-ForApiReady
    Wait-ForResult `
        -Description "Agent heartbeat recovery after API restart" `
        -Probe { Get-OwnedAgent -AccessToken $owner.AccessToken -Name $agentName } `
        -Satisfied {
            param($value)
            return $null -ne $value -and
                $value.Status -eq "Online" -and
                [DateTimeOffset]$value.LastSeenAt -gt $beforeApiRestartHeartbeat
        } | Out-Null
    $afterApiRestart = Get-ServerInstance `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id
    Assert-Condition `
        ($afterApiRestart.Status -eq "Running" -and
            $afterApiRestart.LastProcessId -eq $fixtureProcessId) `
        "API restart did not preserve the Running process state."

    $firstStop = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/server-instances/$($server.Id)/commands/stop" `
        -ExpectedStatus 201 `
        -AccessToken $owner.AccessToken).Body
    Wait-ForCompletedCommand `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id `
        -CommandId $firstStop.Id | Out-Null
    Wait-ForResult `
        -Description "ServerInstance Stopped state" `
        -Probe { Get-ServerInstance -AccessToken $owner.AccessToken -ServerInstanceId $server.Id } `
        -Satisfied {
            param($value)
            return $value.Status -eq "Stopped" -and $null -eq $value.LastProcessId
        } | Out-Null
    Assert-Condition ($null -eq (Get-Process -Id $fixtureProcessId -ErrorAction SilentlyContinue)) `
        "The fixture process is still running after StopServer."

    $secondStop = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/server-instances/$($server.Id)/commands/stop" `
        -ExpectedStatus 201 `
        -AccessToken $owner.AccessToken).Body
    Wait-ForCompletedCommand `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id `
        -CommandId $secondStop.Id | Out-Null

    $history = Get-CommandHistory `
        -AccessToken $owner.AccessToken `
        -ServerInstanceId $server.Id
    $expectedCommandIds = @(
        $firstStart.Id,
        $secondStart.Id,
        $firstStop.Id,
        $secondStop.Id
    )
    foreach ($commandId in $expectedCommandIds) {
        $command = @($history.Items) |
            Where-Object { $_.Id -eq $commandId } |
            Select-Object -First 1
        Assert-Condition `
            ($null -ne $command -and $command.Status -eq "Completed") `
            "Command history is missing completed command $commandId."
        $correlationId = [Guid]::Empty
        Assert-Condition `
            ([Guid]::TryParse([string]$command.CorrelationId, [ref]$correlationId) -and
                $correlationId -ne [Guid]::Empty) `
            "Command $commandId has no valid correlation ID."
    }

    $registeredOther = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/auth/register" `
        -ExpectedStatus 201 `
        -Body @{ Email = $otherEmail; Password = $otherPassword }).Body
    $other = (Invoke-ApiRequest `
        -Method POST `
        -Path "api/auth/login" `
        -ExpectedStatus 200 `
        -Body @{ Email = $otherEmail; Password = $otherPassword }).Body
    Assert-Condition `
        ($registeredOther.UserId -eq $other.UserId) `
        "Second-user registration and login returned different users."
    Invoke-ApiRequest `
        -Method GET `
        -Path "api/agents/$($agent.Id)" `
        -ExpectedStatus 404 `
        -AccessToken $other.AccessToken | Out-Null
    Invoke-ApiRequest `
        -Method GET `
        -Path "api/server-instances/$($server.Id)" `
        -ExpectedStatus 404 `
        -AccessToken $other.AccessToken | Out-Null
    Invoke-ApiRequest `
        -Method POST `
        -Path "api/server-instances/$($server.Id)/commands/start" `
        -ExpectedStatus 404 `
        -AccessToken $other.AccessToken | Out-Null
    Invoke-ApiRequest `
        -Method GET `
        -Path "api/server-instances/$($server.Id)/commands?limit=20" `
        -ExpectedStatus 404 `
        -AccessToken $other.AccessToken | Out-Null

    $succeeded = $true
    Write-Host "ServerPilot full MVP E2E verification completed successfully."
    Write-Host "AgentId: $($agent.Id)"
    Write-Host "ServerInstanceId: $($server.Id)"
    Write-Host "Verified process ID: $fixtureProcessId"
    Write-Host "Completed commands: $($expectedCommandIds -join ', ')"
}
catch {
    Write-Warning "E2E logs were retained at '$runDirectory'."
    & docker compose --project-name $projectName logs --no-color
    throw
}
finally {
    Stop-TestAgent -Process $agentProcess
    Stop-TestFixture -ProcessId $fixtureProcessId -ExpectedExecutablePath $fixtureExecutable
    & docker compose --project-name $projectName down --volumes --remove-orphans

    if ($credentialCreatedByTest -and (Test-Path -LiteralPath $credentialPath)) {
        Remove-Item -LiteralPath $credentialPath -Force
    }

    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            [EnvironmentVariableTarget]::Process)
    }

    if ($succeeded) {
        Get-ChildItem -LiteralPath $runDirectory -File | Remove-Item -Force
        Remove-Item -LiteralPath $runDirectory -Force
    }
}
