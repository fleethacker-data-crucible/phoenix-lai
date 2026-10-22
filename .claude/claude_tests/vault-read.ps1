# Dev/QA only: prints the key stored in Windows Credential Manager for a Phoenix client connection id, or <none>.
#   powershell.exe -NoProfile -File .claude\claude_tests\vault-read.ps1 <connection id>
param([Parameter(Mandatory)][string]$Id)
[void][Windows.Security.Credentials.PasswordVault, Windows.Security.Credentials, ContentType = WindowsRuntime]
$v = New-Object Windows.Security.Credentials.PasswordVault
try { $c = $v.Retrieve('PhoenixLAI.Connections', $Id); $c.RetrievePassword(); $c.Password } catch { '<none>' }
