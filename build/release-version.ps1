# The release version: ONE rule, used by build\make-installer.ps1 and by CI, so the installer's name, the
# version Setup records and the version the app shows in Settings always agree. Same rule as Vordr's
# build/release-version.sh.
#
#   a tag push    the tag without its v:   v0.1.8 -> 0.1.8,   v0.1.9-dev.1 -> 0.1.9-dev.1
#   anything else git describe:            0.1.8 on the tag,  0.1.8.3 three commits past it,
#                                          0.1.9-dev.1.3 three commits past a pre-release tag
#   no tags at all                         0.1.0.<commit count>
#
# Prints the version. Under GitHub Actions also writes version= and prerelease= to $GITHUB_OUTPUT.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Runs git and returns its output, or $null when it fails. Its error text is dropped: under Windows PowerShell 5.1
# a native command's stderr becomes an error record, which $ErrorActionPreference = 'Stop' would make fatal.
function Invoke-Git {
    $ErrorActionPreference = 'Continue'
    $out = & git -C $root @args 2>$null
    if ($LASTEXITCODE -eq 0) { $out } else { $null }
}

function Test-ReleaseVersion([string]$v) { $v -match '^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z.]+)?$' }

if ($env:GITHUB_REF_TYPE -eq 'tag') {
    $version = $env:GITHUB_REF_NAME -replace '^[vV]', ''
    if (-not (Test-ReleaseVersion $version)) {
        throw "Tag '$env:GITHUB_REF_NAME' is not a release tag. Use vMAJOR.MINOR.PATCH, optionally with a pre-release suffix such as v0.2.0-dev.1."
    }
}
else {
    if (-not (Invoke-Git rev-parse --verify HEAD)) { throw 'Not a git checkout (or git refused it); cannot derive a version.' }

    $describe = Invoke-Git describe --tags --long --match 'v[0-9]*'
    if ($describe) {
        # v0.1.8-3-gabc1234  /  v0.1.9-dev.1-0-gabc1234. The pre-release part cannot contain '-', so the
        # last two '-' fields are always <commits since> and g<hash>.
        if ($describe -notmatch '^[vV](.+)-(\d+)-g[0-9a-fA-F]+$') { throw "Unexpected git describe output '$describe'." }
        $version = if ($Matches[2] -eq '0') { $Matches[1] } else { "$($Matches[1]).$($Matches[2])" }
    }
    else {
        $version = "0.1.0.$((Invoke-Git rev-list --count HEAD).Trim())"
    }
    if (-not (Test-ReleaseVersion $version)) { throw "The nearest tag gives '$version', which is not a release version." }
}

$prerelease = ($version -like '*-*').ToString().ToLower()

$version
if ($env:GITHUB_OUTPUT) {
    "version=$version" >> $env:GITHUB_OUTPUT
    "prerelease=$prerelease" >> $env:GITHUB_OUTPUT
}
