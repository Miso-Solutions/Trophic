param(
    [string]$PublishDir
)

$pub = $PublishDir.Trim('"', "'").TrimEnd('\', '/')
$lib = Join-Path $pub 'lib'
New-Item -ItemType Directory -Path $lib -Force | Out-Null

# Files that must stay in root regardless
$keepInRoot = @(
    'Trophic.exe',
    'Trophic.dll',
    'Trophic.deps.json',
    'Trophic.runtimeconfig.json',
    'System.Private.CoreLib.dll',
    'System.Runtime.dll'
)

function Test-ManagedAssembly($filePath) {
    try {
        [System.Reflection.AssemblyName]::GetAssemblyName($filePath) | Out-Null
        return $true
    } catch {
        return $false
    }
}

# Move only MANAGED DLLs (not native) to lib/, keep native DLLs in root
Get-ChildItem -Path $pub -File |
    Where-Object {
        $_.Extension -in '.dll', '.pdb' -and
        $keepInRoot -notcontains $_.Name -and
        (Test-ManagedAssembly $_.FullName)
    } |
    ForEach-Object { Move-Item $_.FullName $lib -Force }

# Also move non-essential native exes (diagnostics)
$moveExes = @('createdump.exe')
foreach ($f in $moveExes) {
    $src = Join-Path $pub $f
    if (Test-Path $src) { Move-Item $src $lib -Force }
}

# Move localization directories to lib/
$localeDirs = @('ar','cs','de','es','fr','he','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant')
foreach ($d in $localeDirs) {
    $src = Join-Path $pub $d
    $dst = Join-Path $lib $d
    if (Test-Path $src) {
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        Move-Item $src $dst
    }
}

# Strip managed assembly entries from deps.json so they're not on TPA.
# This lets the app's AssemblyResolve handler find them in lib/.
$depsFile = Join-Path $pub 'Trophic.deps.json'
$deps = Get-Content $depsFile -Raw | ConvertFrom-Json

$targetKey = '.NETCoreApp,Version=v8.0/win-x64'
$target = $deps.targets.$targetKey

foreach ($prop in @($target.PSObject.Properties)) {
    $val = $prop.Value
    if ($prop.Name -eq 'Trophic/1.0.0') { continue }
    if ($val.PSObject.Properties['runtime']) {
        $runtime = $val.runtime
        $toRemove = @()
        foreach ($asm in $runtime.PSObject.Properties) {
            $fileName = [System.IO.Path]::GetFileName($asm.Name)
            if ($keepInRoot -notcontains $fileName) {
                $toRemove += $asm.Name
            }
        }
        foreach ($name in $toRemove) {
            $runtime.PSObject.Properties.Remove($name)
        }
        if (@($runtime.PSObject.Properties).Count -eq 0) {
            $val.PSObject.Properties.Remove('runtime')
        }
    }
}

$deps | ConvertTo-Json -Depth 20 | Set-Content $depsFile -Encoding UTF8

# Remove PDB files (debug symbols — not needed for release, can trigger SmartScreen)
Get-ChildItem -Path $pub -Recurse -Filter '*.pdb' | Remove-Item -Force
