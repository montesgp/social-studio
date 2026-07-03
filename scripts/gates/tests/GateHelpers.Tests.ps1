$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) {
        throw "$Message. Expected '$Expected', got '$Actual'."
    }
}

Assert-Equal 'Incoders.Hive.Domain' `
    (Get-ProjectReferenceName -Include '..\Incoders.Hive.Domain\Incoders.Hive.Domain.csproj') `
    'Windows-style ProjectReference must resolve cross-platform'
Assert-Equal 'Incoders.Hive.Domain' `
    (Get-ProjectReferenceName -Include '../Incoders.Hive.Domain/Incoders.Hive.Domain.csproj') `
    'Unix-style ProjectReference must resolve cross-platform'

$details = Format-GateDetails -Details "token=super-sensitive-value`nline2`nline3" -MaxLines 2 -MaxCharacters 500
if ($details -match 'super-sensitive-value') { throw 'Gate details leaked a token value.' }
if ($details -notmatch '\[REDACTED\]') { throw 'Gate details did not mark the redaction.' }
if ($details -notmatch 'details truncated') { throw 'Gate details did not report truncation.' }

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

Write-Host 'Gate helper tests passed.'
