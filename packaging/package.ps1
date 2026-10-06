param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ServerRpm', 'ClientRpm', 'ClientMsi', 'ClientApkDebug', 'ClientTermuxDeb')]
    [string]$Target,
    [string]$NfpmPath,
    [string]$WixPath
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = ([xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$packages = Join-Path $repository "artifacts/packages/$version"
$scratch = Join-Path $repository 'temp/packaging'
New-Item -ItemType Directory -Path $packages, $scratch -Force | Out-Null

function Assert-Payload([string]$path, [string]$executable) {
    if (-not (Test-Path -LiteralPath (Join-Path $path $executable) -PathType Leaf)) {
        throw "Publish the matching runtime first: $path"
    }
    $unexpected = Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object {
        $_.Name -eq 'workspace-access.config' -or $_.Extension -in @('.key', '.pem', '.pfx') -or
        $_.Name -like '*Tests*'
    }
    if ($unexpected) { throw 'Publish payload contains a private configuration, key, or test assembly.' }
}

function Add-PackageFile([System.Collections.Generic.List[string]]$lines,
    [string]$source, [string]$destination, [string]$mode = '0644') {
    $normalized = $source.Replace('\', '/')
    $lines.Add('  - src: ' + (ConvertTo-Json -InputObject $normalized -Compress))
    $lines.Add('    dst: ' + (ConvertTo-Json -InputObject $destination -Compress))
    $lines.Add('    file_info:')
    $lines.Add("      mode: $mode")
}

function Normalize-TermuxDebArchitecture([string]$path) {
    $ar = (Get-Command ar -ErrorAction SilentlyContinue).Source
    if (-not $ar) {
        throw 'The Termux DEB post-processing step requires ar.'
    }

    $work = Join-Path $scratch 'termux-deb-architecture'
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $work | Out-Null
    Push-Location $work
    try {
        & $ar x $path
        if ($LASTEXITCODE -ne 0) { throw 'Failed to unpack the Termux DEB archive.' }

        $controlDirectory = Join-Path $work 'control'
        New-Item -ItemType Directory -Path $controlDirectory | Out-Null
        $controlInput = [IO.File]::OpenRead((Join-Path $work 'control.tar.gz'))
        $controlGzip = [IO.Compression.GZipStream]::new($controlInput, [IO.Compression.CompressionMode]::Decompress)
        try {
            [System.Formats.Tar.TarFile]::ExtractToDirectory($controlGzip, $controlDirectory, $true)
        }
        finally {
            $controlGzip.Dispose()
            $controlInput.Dispose()
        }

        $controlPath = Join-Path $controlDirectory 'control'
        $control = [IO.File]::ReadAllText($controlPath)
        if (-not $control.Contains("Architecture: arm64")) {
            throw 'The generated DEB does not contain the expected arm64 architecture field.'
        }
        [IO.File]::WriteAllText($controlPath, $control.Replace('Architecture: arm64', 'Architecture: aarch64'))
        Remove-Item -LiteralPath (Join-Path $work 'control.tar.gz') -Force
        $controlOutput = [IO.File]::Create((Join-Path $work 'control.tar.gz'))
        $controlGzip = [IO.Compression.GZipStream]::new($controlOutput, [IO.Compression.CompressionLevel]::SmallestSize)
        try {
            [System.Formats.Tar.TarFile]::CreateFromDirectory($controlDirectory, $controlGzip, $false)
        }
        finally {
            $controlGzip.Dispose()
            $controlOutput.Dispose()
        }

        $rebuilt = Join-Path $work 'rebuilt.deb'
        & $ar r $rebuilt 'debian-binary' 'control.tar.gz' 'data.tar.gz'
        if ($LASTEXITCODE -ne 0) { throw 'Failed to rebuild the Termux DEB archive.' }
        Move-Item -LiteralPath $rebuilt -Destination $path -Force
    }
    finally {
        Pop-Location
    }
}

if ($Target -eq 'ClientApkDebug') {
    $source = Join-Path $repository 'temp/CharacAndroid/bin/Debug/net10.0-android/com.nitload.charac-Signed.apk'
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw 'Build src/android/CharacAndroid.csproj in Debug first.'
    }
    $output = Join-Path $packages "charac-client_${version}_android-debug.apk"
    [IO.File]::Copy($source, $output, $true)
    Write-Output $output
    exit 0
}

if ($Target -eq 'ClientMsi') {
    $payload = Join-Path $repository 'artifacts/publish/win-x64/client'
    Assert-Payload $payload 'charac.exe'
    if (-not $WixPath) { $WixPath = Join-Path $repository 'temp/tools/wix.exe' }
    if (-not (Test-Path -LiteralPath $WixPath -PathType Leaf)) { throw 'WiX CLI 6.0.2 is required; pass -WixPath.' }
    $output = Join-Path $packages "charac-client_${version}_win-x64.msi"
    & $WixPath build '-arch' 'x64' '-d' "PackageVersion=$version" '-d' "PayloadDir=$payload" '-d' "ConfigTemplatePath=$(Join-Path $repository 'client.config.example')" '-d' "LicensePath=$(Join-Path $repository 'LICENSE')" '-pdbtype' 'none' '-o' $output (Join-Path $PSScriptRoot 'windows/Client.wxs')
    if ($LASTEXITCODE -ne 0) { throw "WiX failed with exit code $LASTEXITCODE." }
    Write-Output $output
    exit 0
}

if ($Target -eq 'ClientTermuxDeb') {
    $payload = Join-Path $repository 'artifacts/publish/linux-bionic-arm64/client'
    Assert-Payload $payload 'charac.dll'
    if (-not $NfpmPath) { $NfpmPath = Join-Path $repository 'temp/tools/nfpm/nfpm.exe' }
    if (-not (Test-Path -LiteralPath $NfpmPath -PathType Leaf)) { throw 'nFPM 2.47.0 is required; pass -NfpmPath.' }
    $prefix = '/data/data/com.termux/files/usr'
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('name: charac-termux')
    $lines.Add("version: $version")
    $lines.Add('release: 2')
    $lines.Add('arch: aarch64')
    $lines.Add('platform: linux')
    $lines.Add('maintainer: nitload')
    $lines.Add('vendor: nitload')
    $lines.Add('license: MIT AND OFL-1.1')
    $lines.Add('description: Charac CLI for Termux')
    $lines.Add('depends:')
    $lines.Add('  - dotnet-runtime-10.0')
    $lines.Add('contents:')
    foreach ($file in (Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName)) {
        $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\', '/')
        if ($file.Extension -eq '.pdb' -or $relative.StartsWith('WorkspaceAccessClient', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $mode = if ($relative -eq 'charac') { '0755' } else { '0644' }
        Add-PackageFile $lines $file.FullName "$prefix/opt/charac/$relative" $mode
    }
    Add-PackageFile $lines (Join-Path $PSScriptRoot 'termux/charac') "$prefix/bin/charac" '0755'
    Add-PackageFile $lines (Join-Path $repository 'LICENSE') "$prefix/share/doc/charac/LICENSE"
    Add-PackageFile $lines (Join-Path $repository 'src/client/Assets/Fonts/IBM-Plex-LICENSE.txt') "$prefix/share/doc/charac/IBM-Plex-LICENSE.txt"
    $config = Join-Path $scratch 'charac-termux.yaml'
    [IO.File]::WriteAllLines($config, [string[]]$lines)
    $output = Join-Path $packages "charac-termux_${version}-2_aarch64.deb"
    & $NfpmPath package --config $config --packager deb --target $output
    if ($LASTEXITCODE -ne 0) { throw "nFPM failed with exit code $LASTEXITCODE." }
    Normalize-TermuxDebArchitecture $output
    Write-Output $output
    exit 0
}

$kind = if ($Target -eq 'ServerRpm') { 'server' } else { 'client' }
$executable = if ($kind -eq 'server') { 'char_rac_server' } else { 'charac' }
$payload = Join-Path $repository "artifacts/publish/linux-x64/$kind"
Assert-Payload $payload $executable
if (-not $NfpmPath) { $NfpmPath = Join-Path $repository 'temp/tools/nfpm/nfpm.exe' }
if (-not (Test-Path -LiteralPath $NfpmPath -PathType Leaf)) { throw 'nFPM 2.47.0 is required; pass -NfpmPath.' }

$name = "charac-$kind"
$release = if ($kind -eq 'server') { '3' } else { '2' }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("name: $name")
$lines.Add("version: $version")
$lines.Add("release: $release")
$lines.Add('arch: amd64')
$lines.Add('platform: linux')
$lines.Add('maintainer: nitload')
$lines.Add('vendor: nitload')
$lines.Add($(if ($kind -eq 'server') { 'license: MIT' } else { 'license: MIT AND OFL-1.1' }))
$lines.Add("description: Charac $kind")
if ($kind -eq 'server') {
    $lines.Add('depends:')
    $lines.Add('  - systemd')
    $lines.Add('scripts:')
    $lines.Add('  postinstall: ' + (ConvertTo-Json -InputObject (Join-Path $PSScriptRoot 'linux/server-postinstall.sh') -Compress))
    $lines.Add('  postremove: ' + (ConvertTo-Json -InputObject (Join-Path $PSScriptRoot 'linux/server-postremove.sh') -Compress))
}
$lines.Add('contents:')
$licenseDirectory = "/usr/share/licenses/$name"
Add-PackageFile $lines (Join-Path $repository 'LICENSE') "$licenseDirectory/LICENSE"
if ($kind -eq 'client') {
    Add-PackageFile $lines (Join-Path $repository 'src/client/Assets/Fonts/IBM-Plex-LICENSE.txt') "$licenseDirectory/IBM-Plex-LICENSE.txt"
}
$files = Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\', '/')
    if ($file.Extension -eq '.pdb' -or $relative.StartsWith('deploy/', [StringComparison]::OrdinalIgnoreCase)) { continue }
    $mode = if ($relative -eq $executable) { '0755' } else { '0644' }
    Add-PackageFile $lines $file.FullName "/opt/charac/$kind/$relative" $mode
}
if ($kind -eq 'server') {
    Add-PackageFile $lines (Join-Path $PSScriptRoot 'linux/charac-server.service') '/usr/lib/systemd/system/charac-server.service'
    Add-PackageFile $lines (Join-Path $PSScriptRoot 'linux/charac.sysusers') '/usr/lib/sysusers.d/charac.conf'
    Add-PackageFile $lines (Join-Path $repository 'workspace-access.config.example') '/usr/share/doc/charac-server/workspace-access.config.example'
} else {
    Add-PackageFile $lines (Join-Path $repository 'client.config.example') '/usr/share/doc/charac-client/client.config.example'
    # The self-contained Client is already named charac; link it into PATH.
    $lines.Add('  - src: /opt/charac/client/charac')
    $lines.Add('    dst: /usr/bin/charac')
    $lines.Add('    type: symlink')
}
$config = Join-Path $scratch "$name.yaml"
[IO.File]::WriteAllLines($config, [string[]]$lines)
$output = Join-Path $packages "${name}-${version}-${release}.x86_64.rpm"
& $NfpmPath package --config $config --packager rpm --target $output
if ($LASTEXITCODE -ne 0) { throw "nFPM failed with exit code $LASTEXITCODE." }
Write-Output $output
