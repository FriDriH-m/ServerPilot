#Requires -Version 5.1
#Requires -RunAsAdministrator

param(
    [Parameter(Mandatory)]
    [ValidateSet("Install", "Update", "Start", "Stop", "Uninstall", "GrantPath")]
    [string]$Action,

    [string]$PackageDirectory = (Join-Path $PSScriptRoot "../app"),

    [string]$ApiBaseUrl = "",

    [string]$AgentName = "",

    [Security.SecureString]$InstallationToken,

    [string[]]$ManagedServerDirectory = @(),

    [ValidateRange(10, 300)]
    [int]$StartupTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$serviceName = "ServerPilot.Agent"
$displayName = "ServerPilot Agent"
$serviceAccount = "NT SERVICE\$serviceName"
$installDirectory = Join-Path $env:ProgramFiles "ServerPilot\Agent"
$dataDirectory = Join-Path $env:ProgramData "ServerPilot\Agent"
$configurationPath = Join-Path $dataDirectory "appsettings.json"
$credentialPath = Join-Path $dataDirectory "agent-credential.dat"
$executablePath = Join-Path $installDirectory "ServerPilot.Agent.exe"

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory)][string]$CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "Command '$CommandName' failed with exit code $LASTEXITCODE."
    }
}

function Get-InstalledService {
    Get-Service -Name $serviceName -ErrorAction SilentlyContinue
}

function Resolve-ServiceSid {
    $account = New-Object Security.Principal.NTAccount("NT SERVICE", $serviceName)
    $sid = $account.Translate([Security.Principal.SecurityIdentifier])
    $sid.Value
}

function Set-DirectoryAcl {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ServiceRights
    )

    $serviceSid = Resolve-ServiceSid
    & icacls.exe $Path /inheritance:r `
        /grant:r `
        "*S-1-5-18:(OI)(CI)(F)" `
        "*S-1-5-32-544:(OI)(CI)(F)" `
        "*$serviceSid`:(OI)(CI)($ServiceRights)" `
        /T /C | Out-Null
    Assert-NativeCommandSucceeded "icacls $Path"
}

function Assert-SafeManagedServerDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [IO.Path]::IsPathRooted($Path)) {
        throw "Managed server directory must be an absolute path: '$Path'."
    }

    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        throw "Managed server directory does not exist: '$resolvedPath'."
    }

    $pathRoot = [IO.Path]::GetPathRoot($resolvedPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ($resolvedPath.Equals($pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "A drive or share root cannot be granted to the Agent service."
    }

    $windowsDirectory = [IO.Path]::GetFullPath($env:WINDIR).TrimEnd(
        [IO.Path]::DirectorySeparatorChar)
    if ($resolvedPath.Equals($windowsDirectory, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPath.StartsWith(
            "$windowsDirectory$([IO.Path]::DirectorySeparatorChar)",
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Windows directory cannot be granted to the Agent service."
    }

    $resolvedPath
}

function Grant-ManagedServerDirectories {
    if ($ManagedServerDirectory.Count -eq 0) {
        throw "At least one -ManagedServerDirectory is required for GrantPath."
    }

    $serviceSid = Resolve-ServiceSid
    foreach ($path in $ManagedServerDirectory) {
        $resolvedPath = Assert-SafeManagedServerDirectory $path
        & icacls.exe $resolvedPath /grant:r "*$serviceSid`:(OI)(CI)(M)" /T /C | Out-Null
        Assert-NativeCommandSucceeded "icacls $resolvedPath"
        Write-Host "Granted the Agent service Modify access to '$resolvedPath'."
    }
}

function ConvertFrom-SecureToken {
    param([Parameter(Mandatory)][Security.SecureString]$SecureToken)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureToken)
    try {
        $plainText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($plainText)) {
            throw "Installation token must not be empty."
        }

        $plainText
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Write-JsonAtomically {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )

    $temporaryPath = Join-Path `
        ([IO.Path]::GetDirectoryName($Path)) `
        ".$([IO.Path]::GetFileName($Path)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 8
        [IO.File]::WriteAllText($temporaryPath, $json, (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Write-InitialConfiguration {
    if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
        throw "-ApiBaseUrl is required for a new installation."
    }

    $apiUri = $null
    if (-not [Uri]::TryCreate($ApiBaseUrl, [UriKind]::Absolute, [ref]$apiUri) -or
        ($apiUri.Scheme -ne [Uri]::UriSchemeHttp -and
            $apiUri.Scheme -ne [Uri]::UriSchemeHttps) -or
        -not [string]::IsNullOrEmpty($apiUri.Query) -or
        -not [string]::IsNullOrEmpty($apiUri.Fragment) -or
        ($apiUri.Scheme -eq [Uri]::UriSchemeHttp -and -not $apiUri.IsLoopback)) {
        throw "-ApiBaseUrl must be an absolute HTTPS URL without a query or fragment; HTTP is allowed only for loopback."
    }

    $trimmedName = $AgentName.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmedName) -or $trimmedName.Length -gt 100) {
        throw "-AgentName is required and must not exceed 100 characters."
    }

    if ($null -eq $InstallationToken) {
        throw "-InstallationToken is required for a new installation. Use Read-Host -AsSecureString."
    }

    $plainTextToken = ConvertFrom-SecureToken $InstallationToken
    try {
        $configuration = [ordered]@{
            Logging = [ordered]@{
                LogLevel = [ordered]@{
                    Default = "Information"
                    "Microsoft.Hosting.Lifetime" = "Information"
                }
                EventLog = [ordered]@{
                    SourceName = $serviceName
                    LogName = "Application"
                    LogLevel = [ordered]@{
                        Default = "Information"
                        Microsoft = "Warning"
                        "Microsoft.Hosting.Lifetime" = "Information"
                    }
                }
            }
            Agent = [ordered]@{
                ApiBaseUrl = $apiUri.AbsoluteUri
                Name = $trimmedName
                InstallationToken = $plainTextToken
                HeartbeatIntervalSeconds = 10
                CommandPollingIntervalSeconds = 5
                ProcessReconciliationIntervalSeconds = 10
            }
        }

        Write-JsonAtomically -Path $configurationPath -Value $configuration
    }
    finally {
        $plainTextToken = $null
    }
}

function Remove-InstallationTokenFromConfiguration {
    $configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
    if ($null -ne $configuration.Agent) {
        $configuration.Agent.PSObject.Properties.Remove("InstallationToken")
        Write-JsonAtomically -Path $configurationPath -Value $configuration
    }
}

function Stop-InstalledService {
    $service = Get-InstalledService
    if ($null -ne $service -and $service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }
}

function Start-InstalledService {
    $service = Get-InstalledService
    if ($null -eq $service) {
        throw "Service '$serviceName' is not installed."
    }

    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $serviceName
        $service.WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds($StartupTimeoutSeconds))
    }
}

function Assert-Package {
    $resolvedPackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
    $packageExecutable = Join-Path $resolvedPackageDirectory "ServerPilot.Agent.exe"
    $packageSettings = Join-Path $resolvedPackageDirectory "appsettings.json"
    if (-not (Test-Path -LiteralPath $packageExecutable -PathType Leaf) -or
        -not (Test-Path -LiteralPath $packageSettings -PathType Leaf)) {
        throw "Package directory must contain ServerPilot.Agent.exe and appsettings.json."
    }

    $resolvedPackageDirectory
}

function Copy-Package {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Install-AgentService {
    if ($null -ne (Get-InstalledService)) {
        throw "Service '$serviceName' is already installed. Use the Update action."
    }

    $resolvedPackageDirectory = Assert-Package
    Copy-Package -Source $resolvedPackageDirectory -Destination $installDirectory
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

    $binaryPath = "`"$executablePath`" --contentRoot `"$dataDirectory`""
    & sc.exe create $serviceName `
        binPath= $binaryPath `
        start= delayed-auto `
        obj= $serviceAccount `
        DisplayName= $displayName | Out-Null
    Assert-NativeCommandSucceeded "sc.exe create $serviceName"

    try {
        & sc.exe description $serviceName `
            "Runs the ServerPilot Agent and manages explicitly configured local server processes." | Out-Null
        Assert-NativeCommandSucceeded "sc.exe description $serviceName"
        & sc.exe sidtype $serviceName unrestricted | Out-Null
        Assert-NativeCommandSucceeded "sc.exe sidtype $serviceName"
        & sc.exe failure $serviceName `
            reset= 86400 `
            actions= restart/5000/restart/15000/restart/60000 | Out-Null
        Assert-NativeCommandSucceeded "sc.exe failure $serviceName"
        & sc.exe failureflag $serviceName 1 | Out-Null
        Assert-NativeCommandSucceeded "sc.exe failureflag $serviceName"

        Set-DirectoryAcl -Path $installDirectory -ServiceRights "RX"
        Set-DirectoryAcl -Path $dataDirectory -ServiceRights "M"

        $eventSourceRegistryPath =
            "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$serviceName"
        if (-not (Test-Path -LiteralPath $eventSourceRegistryPath)) {
            New-EventLog -LogName Application -Source $serviceName
        }

        $configurationExists = Test-Path -LiteralPath $configurationPath -PathType Leaf
        $credentialExists = Test-Path -LiteralPath $credentialPath -PathType Leaf
        if (-not $configurationExists -or
            (-not $credentialExists -and $null -ne $InstallationToken)) {
            Write-InitialConfiguration
        }

        if ($ManagedServerDirectory.Count -gt 0) {
            Grant-ManagedServerDirectories
        }

        Start-InstalledService

        if (-not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
            $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
            while ([DateTime]::UtcNow -lt $deadline -and
                -not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
                Start-Sleep -Seconds 1
            }
        }

        if (Test-Path -LiteralPath $credentialPath -PathType Leaf) {
            Remove-InstallationTokenFromConfiguration
        }
        else {
            throw "The service started but did not persist its credential within $StartupTimeoutSeconds seconds. The restricted configuration retains the one-time token for troubleshooting."
        }
    }
    catch {
        Stop-InstalledService
        & sc.exe delete $serviceName | Out-Null
        throw
    }

    Write-Host "Installed and started '$displayName'."
}

function Update-AgentService {
    $service = Get-InstalledService
    if ($null -eq $service) {
        throw "Service '$serviceName' is not installed."
    }

    $resolvedPackageDirectory = Assert-Package
    $wasRunning = $service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped
    $stagingDirectory = "$installDirectory.update.$([Guid]::NewGuid().ToString('N'))"
    $backupDirectory = "$installDirectory.backup.$([Guid]::NewGuid().ToString('N'))"
    $updateSucceeded = $false
    $originalMoved = $false
    try {
        Copy-Package -Source $resolvedPackageDirectory -Destination $stagingDirectory
        Stop-InstalledService
        Move-Item -LiteralPath $installDirectory -Destination $backupDirectory
        $originalMoved = $true
        Move-Item -LiteralPath $stagingDirectory -Destination $installDirectory
        Set-DirectoryAcl -Path $installDirectory -ServiceRights "RX"

        if ($wasRunning) {
            Start-InstalledService
        }
        $updateSucceeded = $true
    }
    catch {
        if ($originalMoved) {
            Stop-InstalledService
            if (Test-Path -LiteralPath $installDirectory) {
                Remove-Item -LiteralPath $installDirectory -Recurse -Force
            }
            if (Test-Path -LiteralPath $backupDirectory) {
                Move-Item -LiteralPath $backupDirectory -Destination $installDirectory
                Set-DirectoryAcl -Path $installDirectory -ServiceRights "RX"
                if ($wasRunning) {
                    Start-InstalledService
                }
            }
        }
        elseif ($wasRunning) {
            Start-InstalledService
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stagingDirectory) {
            Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
        }
    }

    if ($updateSucceeded -and (Test-Path -LiteralPath $backupDirectory)) {
        try {
            Remove-Item -LiteralPath $backupDirectory -Recurse -Force
        }
        catch {
            Write-Warning "Update succeeded, but old binaries remain in '$backupDirectory': $($_.Exception.Message)"
        }
    }

    Write-Host "Updated '$displayName'. Configuration and credential were preserved."
}

function Uninstall-AgentService {
    if ($null -eq (Get-InstalledService)) {
        throw "Service '$serviceName' is not installed."
    }

    Stop-InstalledService
    & sc.exe delete $serviceName | Out-Null
    Assert-NativeCommandSucceeded "sc.exe delete $serviceName"

    if (Test-Path -LiteralPath $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }

    Write-Host "Uninstalled '$displayName'. Preserved configuration and credential in '$dataDirectory'."
}

switch ($Action) {
    "Install" { Install-AgentService }
    "Update" { Update-AgentService }
    "Start" { Start-InstalledService }
    "Stop" { Stop-InstalledService }
    "Uninstall" { Uninstall-AgentService }
    "GrantPath" { Grant-ManagedServerDirectories }
}
