Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = (Get-Command dotnet).Source
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$checks = 0
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('cki-lz78-' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Assert-Bytes([byte[]] $expected, [byte[]] $actual, [string] $message) {
    Assert-True ([Convert]::ToBase64String($expected) -ceq [Convert]::ToBase64String($actual)) $message
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

function Assert-Success($result, [string] $message) {
    Assert-True ($result.ExitCode -eq 0 -and $result.Error.Length -eq 0) ($message + ' ' + $result.Error)
}

function Get-PayloadBits([byte[]] $archive) {
    $bitCount = [BitConverter]::ToInt64($archive, 12)
    $builder = [Text.StringBuilder]::new()
    for ($index = 0; $index -lt $bitCount; $index++) {
        $bit = ($archive[20 + [int][Math]::Floor($index / 8)] -shr (7 - ($index % 8))) -band 1
        [void]$builder.Append($bit)
    }
    return $builder.ToString()
}

function New-Archive([long] $originalLength, [string] $bits) {
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('LZ78'))
        $writer.Write($originalLength)
        $writer.Write([long]$bits.Length)
        for ($offset = 0; $offset -lt $bits.Length; $offset += 8) {
            $chunk = $bits.Substring($offset, [Math]::Min(8, $bits.Length - $offset)).PadRight(8, '0')
            $writer.Write([byte][Convert]::ToInt32($chunk, 2))
        }
        $writer.Flush()
        return ,$stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

foreach ($role in @('Compress', 'Decompress')) {
    $project = Join-Path $repository "Lz78Coding/$role/Lz78$role.csproj"
    & $dotnet build $project -c Release --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

$compressor = Join-Path $repository 'Lz78Coding/Compress/bin/Release/net8.0/Lz78Compress.dll'
$decompressor = Join-Path $repository 'Lz78Coding/Decompress/bin/Release/net8.0/Lz78Decompress.dll'
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    $fixtures = [ordered]@{
        empty = [byte[]]@()
        single = [Text.Encoding]::ASCII.GetBytes('A')
        terminal = [Text.Encoding]::ASCII.GetBytes('AA')
        phrase = [Text.Encoding]::ASCII.GetBytes('AAA')
        example = [Text.Encoding]::ASCII.GetBytes('ABABABA')
        boundary = [Text.Encoding]::ASCII.GetBytes('ABABABAB')
        repetitive = [Text.Encoding]::ASCII.GetBytes(('abracadabra ' * 5000))
        allbytes = [byte[]](0..255)
        unicode = $utf8.GetBytes("Привіт, світе! 😀`r`nДругий рядок.`n")
    }
    $random = [Random]::new(20261010)
    foreach ($length in @(2, 7, 8, 9, 127, 1024, 4096, 65536)) {
        $bytes = [byte[]]::new($length)
        $random.NextBytes($bytes)
        $fixtures["random$length"] = $bytes
    }

    foreach ($fixture in $fixtures.GetEnumerator()) {
        $source = Join-Path $temporaryDirectory ($fixture.Key + '.input')
        $archive = Join-Path $temporaryDirectory ($fixture.Key + '.lz78')
        $restored = $archive + '.restored'
        [IO.File]::WriteAllBytes($source, $fixture.Value)
        Assert-Success (Invoke-Program $compressor @($source, $archive)) "Compression failed: $($fixture.Key)."
        Assert-Success (Invoke-Program $decompressor @($archive)) "Restoration failed: $($fixture.Key)."
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($restored)) "Restored bytes differ: $($fixture.Key)."
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($source)) "Source changed: $($fixture.Key)."
    }

    $payloads = [ordered]@{
        empty = ''
        single = '01000001'
        terminal = '010000011'
        phrase = '01000001101000001'
        example = '01000001' + '001000010' + '0101000010' + '1101000001'
        boundary = '01000001' + '001000010' + '0101000010' + '1101000001' + '010'
    }
    foreach ($expected in $payloads.GetEnumerator()) {
        $archive = [IO.File]::ReadAllBytes((Join-Path $temporaryDirectory ($expected.Key + '.lz78')))
        Assert-True ([Text.Encoding]::ASCII.GetString($archive, 0, 4) -ceq 'LZ78') 'Wrong signature.'
        Assert-True ([BitConverter]::ToInt64($archive, 12) -eq $expected.Value.Length) "Wrong bit count: $($expected.Key)."
        Assert-True ((Get-PayloadBits $archive) -ceq $expected.Value) "Wrong LZ-78 code: $($expected.Key)."
        Assert-True ($archive.Length -eq 20 + [Math]::Ceiling($expected.Value.Length / 8)) 'Wrong archive size.'
    }

    foreach ($sample in @('', 'A', 'AA', 'ABABABA', '0101010100111100', 'Привіт, світе! 😀', "Line 1`r`nLine 2`n")) {
        $encoded = Invoke-Program $compressor @('--text', $sample)
        Assert-Success $encoded 'String compression failed.'
        $decoded = Invoke-Program $decompressor @('--text', $encoded.Output.TrimEnd([char]13, [char]10))
        Assert-Success $decoded 'String restoration failed.'
        Assert-True ($decoded.Output -ceq $sample) 'Restored string differs.'
    }

    $source = Join-Path $temporaryDirectory 'example.input'
    $archivePath = Join-Path $temporaryDirectory 'example.lz78'
    $explicitOutput = Join-Path $temporaryDirectory 'explicit.output'
    Assert-Success (Invoke-Program $decompressor @($archivePath, $explicitOutput)) 'Explicit output failed.'
    Assert-Bytes $fixtures.example ([IO.File]::ReadAllBytes($explicitOutput)) 'Explicit output differs.'
    $protected = Join-Path $temporaryDirectory 'protected.output'
    [IO.File]::WriteAllBytes($protected, [Text.Encoding]::ASCII.GetBytes('keep'))
    Assert-True ((Invoke-Program $compressor @($source, $protected)).ExitCode -eq 1) 'Compressor overwrote an existing output.'
    Assert-True ((Invoke-Program $decompressor @($archivePath, $protected)).ExitCode -eq 1) 'Decoder overwrote an existing output.'
    Assert-Bytes ([Text.Encoding]::ASCII.GetBytes('keep')) ([IO.File]::ReadAllBytes($protected)) 'Protected output changed.'

    $valid = [IO.File]::ReadAllBytes($archivePath)
    $badSignature = [byte[]]$valid.Clone()
    $badSignature[0] = 0
    $badPadding = [IO.File]::ReadAllBytes((Join-Path $temporaryDirectory 'terminal.lz78'))
    $badPadding[-1] = $badPadding[-1] -bor 1
    $badLength = [byte[]]$valid.Clone()
    [Array]::Copy([BitConverter]::GetBytes([long]8), 0, $badLength, 4, 8)
    $invalidArchives = [ordered]@{
        empty = [byte[]]@()
        signature = $badSignature
        header = [byte[]]$valid[0..10]
        truncated = [byte[]]$valid[0..($valid.Length - 2)]
        padding = $badPadding
        length = $badLength
        index = New-Archive 3 ('01000001' + '001000010' + '11' + '00000000')
        symbol = New-Archive 2 ('01000001' + '1' + '010')
        emptyReference = New-Archive 1 ('01000001' + '0')
    }
    foreach ($fixture in $invalidArchives.GetEnumerator()) {
        $invalidPath = Join-Path $temporaryDirectory ($fixture.Key + '.invalid')
        $unexpectedOutput = Join-Path $temporaryDirectory ($fixture.Key + '.unexpected')
        [IO.File]::WriteAllBytes($invalidPath, $fixture.Value)
        $result = Invoke-Program $decompressor @($invalidPath, $unexpectedOutput)
        Assert-True ($result.ExitCode -eq 1 -and $result.Error.Contains('Error: invalid archive.')) "Invalid archive accepted or crashed: $($fixture.Key)."
        Assert-True (-not (Test-Path -LiteralPath $unexpectedOutput)) 'Invalid archive created output.'
    }

    $invalidText = Invoke-Program $decompressor @('--text', '!!!')
    Assert-True ($invalidText.ExitCode -eq 1 -and $invalidText.Error.Contains('Error: invalid Base64.')) 'Invalid Base64 accepted.'
    $binaryArchive = Join-Path $temporaryDirectory 'allbytes.lz78'
    $invalidUtf8 = Invoke-Program $decompressor @('--text', [Convert]::ToBase64String([IO.File]::ReadAllBytes($binaryArchive)))
    Assert-True ($invalidUtf8.ExitCode -eq 1 -and $invalidUtf8.Error.Contains('Error: invalid UTF-8.')) 'Invalid UTF-8 accepted.'
    Assert-True ((Invoke-Program $compressor @()).ExitCode -eq 1) 'Missing compression arguments accepted.'
    Assert-True ((Invoke-Program $decompressor @('--text')).ExitCode -eq 1) 'Missing Base64 accepted.'
    Assert-True (([IO.File]::ReadAllBytes((Join-Path $temporaryDirectory 'repetitive.lz78'))).Length -lt $fixtures.repetitive.Length) 'Repetitive data did not compress.'

    Write-Output "Passed $checks LZ-78 checks."
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
