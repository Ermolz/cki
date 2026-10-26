Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = (Get-Command dotnet).Source
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('cki-arithmetic-' + [Guid]::NewGuid().ToString('N'))
$checks = 0

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Invoke-Program([string] $program, [string[]] $programArguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.ArgumentList.Add($program)
    foreach ($argument in $programArguments) { $start.ArgumentList.Add($argument) }
    $start.WorkingDirectory = $repository
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    $start.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($start)
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $result = [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Error = $errorOutput }
    $process.Dispose()
    return $result
}

function Assert-Bytes([byte[]] $expected, [byte[]] $actual, [string] $message) {
    Assert-True ([Convert]::ToBase64String($expected) -ceq [Convert]::ToBase64String($actual)) $message
}

foreach ($role in @('Compress', 'Decompress')) {
    $project = Join-Path $repository "ArithmeticCoding/$role/Arithmetic$role.csproj"
    & $dotnet build $project -c Release --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

$compressor = Join-Path $repository 'ArithmeticCoding/Compress/bin/Release/net8.0/ArithmeticCompress.dll'
$decompressor = Join-Path $repository 'ArithmeticCoding/Decompress/bin/Release/net8.0/ArithmeticDecompress.dll'
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    $fixtures = [ordered]@{
        empty = [byte[]]@()
        single = [byte[]]@(65)
        repeated = [Text.Encoding]::ASCII.GetBytes(('A' * 1000))
        example = [Text.Encoding]::ASCII.GetBytes('AAAAABBC')
        allbytes = [byte[]](0..255)
        unicode = $utf8.GetBytes("Привіт, світе! 😀`r`nДругий рядок.")
    }
    $random = [Random]::new(20261003)
    foreach ($length in @(2, 7, 8, 9, 127, 1024, 16384, 65536)) {
        $bytes = [byte[]]::new($length)
        $random.NextBytes($bytes)
        $fixtures["random$length"] = $bytes
    }

    foreach ($fixture in $fixtures.GetEnumerator()) {
        $source = Join-Path $temporaryDirectory ($fixture.Key + '.input')
        $archive = Join-Path $temporaryDirectory ($fixture.Key + '.arc')
        $restored = Join-Path $temporaryDirectory ($fixture.Key + '.restored')
        [IO.File]::WriteAllBytes($source, $fixture.Value)
        $encoded = Invoke-Program $compressor @($source, $archive)
        Assert-True ($encoded.ExitCode -eq 0) "Compression failed: $($fixture.Key). $($encoded.Error)"
        $decoded = Invoke-Program $decompressor @($archive, $restored)
        Assert-True ($decoded.ExitCode -eq 0) "Restoration failed: $($fixture.Key). $($decoded.Error)"
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($restored)) "Restored bytes differ: $($fixture.Key)."
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($source)) "Source changed: $($fixture.Key)."
    }

    $exampleArchive = [IO.File]::ReadAllBytes((Join-Path $temporaryDirectory 'example.arc'))
    Assert-True ([Text.Encoding]::ASCII.GetString($exampleArchive, 0, 4) -ceq 'ARC1') 'Wrong archive signature.'
    Assert-True ([BitConverter]::ToUInt16($exampleArchive, 20) -eq 3) 'Wrong frequency table size.'
    Assert-True ([BitConverter]::ToInt64($exampleArchive, 23) -eq 5) 'Wrong frequency for A.'

    foreach ($sample in @('', 'A', 'AAAAABBC', 'Привіт, світе! 😀', "Line 1`r`nLine 2`n")) {
        $encoded = Invoke-Program $compressor @('--text', $sample)
        Assert-True ($encoded.ExitCode -eq 0) "String compression failed: $($encoded.Error)"
        $decoded = Invoke-Program $decompressor @('--text', $encoded.Output.TrimEnd("`r", "`n"))
        Assert-True ($decoded.ExitCode -eq 0) "String restoration failed: $($decoded.Error)"
        Assert-True ($decoded.Output -ceq $sample) 'Restored string differs.'
    }

    $protected = Join-Path $temporaryDirectory 'protected.output'
    [IO.File]::WriteAllBytes($protected, [Text.Encoding]::ASCII.GetBytes('keep'))
    Assert-True ((Invoke-Program $compressor @((Join-Path $temporaryDirectory 'example.input'), $protected)).ExitCode -eq 1) 'Compressor overwrote an existing file.'
    Assert-True ((Invoke-Program $decompressor @((Join-Path $temporaryDirectory 'example.arc'), $protected)).ExitCode -eq 1) 'Decoder overwrote an existing file.'
    Assert-Bytes ([Text.Encoding]::ASCII.GetBytes('keep')) ([IO.File]::ReadAllBytes($protected)) 'Protected file changed.'

    $truncated = Join-Path $temporaryDirectory 'truncated.arc'
    $unexpectedOutput = Join-Path $temporaryDirectory 'unexpected.output'
    [IO.File]::WriteAllBytes($truncated, [byte[]]$exampleArchive[0..($exampleArchive.Length - 2)])
    Assert-True ((Invoke-Program $decompressor @($truncated, $unexpectedOutput)).ExitCode -eq 1) 'Truncated archive accepted.'
    Assert-True (-not (Test-Path -LiteralPath $unexpectedOutput)) 'Truncated archive created output.'
    Assert-True ((Invoke-Program $decompressor @('--text', '!!!')).ExitCode -eq 1) 'Invalid Base64 accepted.'
    Assert-True ((Invoke-Program $decompressor @('--text', [Convert]::ToBase64String([byte[]]@()))).ExitCode -eq 1) 'Invalid archive accepted.'

    Write-Output "Passed $checks arithmetic coding checks."
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedDirectory = [IO.Path]::GetFullPath($temporaryDirectory)
    if (-not $resolvedDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target is outside the temporary directory.' }
    if (Test-Path -LiteralPath $resolvedDirectory) {
        $item = Get-Item -LiteralPath $resolvedDirectory
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Cleanup target is a link.' }
        Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    }
}
