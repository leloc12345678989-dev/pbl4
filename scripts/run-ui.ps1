Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    dotnet run --project .\src\UI\pbl4.UI.csproj
}
finally {
    Pop-Location
}

