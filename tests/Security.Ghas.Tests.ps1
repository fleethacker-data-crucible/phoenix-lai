# Pester 5: GHAS sprint fixes (path confinement, skill-name matcher). Static plus regex language checks.
# Run: Invoke-Pester -Path .\tests -Output Detailed
BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    function Read-Repo([string]$rel) {
        $rel = $rel.Replace('\', [IO.Path]::DirectorySeparatorChar)
        return [IO.File]::ReadAllText((Join-Path $script:Root $rel))
    }
    $script:OpenAi = Read-Repo 'src\WebServer.OpenAi.cs'
    $script:Skills = Read-Repo 'src\Skills.cs'
    $script:SkillApi = Read-Repo 'src\WebServer.Skills.cs'
}

Describe 'Model id path confinement' {
    It 'resolves a model id only inside the models folder' {
        $m = [regex]::Match($script:OpenAi, 'bool ModelFileInModelsDir\(string id\)\s*\{(?s)(?<body>.*?)\n        \}')
        $m.Success | Should -BeTrue
        $body = $m.Groups['body'].Value
        $body.Contains('name.Contains("..")') | Should -BeTrue
        $body.Contains('name.Contains("/")') | Should -BeTrue
        $body.Contains('name.Contains("\\")') | Should -BeTrue
        $body.Contains('name.Contains(":")') | Should -BeTrue
        $body.IndexOf('name.Contains(') | Should -BeLessThan $body.IndexOf('Path.GetFullPath')
        $body.IndexOf('Path.GetFullPath') | Should -BeLessThan $body.IndexOf('File.Exists(full)')
        $body.Contains('string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)') | Should -BeTrue
        $body.Contains('full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)') | Should -BeTrue
        $script:OpenAi.Contains('ModelFileInModelsDir(id)') | Should -BeTrue
        $script:OpenAi.Contains('ModelFileInModelsDir(askedModel)') | Should -BeTrue
        $script:OpenAi.Contains('File.Exists(Path.Combine(_modelsDir, id') | Should -BeFalse
        $script:OpenAi.Contains('File.Exists(Path.Combine(_modelsDir, askedModel') | Should -BeFalse
    }
}

Describe 'Skill name matcher' {
    It 'bounds the name, then uses a non-nested pattern with a timeout' {
        $script:SkillApi.Contains('Skills.IsSkillName(name)') | Should -BeTrue
        $script:SkillApi.Contains('(-[a-z0-9]+)*') | Should -BeFalse
        $m = [regex]::Match($script:Skills, 'bool IsSkillName\(string name\)\s*\{(?s)(?<body>.*?)\n        \}')
        $m.Success | Should -BeTrue
        $body = $m.Groups['body'].Value
        $body.IndexOf('name.Length > MaxName') | Should -BeGreaterThan -1
        $body.IndexOf('name.Length > MaxName') | Should -BeLessThan $body.IndexOf('NameMatches')
        $script:Skills.Contains('TimeSpan.FromSeconds(1)') | Should -BeTrue
        $script:Skills.Contains('(-[a-z0-9]+)*') | Should -BeFalse
        $pat = [regex]::Match($script:Skills, 'new Regex\(@"([^"]+)"').Groups[1].Value
        $pat | Should -Be '^[a-z0-9](-?[a-z0-9])*$'
    }

    It 'accepts the same names as the single-hyphen rule' {
        $opt = [System.Text.RegularExpressions.RegexOptions]::None
        $server = [regex]::new('^[a-z0-9](-?[a-z0-9])*$', $opt, [TimeSpan]::FromSeconds(1))
        $page = [regex]::new('^[a-z0-9]+(-[a-z0-9]+)*$', $opt, [TimeSpan]::FromSeconds(1))
        $samples = @('code-review', 'a', 'a-b-c', 'ab', 'a9', 'a-', '-a', 'a--b', 'A', 'a_b', '', '9', '-', '--a', 'a-b-')
        foreach ($s in $samples) { $server.IsMatch($s) | Should -Be $page.IsMatch($s) }
        $chars = @('a', 'b', '0', '-', 'A', '_')
        foreach ($n in 0..4) {
            $max = [math]::Pow($chars.Count, $n)
            for ($i = 0; $i -lt $max; $i++) {
                $s = ''
                $x = [int]$i
                for ($k = 0; $k -lt $n; $k++) {
                    $s += $chars[$x % $chars.Count]
                    $x = [int]($x / $chars.Count)
                }
                $server.IsMatch($s) | Should -Be $page.IsMatch($s)
            }
        }
    }
}
