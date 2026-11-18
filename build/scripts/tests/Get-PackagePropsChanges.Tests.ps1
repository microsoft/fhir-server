BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Get-PackagePropsChanges.ps1'

    # Directory.Packages.props at the head commit.
    $script:Content = @(
        '<Project>',
        '  <PropertyGroup>',
        '    <OpenIddictPackageVersion>7.8.0</OpenIddictPackageVersion>',
        '    <SomeToolVersion>1.0.0</SomeToolVersion>',
        '  </PropertyGroup>',
        '  <ItemGroup>',
        '    <PackageVersion Include="OpenIddict.AspNetCore" Version="$(OpenIddictPackageVersion)" />',
        '    <PackageVersion Include="OpenIddict.Core" Version="$(OpenIddictPackageVersion)" />',
        '    <PackageVersion Include="xunit.v3" Version="2.0.0" />',
        '  </ItemGroup>',
        '</Project>')
}

Describe 'Get-PackagePropsChanges' {
    It 'returns the names on added and removed PackageVersion lines, once each' {
        $diff = @(
            'diff --git a/Directory.Packages.props b/Directory.Packages.props',
            '--- a/Directory.Packages.props',
            '+++ b/Directory.Packages.props',
            '@@ -10 +10 @@',
            '-    <PackageVersion Include="Microsoft.Azure.Cosmos" Version="3.40.0" />',
            '+    <PackageVersion Include="Microsoft.Azure.Cosmos" Version="3.41.0" />',
            '@@ -20,0 +21 @@',
            '+    <PackageVersion Include="xunit.v3" Version="2.0.0" />')

        $changes = & $script:ScriptPath -DiffLines $diff -Content $script:Content
        $changes.Packages | Should -Be @('Microsoft.Azure.Cosmos', 'xunit.v3')
        $changes.Properties | Should -BeNullOrEmpty
    }

    It 'maps a changed version property to the packages that reference it' {
        $diff = @(
            '-    <OpenIddictPackageVersion>7.7.0</OpenIddictPackageVersion>',
            '+    <OpenIddictPackageVersion>7.8.0</OpenIddictPackageVersion>')

        $changes = & $script:ScriptPath -DiffLines $diff -Content $script:Content
        $changes.Packages | Should -Be @('OpenIddict.AspNetCore', 'OpenIddict.Core')
        $changes.Properties | Should -Be @('OpenIddictPackageVersion')
    }

    It 'returns an unreferenced property with no packages' {
        $diff = @('+    <SomeToolVersion>1.0.0</SomeToolVersion>')

        $changes = & $script:ScriptPath -DiffLines $diff -Content $script:Content
        $changes.Packages | Should -BeNullOrEmpty
        $changes.Properties | Should -Be @('SomeToolVersion')
    }

    It 'ignores unchanged context lines' {
        $changes = & $script:ScriptPath -DiffLines @(' <PackageVersion Include="Azure.Identity" Version="1.0.0" />') -Content $script:Content
        $changes.Packages | Should -BeNullOrEmpty
        $changes.Properties | Should -BeNullOrEmpty
    }
}
