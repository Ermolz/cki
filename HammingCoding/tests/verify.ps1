Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = (Get-Command dotnet).Source
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$checks = 0
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('cki-hamming-' + [Guid]::NewGuid().ToString('N'))

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

foreach ($role in @('Encode', 'Decode')) {
    $project = Join-Path $repository "HammingCoding/$role/Hamming$role.csproj"
    & $dotnet build $project -c Release --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

$encoder = Join-Path $repository 'HammingCoding/Encode/bin/Release/net8.0/HammingEncode.dll'
$decoder = Join-Path $repository 'HammingCoding/Decode/bin/Release/net8.0/HammingDecode.dll'
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$encodeBlock = [Reflection.Assembly]::LoadFrom($encoder).GetType('Program').GetMethod('EncodeNibble', $flags)
$decodeBlock = [Reflection.Assembly]::LoadFrom($decoder).GetType('Program').GetMethod('DecodeNibble', $flags)
$codewords = [byte[]]@(0x00, 0xD2, 0x55, 0x87, 0x99, 0x4B, 0xCC, 0x1E,
                       0xE1, 0x33, 0xB4, 0x66, 0x78, 0xAA, 0x2D, 0xFF)

for ($data = 0; $data -lt 16; $data++) {
    $code = [byte]$encodeBlock.Invoke($null, [object[]]@($data))
    Assert-True ($code -eq $codewords[$data]) "Wrong codeword for $data."
    Assert-True ([int]$decodeBlock.Invoke($null, [object[]]@($code)) -eq $data) 'Unchanged block decoded incorrectly.'

    for ($first = 0; $first -lt 8; $first++) {
        $damaged = [byte]($code -bxor (1 -shl $first))
        Assert-True ([int]$decodeBlock.Invoke($null, [object[]]@($damaged)) -eq $data) "Single-bit error was not corrected: $data/$first."

        for ($second = $first + 1; $second -lt 8; $second++) {
            $damaged = [byte]($code -bxor (1 -shl $first) -bxor (1 -shl $second))
            $rejected = $false
            try {
                $null = $decodeBlock.Invoke($null, [object[]]@($damaged))
            }
            catch {
                $cause = $_.Exception
                while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
                $rejected = $cause -is [IO.InvalidDataException]
            }
            Assert-True $rejected "Double-bit error was not rejected: $data/$first/$second."
        }
    }
}

[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    $fixtures = [ordered]@{
        empty = [byte[]]@()
        zero = [byte[]]@(0)
        ones = [byte[]]@(255)
        example = [Text.Encoding]::ASCII.GetBytes('A')
        allbytes = [byte[]](0..255)
        unicode = $utf8.GetBytes("Привіт, світе! 😀`r`nДругий рядок.`n")
        repetitive = [Text.Encoding]::ASCII.GetBytes(('abracadabra ' * 5000))
    }
    $random = [Random]::new(20261010)
    foreach ($length in @(7, 8, 9, 4096, 65536)) {
        $bytes = [byte[]]::new($length)
        $random.NextBytes($bytes)
        $fixtures["random$length"] = $bytes
    }

    foreach ($fixture in $fixtures.GetEnumerator()) {
        $source = Join-Path $temporaryDirectory ($fixture.Key + '.input')
        $encodedPath = Join-Path $temporaryDirectory ($fixture.Key + '.ham')
        [IO.File]::WriteAllBytes($source, $fixture.Value)
        Assert-Success (Invoke-Program $encoder @($source, $encodedPath)) "Encoding failed: $($fixture.Key)."
        $encoded = [IO.File]::ReadAllBytes($encodedPath)
        Assert-True ($encoded.Length -eq 2 * $fixture.Value.Length) 'Wrong encoded length.'
        $expected = [byte[]]::new($encoded.Length)
        for ($index = 0; $index -lt $fixture.Value.Length; $index++) {
            $expected[2 * $index] = $codewords[$fixture.Value[$index] -shr 4]
            $expected[2 * $index + 1] = $codewords[$fixture.Value[$index] -band 15]
        }
        Assert-Bytes $expected $encoded 'Encoded file differs from the reference codewords.'
        Assert-Success (Invoke-Program $decoder @($encodedPath)) 'Decoding failed.'
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($encodedPath + '.restored')) 'Restored file differs.'
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($source)) 'Source changed.'

        for ($index = 0; $index -lt $encoded.Length; $index++) {
            $encoded[$index] = [byte]($encoded[$index] -bxor (1 -shl ($index % 8)))
        }
        $damagedPath = Join-Path $temporaryDirectory ($fixture.Key + '.damaged.ham')
        $correctedPath = Join-Path $temporaryDirectory ($fixture.Key + '.corrected')
        [IO.File]::WriteAllBytes($damagedPath, $encoded)
        Assert-Success (Invoke-Program $decoder @($damagedPath, $correctedPath)) 'Damaged file could not be corrected.'
        Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($correctedPath)) 'Corrected file differs.'
    }

    $allBytesPath = Join-Path $temporaryDirectory 'allbytes.ham'
    $allBytes = [IO.File]::ReadAllBytes($allBytesPath)
    for ($bit = 0; $bit -lt 8; $bit++) {
        $damaged = [byte[]]$allBytes.Clone()
        for ($index = 0; $index -lt $damaged.Length; $index++) {
            $damaged[$index] = [byte]($damaged[$index] -bxor (1 -shl $bit))
        }
        $damagedPath = Join-Path $temporaryDirectory "bit$bit.ham"
        [IO.File]::WriteAllBytes($damagedPath, $damaged)
        Assert-Success (Invoke-Program $decoder @($damagedPath)) "File error at bit $bit was not corrected."
        Assert-Bytes $fixtures.allbytes ([IO.File]::ReadAllBytes($damagedPath + '.restored')) 'Exhaustive file correction differs.'
    }

    foreach ($sample in @('', 'A', '0101010100111100', 'Привіт, світе! 😀', "Line 1`r`nLine 2`n")) {
        $result = Invoke-Program $encoder @('--text', $sample)
        Assert-Success $result 'String encoding failed.'
        $base64 = $result.Output.TrimEnd([char]13, [char]10)
        $decoded = Invoke-Program $decoder @('--text', $base64)
        Assert-Success $decoded 'String decoding failed.'
        Assert-True ($decoded.Output -ceq $sample) 'Decoded string differs.'
        $damaged = [Convert]::FromBase64String($base64)
        for ($index = 0; $index -lt $damaged.Length; $index++) {
            $damaged[$index] = [byte]($damaged[$index] -bxor (1 -shl ($index % 8)))
        }
        $decoded = Invoke-Program $decoder @('--text', [Convert]::ToBase64String($damaged))
        Assert-Success $decoded 'String error correction failed.'
        Assert-True ($decoded.Output -ceq $sample) 'Corrected string differs.'
    }

    $protected = Join-Path $temporaryDirectory 'protected.output'
    $keep = [Text.Encoding]::ASCII.GetBytes('keep')
    [IO.File]::WriteAllBytes($protected, $keep)
    Assert-True ((Invoke-Program $encoder @((Join-Path $temporaryDirectory 'example.input'), $protected)).ExitCode -eq 1) 'Encoder overwrote an output.'
    Assert-True ((Invoke-Program $decoder @($allBytesPath, $protected)).ExitCode -eq 1) 'Decoder overwrote an output.'
    Assert-Bytes $keep ([IO.File]::ReadAllBytes($protected)) 'Protected output changed.'

    $invalidCases = [ordered]@{
        odd = @{ Bytes = [byte[]]@(0); Error = 'Error: invalid encoded length.' }
        double = @{ Bytes = [byte[]]@(0xC0, 0); Error = 'Error: uncorrectable block.' }
        lateDouble = @{ Bytes = [byte[]]@(0, 0, 0, 0x03); Error = 'Error: uncorrectable block.' }
    }
    foreach ($fixture in $invalidCases.GetEnumerator()) {
        $invalidPath = Join-Path $temporaryDirectory ($fixture.Key + '.invalid.ham')
        $outputPath = Join-Path $temporaryDirectory ($fixture.Key + '.unexpected')
        [IO.File]::WriteAllBytes($invalidPath, $fixture.Value.Bytes)
        $result = Invoke-Program $decoder @($invalidPath, $outputPath)
        Assert-True ($result.ExitCode -eq 1 -and $result.Error.Contains($fixture.Value.Error)) 'Invalid encoded file accepted or crashed.'
        Assert-True (-not (Test-Path -LiteralPath $outputPath)) 'Invalid data created an output.'
    }

    $result = Invoke-Program $decoder @('--text', '!!!')
    Assert-True ($result.ExitCode -eq 1 -and $result.Error.Contains('Error: invalid Base64.')) 'Invalid Base64 accepted.'
    $result = Invoke-Program $decoder @('--text', [Convert]::ToBase64String($allBytes))
    Assert-True ($result.ExitCode -eq 1 -and $result.Error.Contains('Error: invalid UTF-8.')) 'Invalid UTF-8 accepted.'
    Assert-True ((Invoke-Program $encoder @()).ExitCode -eq 1) 'Missing encoding arguments accepted.'
    Assert-True ((Invoke-Program $decoder @('--text')).ExitCode -eq 1) 'Missing Base64 accepted.'
    Assert-True ((Invoke-Program $encoder @('--help')).ExitCode -eq 0) 'Encoder help failed.'
    Assert-True ((Invoke-Program $decoder @('--help')).ExitCode -eq 0) 'Decoder help failed.'

    Write-Output "Passed $checks Hamming checks."
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
