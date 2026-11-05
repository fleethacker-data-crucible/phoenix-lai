# Pester 5 static checks: every custom modal window has dialog semantics and goes through the shared helper (html\js\modal.js).
# They read files and never start the app. The helper's behavior (focus in, Tab trap, Esc, inert background, focus return)
# was checked by hand in a headless Chromium page; see the pull request.
BeforeDiscovery {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $script:Pages = @('chat.html', 'vscode.html') | ForEach-Object { @{ Page = $_ } }
    $script:Modals = foreach ($page in 'chat.html', 'vscode.html') {
        foreach ($id in 'settingsModal', 'modelModal', 'provModal', 'downModal') { @{ Page = $page; Id = $id } }
    }
}

BeforeAll {
    $script:Html = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'html'
    function script:Read-Html([string]$rel) { [IO.File]::ReadAllText((Join-Path $script:Html $rel)) }
}

Describe 'Modal windows: dialog semantics' {
    It '<Page> <Id> has role=dialog, aria-modal and a name from its heading' -ForEach $script:Modals {
        $t = Read-Html $Page
        $m = [regex]::Match($t, '<div class="modal" id="' + $Id + '">\s*<div class="window[^"]*"([^>]*)>')
        $m.Success | Should -BeTrue
        $m.Groups[1].Value | Should -Match 'role="dialog"'
        $m.Groups[1].Value | Should -Match 'aria-modal="true"'
        $label = [regex]::Match($m.Groups[1].Value, 'aria-labelledby="([^"]+)"')
        $label.Success | Should -BeTrue
        $t | Should -Match ('<h2 id="' + $label.Groups[1].Value + '"')
    }
    It 'the skill builder window has role=dialog, aria-modal and aria-labelledby' {
        $t = Read-Html 'js\skills.js'
        $t | Should -Match 'class="window settings skillwin" role="dialog" aria-modal="true" aria-labelledby="skTitle"'
        $t | Should -Match '<h2 id="skTitle"'
    }
}

Describe 'Modal windows: shared helper' {
    It '<Page> loads modal.js before chat.js' -ForEach $script:Pages {
        $t = Read-Html $Page
        $a = $t.IndexOf('js/modal.js'); $b = $t.IndexOf('js/chat.js')
        $a | Should -BeGreaterThan -1
        $a | Should -BeLessThan $b
    }
    It 'no script toggles a .modal window directly' {
        foreach ($f in 'chat.js', 'providers.js', 'skills.js') {
            $t = Read-Html "js\$f"
            $t | Should -Not -Match "(settingsModal|modelModal|provModal|downModal|\bmodal)['`"\)]*\s*\.classList\.(add|remove)\('show'\)"
            $t | Should -Not -Match "querySelectorAll\('\.modal\.show'\)\.forEach"
            $t | Should -Match 'Modal\.(open|close)'
        }
    }
    It 'every modal is opened through Modal.open with a close function' {
        (Read-Html 'js\chat.js') | Should -Match "Modal\.open\(\`$\('settingsModal'\)"
        (Read-Html 'js\chat.js') | Should -Match "Modal\.open\(\`$\('modelModal'\)"
        (Read-Html 'js\providers.js') | Should -Match "Modal\.open\(\`$\('provModal'\)"
        (Read-Html 'js\providers.js') | Should -Match "Modal\.open\(\`$\('downModal'\)"
        (Read-Html 'js\skills.js') | Should -Match 'Modal\.open\(modal,'
    }
    It 'the helper implements focus move, Tab trap, Escape, inert background, scroll lock and focus return' {
        $t = Read-Html 'js\modal.js'
        $t | Should -Match '\.focus\(\)'
        $t | Should -Match "e\.key === 'Tab'"
        $t | Should -Match "e\.key === 'Escape'"
        $t | Should -Match '\.inert = true'
        $t | Should -Match "aria-hidden', 'true'"
        $t | Should -Match "overflow = 'hidden'"
        $t | Should -Match 'opener'
    }
}
