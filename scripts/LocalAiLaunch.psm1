function New-LocalAiSteamLaunch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RunDir,
        [Parameter(Mandatory)][string]$GameDir,
        [switch]$HostBridge
    )
    $ErrorActionPreference = 'Stop'
    $RunDir = (Resolve-Path -LiteralPath $RunDir).Path
    if (![System.IO.Path]::IsPathFullyQualified($RunDir) -or $RunDir.StartsWith('\\')) {
        throw 'The launch directory must be a local absolute path.'
    }
    $arguments = @('-applaunch', '2868840', '--log-file', ('"' + (Join-Path $RunDir 'game.log') + '"'))
    $bridge = $null
    if ($HostBridge) {
        $assembly = Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'
        $hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        try {
            $listener.Start()
            $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
        } finally { $listener.Stop() }
        if ($port -lt 1024) { throw 'The selected bridge port is outside the supported range.' }
        $tokenBytes = [byte[]]::new(16)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($tokenBytes)
        $config = [ordered]@{
            Port = $port
            Token = [Convert]::ToHexString($tokenBytes)
            ExpectedAssemblySha256 = $hash
        }
        $configPath = Join-Path $RunDir 'host-bridge.json'
        $jsonBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(($config | ConvertTo-Json -Compress))
        $file = [System.IO.File]::Open($configPath, 'CreateNew', 'Write', 'None')
        try { $file.Write($jsonBytes, 0, $jsonBytes.Length) } finally { $file.Dispose() }
        $arguments += @('--', ('"--local-ai-host-bridge-config=' + $configPath + '"'))
        $bridge = [ordered]@{ enabled = $true; configPath = $configPath; port = $port; expectedAssemblySha256 = $hash }
    }
    [pscustomobject]@{ Arguments = $arguments; HostBridge = $bridge }
}

Export-ModuleMember -Function New-LocalAiSteamLaunch
