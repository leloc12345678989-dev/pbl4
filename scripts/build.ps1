Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    dotnet restore .\pbl4.sln
    dotnet build .\pbl4.sln --no-restore
}
finally {
    Pop-Location
}

