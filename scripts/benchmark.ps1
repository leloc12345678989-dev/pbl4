param(
    [Parameter(Mandatory = $true)]
    [string] $Url,

    [string] $OutputDir = "benchmark-results"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    dotnet run --project .\src\Benchmark\pbl4.Benchmark.csproj -- $Url $OutputDir
}
finally {
    Pop-Location
}
