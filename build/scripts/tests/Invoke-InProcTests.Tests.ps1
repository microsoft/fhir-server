BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Invoke-InProcTests.ps1'

    # Lays out a checkout like the pipeline agent's: the source tree with the web project's launchSettings.json,
    # plus the extracted E2E test application under _e2e/<version>. Returns the path of the test dll.
    function New-AgentLayout {
        param([string[]] $Omit = @())
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $web = Join-Path $root 'src' 'Microsoft.Health.Fhir.R4.Web'
        $tests = Join-Path $root '_e2e' 'R4'
        $null = New-Item -ItemType Directory -Path (Join-Path $web 'Properties'), $tests
        $files = @{
            'launchSettings.json'      = Join-Path $web 'Properties' 'launchSettings.json'
            'testauthenvironment.json' = Join-Path $tests 'testauthenvironment.json'
            'testconfiguration.json'   = Join-Path $tests 'testconfiguration.json'
        }

        Set-Content -Path (Join-Path $web 'Microsoft.Health.Fhir.R4.Web.csproj') -Value '<Project />'
        foreach ($name in $files.Keys) {
            if ($Omit -notcontains $name) {
                Set-Content -Path $files[$name] -Value '{}'
            }
        }

        $dll = Join-Path $tests 'Microsoft.Health.Fhir.R4.Tests.E2E.dll'
        Set-Content -Path $dll -Value ''
        return $dll
    }

    # A stand-in for the dotnet host, run as a separate process like the real one: it records the arguments it
    # received and the names of the environment variables it inherited, then exits with $env:FAKE_DOTNET_EXIT.
    function New-FakeDotnet {
        $directory = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $directory
        $body = @'
@{
    Arguments = @($args)
    EnvironmentNames = @([Environment]::GetEnvironmentVariables().Keys | ForEach-Object { [string] $_ })
} | ConvertTo-Json | Set-Content -Path $env:FAKE_DOTNET_OUT
exit [int] $env:FAKE_DOTNET_EXIT
'@
        if ($IsWindows) {
            Set-Content -Path (Join-Path $directory 'fake-dotnet.ps1') -Value $body
            $path = Join-Path $directory 'fake-dotnet.cmd'
            Set-Content -Path $path -Value "@`"$((Get-Process -Id $PID).Path)`" -NoProfile -NonInteractive -File `"%~dp0fake-dotnet.ps1`" %*`r`n@exit /b %ERRORLEVEL%"
        }
        else {
            $path = Join-Path $directory 'fake-dotnet'
            Set-Content -Path $path -Value ("#!/usr/bin/env pwsh`n" + $body)
            & chmod +x $path
        }

        return $path
    }
}

Describe 'Invoke-InProcTests' {
    BeforeEach {
        $script:SavedEnvironment = [Environment]::GetEnvironmentVariables()
        $env:FAKE_DOTNET_OUT = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        $env:FAKE_DOTNET_EXIT = '3'
    }

    AfterEach {
        # The script strips variables from this process; put the environment back exactly as it was.
        foreach ($name in @([Environment]::GetEnvironmentVariables().Keys)) {
            if (-not $script:SavedEnvironment.Contains($name)) {
                Remove-Item -LiteralPath "env:$name"
            }
        }

        foreach ($name in $script:SavedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $script:SavedEnvironment[$name])
        }
    }

    It 'removes TestEnvironmentUrl variables regardless of case' {
        $remote = @('TestEnvironmentUrl', 'TESTENVIRONMENTURL_R4_SQL', 'testenvironmenturl_R5', 'TestTokenEndpoint', 'resource')
        $kept = @('TestEnvironmentName', 'ResourceGroupName', 'XTestEnvironmentUrl')
        foreach ($name in $remote + $kept) {
            [Environment]::SetEnvironmentVariable($name, '$(TestEnvironmentUrl)')
        }

        $dll = New-AgentLayout
        $null = & $script:ScriptPath -TestDll $dll -Arguments '--filter-trait', 'DataStore=SqlServer' -Dotnet (New-FakeDotnet) *>&1
        $exitCode = $LASTEXITCODE

        $exitCode | Should -Be 3
        $received = Get-Content -Raw $env:FAKE_DOTNET_OUT | ConvertFrom-Json
        $received.Arguments | Should -Be @($dll, '--filter-trait', 'DataStore=SqlServer', '--timeout', '14m')
        @($received.EnvironmentNames | Where-Object { $_ -match '^(TESTENVIRONMENTURL.*|TESTTOKENENDPOINT|RESOURCE)$' }) | Should -BeNullOrEmpty
        foreach ($name in $kept) {
            $received.EnvironmentNames | Should -Contain $name
        }
    }

    It 'fails when a required packaged file is missing' -ForEach @(
        @{ Missing = 'launchSettings.json' }
        @{ Missing = 'testauthenvironment.json' }
        @{ Missing = 'testconfiguration.json' }
    ) {
        $dll = New-AgentLayout -Omit $Missing

        { & $script:ScriptPath -TestDll $dll -Dotnet (New-FakeDotnet) } | Should -Throw "*$Missing*"
        Test-Path $env:FAKE_DOTNET_OUT | Should -BeFalse
    }
}
