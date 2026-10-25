# Pester 5: VERSION (read by the edge-ops release-zip workflow) must match __version.info (read by WebServer.AppVersion).
# __version.info is Package.ps1's yyyy_MMdd_HHmm stamp; VERSION is the same value as X.Y.Z = yyyy.MMdd.HHmm with no leading zeros.
# Run: Invoke-Pester -Path .\tests -Output Detailed
BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

Describe 'Version files' {
    It 'VERSION is a plain X.Y.Z line' {
        $raw = [IO.File]::ReadAllText((Join-Path $script:Root 'VERSION'))
        $raw | Should -Match '^\d+\.\d+\.\d+\r?\n$'
    }
    It 'VERSION matches __version.info' {
        $stamp = [IO.File]::ReadAllText((Join-Path $script:Root '__version.info')).Trim()
        $stamp | Should -Match '^\d{4}_\d{4}_\d{4}$'
        $p = $stamp.Split('_')
        $expected = '{0}.{1}.{2}' -f [int]$p[0], [int]$p[1], [int]$p[2]
        ([IO.File]::ReadAllText((Join-Path $script:Root 'VERSION'))).Trim() | Should -Be $expected
    }
}
