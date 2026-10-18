param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$CreateInstaller,
    [string]$InnoCompiler = '',
    [string]$DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $DotNet publish src/FieldTrader/FieldTrader.csproj -c $Configuration -r win-x64 --self-contained true '-p:PublishDir=../../artifacts/publish/'
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    if ($CreateInstaller) {
        if (-not $InnoCompiler) {
            $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($command) { $InnoCompiler = $command.Source }
            else { $InnoCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
        }
        if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Install Inno Setup 6 or pass -InnoCompiler.' }
        & $InnoCompiler /Q installer/FieldTrader.iss
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
    Write-Output 'Published files: artifacts/publish'
}
finally { Pop-Location }
