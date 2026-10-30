$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) {
        throw "$Message. Expected '$Expected', got '$Actual'."
    }
}

Assert-Equal 'SocialStudio.Domain' `
    (Get-ProjectReferenceName -Include '..\SocialStudio.Domain\SocialStudio.Domain.csproj') `
    'Windows-style ProjectReference must resolve cross-platform'
Assert-Equal 'SocialStudio.Domain' `
    (Get-ProjectReferenceName -Include '../SocialStudio.Domain/SocialStudio.Domain.csproj') `
    'Unix-style ProjectReference must resolve cross-platform'

$details = Format-GateDetails -Details "token=super-sensitive-value`nline2`nline3`nline4" -MaxLines 3 -MaxCharacters 500
if ($details -match 'super-sensitive-value') { throw 'Gate details leaked a token value.' }
if ($details -notmatch '\[REDACTED\]') { throw 'Gate details did not mark the redaction.' }
if ($details -notmatch 'details truncated') { throw 'Gate details did not report truncation.' }

$longDetails = @('HEAD-DIAGNOSTIC') + @(1..100 | ForEach-Object { "warning-line-$_" }) + @('TAIL-FAILURE-STACK')
$headTail = Format-GateDetails -Details ($longDetails -join "`n") -MaxLines 10 -MaxCharacters 1000
if ($headTail -notmatch 'HEAD-DIAGNOSTIC' -or $headTail -notmatch 'TAIL-FAILURE-STACK') {
    throw 'Gate detail truncation did not preserve both diagnostic head and failure tail.'
}
if ($headTail -match 'warning-line-50' -or $headTail -notmatch 'tail follows') {
    throw 'Gate detail truncation did not remove the middle with a clear marker.'
}
if (@($headTail -split "`n").Count -gt 10 -or $headTail.Length -gt 1000) {
    throw 'Gate detail truncation exceeded its configured bounds.'
}

foreach ($tiny in @(1, 10, 20)) {
    $tinyOutput = Format-GateDetails -Details ($longDetails -join "`n") -MaxLines $tiny -MaxCharacters $tiny
    if (@($tinyOutput -split "`n").Count -gt $tiny -or $tinyOutput.Length -gt $tiny) {
        throw "Gate detail truncation exceeded tiny bound $tiny."
    }
}

foreach ($invalid in @(0, -1)) {
    $lineRejected = $false
    try { $null = Format-GateDetails -Details 'synthetic' -MaxLines $invalid -MaxCharacters 20 }
    catch { $lineRejected = $_.FullyQualifiedErrorId -match 'ParameterArgumentValidationError' }
    if (-not $lineRejected) { throw "MaxLines accepted invalid bound $invalid." }

    $characterRejected = $false
    try { $null = Format-GateDetails -Details 'synthetic' -MaxLines 20 -MaxCharacters $invalid }
    catch { $characterRejected = $_.FullyQualifiedErrorId -match 'ParameterArgumentValidationError' }
    if (-not $characterRejected) { throw "MaxCharacters accepted invalid bound $invalid." }
}

$combinedSecret = 'token=' + "'synthetic phrase that must disappear'"
$combinedInput = @('HEAD-SAFE', $combinedSecret) + @(1..20 | ForEach-Object { "noise-$_" }) + @('TAIL-SAFE')
$combinedOutput = Format-GateDetails -Details ($combinedInput -join "`n") -MaxLines 20 -MaxCharacters 80
if ($combinedOutput.Contains('synthetic phrase that must disappear')) {
    throw 'Character truncation leaked a synthetic secret after sanitization.'
}
if ($combinedOutput -notmatch 'HEAD-SAFE' -or $combinedOutput -notmatch 'TAIL-SAFE') {
    throw 'Character truncation did not preserve diagnostic head and tail when capacity allowed.'
}
if (@($combinedOutput -split "`n").Count -gt 20 -or $combinedOutput.Length -gt 80) {
    throw 'Combined sanitization and character truncation exceeded configured bounds.'
}

$jwt = ('eyJ' + ('a' * 12)) + '.' + ('eyJ' + ('b' * 12)) + '.' + ('c' * 14)
$awsAccess = 'AK' + 'IA' + ('A' * 16)
$awsSecret = 'aws_' + 'secret_' + 'access_key=' + ('d' * 40)
$supabase = 'sb' + 'p_' + ('e' * 40)
$serviceKey = 'sb-' + ('f' * 24)
$pemHeader = '-----BEGIN ' + 'PRIVATE KEY-----'
$pemFooter = '-----END ' + 'PRIVATE KEY-----'
$quoted = "token='alpha beta gamma'"
$multilineQuoted = "client_secret=`"first line`nsecond line`""
$connectionString = 'ConnectionString=Server=db;Password=synthetic phrase;User Id=demo'
$credentialUrl = 'https://demo:synthetic-password@example.invalid/path'
$syntheticDetails = @(
    $jwt,
    $awsAccess,
    $awsSecret,
    $supabase,
    $serviceKey,
    "$pemHeader`nsynthetic-key-body`n$pemFooter",
    $quoted,
    $multilineQuoted,
    $connectionString,
    $credentialUrl
) -join "`n"
$protected = Protect-GateOutput -Text $syntheticDetails

$forbiddenValues = @(
    $jwt, $awsAccess, ('d' * 40), $supabase, $serviceKey, 'synthetic-key-body',
    'alpha beta gamma', 'first line', 'second line', 'Server=db', 'synthetic phrase',
    'demo:synthetic-password'
)
for ($i = 0; $i -lt $forbiddenValues.Count; $i++) {
    if ($protected.Contains($forbiddenValues[$i])) { throw "Sanitizer leaked synthetic credential class index $i." }
}
if ($protected -notmatch 'REDACTED_PRIVATE_KEY') { throw 'Private key block was not redacted.' }
if (($protected | Select-String -Pattern 'REDACTED' -AllMatches).Matches.Count -lt 9) {
    throw 'Expected credential classes were not all redacted.'
}

$messageCredential = 'message_' + 'token=' + "'diagnostic phrase value'"
$detailCredential = 'detail_' + 'secret=' + ('z' * 24)
$result = New-GateResult -Gate 'synthetic.output' -Level 'MUST' -Status 'fail' `
    -Message $messageCredential -Details $detailCredential
$normalOutput = (& { Write-GateResult -Result $result } 6>&1 | Out-String)
if ($normalOutput.Contains('diagnostic phrase value')) { throw 'Normal gate output leaked synthetic message credential.' }
if ($normalOutput.Contains(('z' * 24))) { throw 'Normal gate output leaked synthetic detail credential.' }
if ($normalOutput -notmatch 'synthetic.output' -or $normalOutput -notmatch 'REDACTED') {
    throw 'Normal gate output lost useful diagnostic structure.'
}

$safeJson = ConvertTo-SafeGateResultsJson -Results @($result)
$parsedJson = $safeJson | ConvertFrom-Json
if ($safeJson.Contains('diagnostic phrase value') -or $safeJson.Contains(('z' * 24))) {
    throw 'JSON gate output leaked a synthetic credential.'
}
$parsedResult = @($parsedJson)[0]
if ($parsedResult.Gate -ne 'synthetic.output' -or $parsedResult.Message -notmatch 'REDACTED' -or $parsedResult.Details -notmatch 'REDACTED') {
    throw 'JSON gate output is invalid or lost useful diagnostic structure.'
}

$runGatesPath = Join-Path $PSScriptRoot '..\Run-Gates.ps1'
$smokeJson = & pwsh -File $runGatesPath -Only architecture -Json
if ($LASTEXITCODE -ne 0) { throw 'Run-Gates JSON smoke command failed.' }
$smokeResults = @($smokeJson | ConvertFrom-Json)
if ($smokeResults.Count -ne 2 -or @($smokeResults | Where-Object { -not $_.Gate }).Count -gt 0) {
    throw 'Run-Gates JSON smoke output was not a valid architecture result array.'
}

$testArgsWithoutCoverage = @(Get-DotNetTestArguments -ProjectPath 'Example.Tests.csproj' -TrxFileName 'Example.Tests.trx' -ResultsDirectory '.gates/test-results')
$testArgsWithCoverage = @(Get-DotNetTestArguments -ProjectPath 'Example.Tests.csproj' -TrxFileName 'Example.Tests.trx' -ResultsDirectory '.gates/test-results' -CollectCoverage $true)
foreach ($argumentSet in @($testArgsWithoutCoverage, $testArgsWithCoverage)) {
    $resultsDirectoryCount = @($argumentSet | Where-Object { $_ -eq '--results-directory' }).Count
    if ($resultsDirectoryCount -ne 1) { throw 'Dotnet test arguments must contain exactly one results directory.' }
}
if ($testArgsWithoutCoverage -contains '--collect:XPlat Code Coverage') { throw 'Coverage flag was added when disabled.' }
if ($testArgsWithCoverage -notcontains '--collect:XPlat Code Coverage') { throw 'Coverage flag was missing when enabled.' }

$syntheticRepo = Join-Path ([System.IO.Path]::GetTempPath()) 'gate-repo'
$syntheticCoverage = Join-Path $syntheticRepo '.gates/coverage'
$withoutCoveragePaths = Get-TestArtifactPaths -RepoRoot $syntheticRepo -CoverageDirectory $syntheticCoverage -ProjectPath 'Example.Tests/Example.Tests.csproj'
$withCoveragePaths = Get-TestArtifactPaths -RepoRoot $syntheticRepo -CoverageDirectory $syntheticCoverage -ProjectPath 'Example.Tests/Example.Tests.csproj' -CollectCoverage $true
if ($withoutCoveragePaths.Directory -ne (Join-Path $syntheticRepo '.gates/test-results')) {
    throw 'Non-coverage test artifacts did not use the normal test-results directory.'
}
if ($withCoveragePaths.Directory -ne $syntheticCoverage) {
    throw 'Coverage producer directory did not match the coverage consumer directory.'
}
foreach ($paths in @($withoutCoveragePaths, $withCoveragePaths)) {
    if ((Split-Path -Parent $paths.TrxPath) -ne $paths.Directory -or
        (Split-Path -Leaf $paths.TrxPath) -ne 'Example.Tests.trx') {
        throw 'TRX path was not derived from the effective results directory.'
    }
}

$largeFailureDetails = @('STRUCTURED-FAILURE-HEAD') +
    @($combinedSecret) +
    @(1..120 | ForEach-Object { "failure-context-$_" }) +
    @('STRUCTURED-FAILURE-TAIL')
$largeResult = New-GateResult -Gate 'tests.backend' -Level 'MUST' -Status 'fail' `
    -Message 'synthetic failure' -Details ($largeFailureDetails -join "`n")
$largeJson = ConvertTo-SafeGateResultsJson -Results @($largeResult)
$largeParsed = @($largeJson | ConvertFrom-Json)[0]
if ($largeParsed.Details.Length -gt 6000 -or @($largeParsed.Details -split "`n").Count -gt 80) {
    throw 'Safe JSON details exceeded strict diagnostic bounds.'
}
if ($largeParsed.Details.Contains('synthetic phrase that must disappear')) {
    throw 'Safe JSON details leaked a synthetic secret.'
}
if ($largeParsed.Details -notmatch 'STRUCTURED-FAILURE-HEAD' -or $largeParsed.Details -notmatch 'STRUCTURED-FAILURE-TAIL') {
    throw 'Safe JSON details did not preserve structured failure head and tail.'
}

$trxPath = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-trx-$([Guid]::NewGuid().ToString('N')).trx")
$syntheticToken = 'token=' + "'fixture phrase'"
$trxFixture = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="Example.Namespace.FailingTest" outcome="Failed">
      <Output><ErrorInfo><Message>Expected synthetic failure; $syntheticToken</Message><StackTrace>at Example.Namespace.FailingTest() in Example.cs:line 42</StackTrace></ErrorInfo></Output>
    </UnitTestResult>
    <UnitTestResult testName="Example.Namespace.PassingTest" outcome="Passed" />
  </Results>
</TestRun>
"@
try {
    [IO.File]::WriteAllText($trxPath, $trxFixture)
    $trxSummary = Get-TrxFailureSummary -Path $trxPath
    if ($trxSummary -notmatch 'FAILED TEST: Example.Namespace.FailingTest' -or
        $trxSummary -notmatch 'Expected synthetic failure' -or
        $trxSummary -notmatch 'Example.cs:line 42' -or
        $trxSummary -match 'PassingTest') {
        throw 'TRX failure parser did not return the expected structured failure only.'
    }
    $safeTrxSummary = Protect-GateOutput -Text $trxSummary
    if ($safeTrxSummary.Contains('fixture phrase')) { throw 'Structured TRX summary was not sanitizable.' }
} finally {
    if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }
}

Write-Host 'Gate helper tests passed.'
