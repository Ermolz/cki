Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet).Source
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$checks = 0
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('cki-compression-' + [Guid]::NewGuid().ToString('N'))

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

$projects = @(
    [pscustomobject]@{ Name = 'Huffman'; Folder = 'HuffmanCoding'; Extension = '.huf'; Magic = 'HUF1'; DefaultOutput = $true; ExampleBytes = [byte[]]@(250, 128) }
    [pscustomobject]@{ Name = 'ShannonFano'; Folder = 'ShannonFanoCoding'; Extension = '.sfc'; Magic = 'SFC1'; DefaultOutput = $false; ExampleBytes = [byte[]]@(5, 96) }
)

foreach ($project in $projects) {
    foreach ($role in @('Compress', 'Decompress')) {
        $projectFile = Join-Path $repository "$($project.Folder)/$role/$($project.Name)$role.csproj"
        & $dotnet build $projectFile -c Release --ignore-failed-sources
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $projectFile" }
        $checks++
    }
}

[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

try {
    $fixtures = [ordered]@{
        empty = [byte[]]@()
        single = [byte[]](1..9 | ForEach-Object { 65 })
        example = [Text.Encoding]::ASCII.GetBytes('AAAAABBC')
        allbytes = [byte[]](0..255)
        unicode = $utf8.GetBytes("Привіт, світе! 😀`r`nДругий рядок.`n")
        repetitive = [Text.Encoding]::ASCII.GetBytes(('abracadabra ' * 500))
    }
    $random = [Random]::new(20260919)
    $randomBytes = [byte[]]::new(4096)
    $random.NextBytes($randomBytes)
    $fixtures.random = $randomBytes

    foreach ($project in $projects) {
        $compressor = Join-Path $repository "$($project.Folder)/Compress/bin/Release/net8.0/$($project.Name)Compress.dll"
        $decompressor = Join-Path $repository "$($project.Folder)/Decompress/bin/Release/net8.0/$($project.Name)Decompress.dll"
        $projectDirectory = Join-Path $temporaryDirectory $project.Name
        [IO.Directory]::CreateDirectory($projectDirectory) | Out-Null

        foreach ($fixture in $fixtures.GetEnumerator()) {
            $source = Join-Path $projectDirectory ($fixture.Key + '.input')
            $archive = Join-Path $projectDirectory ($fixture.Key + $project.Extension)
            $restored = if ($project.DefaultOutput) { $archive + '.restored' } else { Join-Path $projectDirectory ($fixture.Key + '.restored') }
            [IO.File]::WriteAllBytes($source, $fixture.Value)
            Assert-Success (Invoke-Program $compressor @($source, $archive)) "Compression failed: $($project.Name)/$($fixture.Key)."
            $arguments = if ($project.DefaultOutput) { @($archive) } else { @($archive, $restored) }
            Assert-Success (Invoke-Program $decompressor $arguments) "Restoration failed: $($project.Name)/$($fixture.Key)."
            Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($restored)) "Restored bytes differ: $($project.Name)/$($fixture.Key)."
            Assert-Bytes $fixture.Value ([IO.File]::ReadAllBytes($source)) "Source changed: $($project.Name)/$($fixture.Key)."
        }

        $exampleArchive = [IO.File]::ReadAllBytes((Join-Path $projectDirectory ('example' + $project.Extension)))
        Assert-True ($exampleArchive.Length -eq 51) "Unexpected archive size: $($project.Name)."
        Assert-True ([Text.Encoding]::ASCII.GetString($exampleArchive, 0, 4) -ceq $project.Magic) "Wrong archive signature: $($project.Name)."
        Assert-True ([BitConverter]::ToInt64($exampleArchive, 12) -eq 11) "Wrong bit count: $($project.Name)."
        Assert-Bytes $project.ExampleBytes ([byte[]]$exampleArchive[49..50]) "Wrong example code: $($project.Name)."
        Assert-True (([IO.File]::ReadAllBytes((Join-Path $projectDirectory ('empty' + $project.Extension)))).Length -eq 22) "Wrong empty archive size: $($project.Name)."
        Assert-True ([BitConverter]::ToUInt16([IO.File]::ReadAllBytes((Join-Path $projectDirectory ('allbytes' + $project.Extension))), 20) -eq 256) "Full alphabet missing: $($project.Name)."

        foreach ($sample in @('', 'A', 'AAAAABBC', 'Привіт, світе! 😀', "Line 1`r`nLine 2`n")) {
            $compressed = Invoke-Program $compressor @('--text', $sample)
            Assert-Success $compressed "String compression failed: $($project.Name)."
            $restored = Invoke-Program $decompressor @('--text', $compressed.Output.TrimEnd("`r", "`n"))
            Assert-Success $restored "String restoration failed: $($project.Name)."
            Assert-True ($restored.Output -ceq $sample) "Restored string differs: $($project.Name)."
        }

        $protected = Join-Path $projectDirectory 'protected.output'
        [IO.File]::WriteAllBytes($protected, [Text.Encoding]::ASCII.GetBytes('keep'))
        $source = Join-Path $projectDirectory 'example.input'
        $archive = Join-Path $projectDirectory ('example' + $project.Extension)
        Assert-True ((Invoke-Program $compressor @($source, $protected)).ExitCode -eq 1) "Existing output overwritten by compressor: $($project.Name)."
        Assert-True ((Invoke-Program $decompressor @($archive, $protected)).ExitCode -eq 1) "Existing output overwritten by decoder: $($project.Name)."
        Assert-Bytes ([Text.Encoding]::ASCII.GetBytes('keep')) ([IO.File]::ReadAllBytes($protected)) "Protected output changed: $($project.Name)."

        $truncated = Join-Path $projectDirectory ('truncated' + $project.Extension)
        $unexpectedOutput = Join-Path $projectDirectory 'unexpected.output'
        [IO.File]::WriteAllBytes($truncated, [byte[]]$exampleArchive[0..49])
        Assert-True ((Invoke-Program $decompressor @($truncated, $unexpectedOutput)).ExitCode -eq 1) "Truncated archive accepted: $($project.Name)."
        Assert-True (-not (Test-Path -LiteralPath $unexpectedOutput)) "Truncated archive created output: $($project.Name)."

        $invalidText = Invoke-Program $decompressor @('--text', '!!!')
        Assert-True ($invalidText.ExitCode -eq 1) "Invalid Base64 accepted: $($project.Name)."
    }

    Write-Output "Passed $checks checks for both algorithms."
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
