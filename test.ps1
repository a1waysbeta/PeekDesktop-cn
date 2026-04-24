[CmdletBinding()]
param(
    [int]$Iterations = 10000,
    [switch]$NoBuild,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

if (-not $NoBuild)
{
    dotnet build ".\PeekDesktop.sln"
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

if ($VerboseOutput)
{
    dotnet run --project ".\src\PeekDesktop.InteropHarness\PeekDesktop.InteropHarness.csproj" -- $Iterations --verbose
}
else
{
    dotnet run --project ".\src\PeekDesktop.InteropHarness\PeekDesktop.InteropHarness.csproj" -- $Iterations
}
exit $LASTEXITCODE
