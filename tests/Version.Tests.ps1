# Pester 5: VERSION (read by the edge-ops release-zip workflow) must match __version.info (read by WebServer.AppVersion).
# __version.info is Package.ps1's yyyy_MMdd_HHmm stamp. VERSION is the same value as X.Y.Z = yyyy.[int]MMdd.[int]HHmm,
# i.e. leading zeros stripped from each part: 2026_0905_0930 -> 2026.905.930, 2026_0101_0005 -> 2026.101.5.
# Run: Invoke-Pester -Path .\tests -Output Detailed
BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $script:SemVer = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
    function ConvertTo-VersionFromStamp([string]$Stamp) {
        if ($Stamp -notmatch '^(\d{4})_(\d{4})_(\d{4})$') { throw "Not a yyyy_MMdd_HHmm stamp: '$Stamp'" }
        '{0}.{1}.{2}' -f [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
    }
}

Describe 'Stamp to VERSION mapping' {
    It 'maps <Stamp> to <Expected>' -ForEach @(
        @{ Stamp = '2026_1022_1600'; Expected = '2026.1022.1600' }
        @{ Stamp = '2026_0905_0930'; Expected = '2026.905.930' }
        @{ Stamp = '2026_0101_0005'; Expected = '2026.101.5' }
        @{ Stamp = '2026_1231_0000'; Expected = '2026.1231.0' }
    ) {
        $v = ConvertTo-VersionFromStamp $Stamp
        $v | Should -Be $Expected
        $v | Should -Match $script:SemVer
    }
    It 'rejects a malformed stamp' {
        { ConvertTo-VersionFromStamp '2026.1022.1600' } | Should -Throw
    }
}

Describe 'Version files' {
    It 'VERSION is one X.Y.Z line (semver, no leading zeros) with a trailing newline' {
        $raw = [IO.File]::ReadAllText((Join-Path $script:Root 'VERSION'))
        $raw | Should -Match '\n$'
        $raw.TrimEnd("`r", "`n") | Should -Match $script:SemVer
    }
    It 'VERSION matches __version.info' {
        $stamp = [IO.File]::ReadAllText((Join-Path $script:Root '__version.info')).Trim()
        $expected = ConvertTo-VersionFromStamp $stamp
        ([IO.File]::ReadAllText((Join-Path $script:Root 'VERSION'))).Trim() | Should -Be $expected
    }
}
