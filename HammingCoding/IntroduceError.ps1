[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    # Each encoded byte is one block; the first block has index 0.
    [ValidateRange(0, 2147483647)]
    [int] $BlockIndex = 0,

    # Positions 1 through 8 run from the most significant bit to the least.
    [ValidateCount(1, 2)]
    [ValidateRange(1, 8)]
    [int[]] $BitPositions = @(3)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourcePath = (Resolve-Path -LiteralPath $InputPath).ProviderPath
$destinationPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)

if ([string]::Equals($sourcePath, $destinationPath, [StringComparison]::OrdinalIgnoreCase))
{
    throw 'Input and output paths must be different.'
}

if (@($BitPositions | Select-Object -Unique).Count -ne $BitPositions.Count)
{
    throw 'Bit positions must be distinct.'
}

$encodedBytes = [IO.File]::ReadAllBytes($sourcePath)

if ($encodedBytes.Length -eq 0 -or $encodedBytes.Length % 2 -ne 0)
{
    throw 'Expected a nonempty Hamming-encoded file with an even byte count.'
}

if ($BlockIndex -ge $encodedBytes.Length)
{
    throw 'Block index is outside the encoded file.'
}

$before = $encodedBytes[$BlockIndex]

foreach ($position in $BitPositions)
{
    $encodedBytes[$BlockIndex] = [byte]($encodedBytes[$BlockIndex] -bxor (1 -shl (8 - $position)))
}

$stream = [IO.File]::Open($destinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)

try
{
    $stream.Write($encodedBytes, 0, $encodedBytes.Length)
}
finally
{
    $stream.Dispose()
}

Write-Output ('Block {0}: {1} -> {2}' -f $BlockIndex,
    [Convert]::ToString($before, 2).PadLeft(8, '0'),
    [Convert]::ToString($encodedBytes[$BlockIndex], 2).PadLeft(8, '0'))
Write-Output ('Flipped positions: ' + ($BitPositions -join ', '))
Write-Output ('Saved: ' + $OutputPath)

# Run from the repository root:
# .\HammingCoding\IntroduceError.ps1 -InputPath .\HammingCoding\encoded.ham -OutputPath .\HammingCoding\damaged.ham
