# Windows PowerShell 5.1 command reference

Every command that ships with Windows PowerShell 5.1 (powershell.exe): 423 commands in 21 modules, each with its syntax, a short description and an example.

**How this was verified:** the command list and every syntax line were read from Windows PowerShell 5.1.26100 itself (Get-Command -Syntax), and the command list was checked module by module against Microsoft's official PowerShell 5.1 reference (github.com/MicrosoftDocs/PowerShell-Docs, reference/5.1, plus the PowerShellGet/PackageManagement and DSC 1.1 docs). The 3 ISE commands only load inside PowerShell ISE, so their syntax comes from those docs. Descriptions and examples are written for this file.

**Reading the syntax:** `[-Name]` optional, `<Type>` value type, `[[-Name] <Type>]` optional positional, `{A | B}` pick one, `[<CommonParameters>]` = -Verbose, -Debug, -ErrorAction, -WarningAction, -InformationAction, -ErrorVariable, -WarningVariable, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable. Each separate syntax line is one parameter set.

## Modules

- [Microsoft.PowerShell.Core](#microsoft-powershell-core) (65)
- [Microsoft.PowerShell.Management](#microsoft-powershell-management) (89)
- [Microsoft.PowerShell.Utility](#microsoft-powershell-utility) (107)
- [Microsoft.PowerShell.Security](#microsoft-powershell-security) (15)
- [Microsoft.PowerShell.Diagnostics](#microsoft-powershell-diagnostics) (5)
- [Microsoft.PowerShell.Host](#microsoft-powershell-host) (2)
- [Microsoft.PowerShell.Archive](#microsoft-powershell-archive) (2)
- [Microsoft.PowerShell.LocalAccounts](#microsoft-powershell-localaccounts) (15)
- [Microsoft.PowerShell.ODataUtils](#microsoft-powershell-odatautils) (1)
- [Microsoft.PowerShell.Operation.Validation](#microsoft-powershell-operation-validation) (2)
- [Microsoft.WSMan.Management](#microsoft-wsman-management) (13)
- [CimCmdlets](#cimcmdlets) (14)
- [PackageManagement](#packagemanagement) (13)
- [PowerShellGet](#powershellget) (25)
- [PSDesiredStateConfiguration](#psdesiredstateconfiguration) (17)
- [PSDiagnostics](#psdiagnostics) (10)
- [PSReadLine](#psreadline) (6)
- [PSScheduledJob](#psscheduledjob) (16)
- [PSWorkflow](#psworkflow) (2)
- [PSWorkflowUtility](#psworkflowutility) (1)
- [ISE](#ise) (3)

## Microsoft.PowerShell.Core

### Add-History

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Adds entries (for example from a saved history file) to the current session's command history.

Syntax:

```powershell
Add-History [[-InputObject] <psobject[]>] [-Passthru] [<CommonParameters>]
```

Example:

```powershell
Import-Csv .\history.csv | Add-History
```

### Add-PSSnapin

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: asnp

Loads a registered Windows PowerShell snap-in (the older, pre-module extension format) into the session.

Syntax:

```powershell
Add-PSSnapin [-Name] <string[]> [-PassThru] [<CommonParameters>]
```

Example:

```powershell
Add-PSSnapin Microsoft.Exchange.Management.PowerShell.SnapIn
```

### Clear-History

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: clhy

Removes entries from the session's command history, all of them or by ID, count or command text.

Syntax:

```powershell
Clear-History [[-Id] <int[]>] [[-Count] <int>] [-Newest] [-WhatIf] [-Confirm] [<CommonParameters>]

Clear-History [[-Count] <int>] [-CommandLine <string[]>] [-Newest] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Clear-History -Count 10 -Newest
```

### Clear-Host

PowerShell 5.1 · module Microsoft.PowerShell.Core · function · aliases: clear, cls

Clears the console screen. Aliases: cls, clear.

Syntax:

```powershell
Clear-Host
```

Example:

```powershell
Clear-Host
```

### Connect-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: cnsn

Reconnects to remote sessions (PSSessions) that were disconnected, so you can keep using them.

Syntax:

```powershell
Connect-PSSession -Name <string[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-Session] <PSSession[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession -ComputerName <string[]> -InstanceId <guid[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-ComputerName] <string[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-ConnectionUri] <uri[]> [-ConfigurationName <string>] [-AllowRedirection] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-ConnectionUri] <uri[]> -InstanceId <guid[]> [-ConfigurationName <string>] [-AllowRedirection] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession -InstanceId <guid[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-Id] <int[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-PSSession -ComputerName Server01 | Connect-PSSession
```

### Debug-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Attaches the debugger to a running job (background, remote or workflow) so you can step through its script.

Syntax:

```powershell
Debug-Job [-Job] <Job> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Job [-Name] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Job [-Id] <int> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Job [-InstanceId] <guid> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Debug-Job -Id 3
```

### Disable-PSRemoting

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Stops the computer's session configurations from accepting remote connections (local access still works).

Syntax:

```powershell
Disable-PSRemoting [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Disable-PSRemoting -Force
```

### Disable-PSSessionConfiguration

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Denies access to one or more registered session configurations (endpoints) on this computer.

Syntax:

```powershell
Disable-PSSessionConfiguration [[-Name] <string[]>] [-Force] [-NoServiceRestart] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Disable-PSSessionConfiguration -Name Microsoft.PowerShell -Force
```

### Disconnect-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: dnsn

Disconnects from a remote session but leaves it running on the remote computer, so you can reconnect later.

Syntax:

```powershell
Disconnect-PSSession [-Session] <PSSession[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Disconnect-PSSession [-Id] <int[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Disconnect-PSSession -Name <string[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Disconnect-PSSession -InstanceId <guid[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-PSSession -Name Work | Disconnect-PSSession
```

### Enable-PSRemoting

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Configures this computer to receive remote PowerShell commands (starts WinRM, creates listeners and firewall rules).

Syntax:

```powershell
Enable-PSRemoting [-Force] [-SkipNetworkProfileCheck] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-PSRemoting -Force
```

### Enable-PSSessionConfiguration

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Re-enables session configurations that were disabled, allowing users to connect to them again.

Syntax:

```powershell
Enable-PSSessionConfiguration [[-Name] <string[]>] [-Force] [-SecurityDescriptorSddl <string>] [-SkipNetworkProfileCheck] [-NoServiceRestart] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-PSSessionConfiguration -Name Microsoft.PowerShell
```

### Enter-PSHostProcess

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Connects to another local process that hosts PowerShell (by ID or name) for interactive debugging.

Syntax:

```powershell
Enter-PSHostProcess [-Id] <int> [[-AppDomainName] <string>] [<CommonParameters>]

Enter-PSHostProcess [-Process] <Process> [[-AppDomainName] <string>] [<CommonParameters>]

Enter-PSHostProcess [-Name] <string> [[-AppDomainName] <string>] [<CommonParameters>]

Enter-PSHostProcess [-HostProcessInfo] <PSHostProcessInfo> [[-AppDomainName] <string>] [<CommonParameters>]
```

Example:

```powershell
Enter-PSHostProcess -Id 4321
```

### Enter-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: etsn

Starts an interactive session with a remote computer; commands you type run there until Exit-PSSession.

Syntax:

```powershell
Enter-PSSession [-ComputerName] <string> [-EnableNetworkAccess] [-Credential <pscredential>] [-ConfigurationName <string>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Enter-PSSession [[-Session] <PSSession>] [<CommonParameters>]

Enter-PSSession [[-ConnectionUri] <uri>] [-EnableNetworkAccess] [-Credential <pscredential>] [-ConfigurationName <string>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Enter-PSSession [-InstanceId <guid>] [<CommonParameters>]

Enter-PSSession [[-Id] <int>] [<CommonParameters>]

Enter-PSSession [-Name <string>] [<CommonParameters>]

Enter-PSSession [-VMId] <guid> [-Credential] <pscredential> [-ConfigurationName <string>] [<CommonParameters>]

Enter-PSSession [-VMName] <string> [-Credential] <pscredential> [-ConfigurationName <string>] [<CommonParameters>]

Enter-PSSession [-ContainerId] <string> [-ConfigurationName <string>] [-RunAsAdministrator] [<CommonParameters>]
```

Example:

```powershell
Enter-PSSession -ComputerName Server01 -Credential (Get-Credential)
```

### Exit-PSHostProcess

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Leaves the process session started by Enter-PSHostProcess.

Syntax:

```powershell
Exit-PSHostProcess [<CommonParameters>]
```

Example:

```powershell
Exit-PSHostProcess
```

### Exit-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: exsn

Ends the interactive remote session started by Enter-PSSession and returns to the local prompt.

Syntax:

```powershell
Exit-PSSession [<CommonParameters>]
```

Example:

```powershell
Exit-PSSession
```

### Export-Console

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Saves the names of the snap-ins loaded in the session to a console file (.psc1).

Syntax:

```powershell
Export-Console [[-Path] <string>] [-Force] [-NoClobber] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Export-Console -Path .\MySession.psc1
```

### Export-ModuleMember

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Inside a script module, declares which functions, variables, aliases and cmdlets the module makes public.

Syntax:

```powershell
Export-ModuleMember [[-Function] <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [<CommonParameters>]
```

Example:

```powershell
Export-ModuleMember -Function Get-Report, Set-Report -Alias gr
```

### ForEach-Object

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: %, foreach

Runs a script block once for each object coming down the pipeline ($_ is the current object). Alias: foreach, %.

Syntax:

```powershell
ForEach-Object [-Process] <scriptblock[]> [-InputObject <psobject>] [-Begin <scriptblock>] [-End <scriptblock>] [-RemainingScripts <scriptblock[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

ForEach-Object [-MemberName] <string> [-InputObject <psobject>] [-ArgumentList <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem *.log | ForEach-Object { $_.Name.ToUpper() }
```

### Get-Command

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: gcm

Lists the commands available in the session (cmdlets, functions, aliases, scripts, programs), filtered by name, verb, noun or module.

Syntax:

```powershell
Get-Command [[-ArgumentList] <Object[]>] [-Verb <string[]>] [-Noun <string[]>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-TotalCount <int>] [-Syntax] [-ShowCommandInfo] [-All] [-ListImported] [-ParameterName <string[]>] [-ParameterType <PSTypeName[]>] [<CommonParameters>]

Get-Command [[-Name] <string[]>] [[-ArgumentList] <Object[]>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-CommandType <CommandTypes>] [-TotalCount <int>] [-Syntax] [-ShowCommandInfo] [-All] [-ListImported] [-ParameterName <string[]>] [-ParameterType <PSTypeName[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Command -Verb Get -Module Microsoft.PowerShell.Management
```

### Get-Help

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Shows help for a command or concept (about_ topics), with options for examples, full detail or a single parameter.

Syntax:

```powershell
Get-Help [[-Name] <string>] [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [-Full] [<CommonParameters>]

Get-Help [[-Name] <string>] -Detailed [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -Examples [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -Parameter <string> [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -Online [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -ShowWindow [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Help Get-Process -Examples
```

### Get-History

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: ghy, h, history

Lists the commands entered in the current session, with their IDs and run times.

Syntax:

```powershell
Get-History [[-Id] <long[]>] [[-Count] <int>] [<CommonParameters>]
```

Example:

```powershell
Get-History -Count 5
```

### Get-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: gjb

Lists the background jobs in the current session and their state (Running, Completed, Failed...).

Syntax:

```powershell
Get-Job [[-Id] <int[]>] [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-InstanceId] <guid[]> [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-Name] <string[]> [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-State] <JobState> [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [-Command <string[]>] [<CommonParameters>]

Get-Job [-Filter] <hashtable> [<CommonParameters>]
```

Example:

```powershell
Get-Job -State Running
```

### Get-Module

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: gmo

Lists the modules imported in the session, or with -ListAvailable every module that can be imported.

Syntax:

```powershell
Get-Module [[-Name] <string[]>] [-FullyQualifiedName <ModuleSpecification[]>] [-All] [<CommonParameters>]

Get-Module [[-Name] <string[]>] -PSSession <PSSession> [-FullyQualifiedName <ModuleSpecification[]>] [-ListAvailable] [-PSEdition <string>] [-Refresh] [<CommonParameters>]

Get-Module [[-Name] <string[]>] -ListAvailable [-FullyQualifiedName <ModuleSpecification[]>] [-All] [-PSEdition <string>] [-Refresh] [<CommonParameters>]

Get-Module [[-Name] <string[]>] -CimSession <CimSession> [-FullyQualifiedName <ModuleSpecification[]>] [-ListAvailable] [-Refresh] [-CimResourceUri <uri>] [-CimNamespace <string>] [<CommonParameters>]
```

Example:

```powershell
Get-Module -ListAvailable -Name Az*
```

### Get-PSHostProcessInfo

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Lists the local processes that host a PowerShell engine (candidates for Enter-PSHostProcess).

Syntax:

```powershell
Get-PSHostProcessInfo [[-Name] <string[]>] [<CommonParameters>]

Get-PSHostProcessInfo [-Process] <Process[]> [<CommonParameters>]

Get-PSHostProcessInfo [-Id] <int[]> [<CommonParameters>]
```

Example:

```powershell
Get-PSHostProcessInfo -Name powershell
```

### Get-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: gsn

Lists the PSSessions created in this session, or the sessions on a remote computer.

Syntax:

```powershell
Get-PSSession [-Name <string[]>] [<CommonParameters>]

Get-PSSession [-ComputerName] <string[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession [-ComputerName] <string[]> -InstanceId <guid[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession [-ConnectionUri] <uri[]> -InstanceId <guid[]> [-ConfigurationName <string>] [-AllowRedirection] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession [-ConnectionUri] <uri[]> [-ConfigurationName <string>] [-AllowRedirection] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession -ContainerId <string[]> [-ConfigurationName <string>] [-Name <string[]>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -InstanceId <guid[]> -ContainerId <string[]> [-ConfigurationName <string>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -VMId <guid[]> [-ConfigurationName <string>] [-Name <string[]>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -InstanceId <guid[]> -VMId <guid[]> [-ConfigurationName <string>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -VMName <string[]> [-ConfigurationName <string>] [-Name <string[]>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -InstanceId <guid[]> -VMName <string[]> [-ConfigurationName <string>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession [-InstanceId <guid[]>] [<CommonParameters>]

Get-PSSession [-Id] <int[]> [<CommonParameters>]
```

Example:

```powershell
Get-PSSession -ComputerName Server01
```

### Get-PSSessionCapability

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Shows which commands a given user can run in a constrained (JEA) session configuration.

Syntax:

```powershell
Get-PSSessionCapability [-ConfigurationName] <string> [-Username] <string> [-Full] [<CommonParameters>]
```

Example:

```powershell
Get-PSSessionCapability -ConfigurationName HelpDesk -Username CONTOSO\Ann
```

### Get-PSSessionConfiguration

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Lists the session configurations (remoting endpoints) registered on this computer.

Syntax:

```powershell
Get-PSSessionConfiguration [[-Name] <string[]>] [-Force] [<CommonParameters>]
```

Example:

```powershell
Get-PSSessionConfiguration | Format-Table Name, Permission
```

### Get-PSSnapin

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: gsnp

Lists the snap-ins loaded in the session, or with -Registered all snap-ins installed on the computer.

Syntax:

```powershell
Get-PSSnapin [[-Name] <string[]>] [-Registered] [<CommonParameters>]
```

Example:

```powershell
Get-PSSnapin -Registered
```

### Get-Verb

PowerShell 5.1 · module Microsoft.PowerShell.Core · function

Lists the approved PowerShell verbs and their groups, for naming your own commands.

Syntax:

```powershell
Get-Verb [[-verb] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Verb | Where-Object Group -eq Data
```

### Import-Module

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: ipmo

Loads a module (by name or path) into the session so its commands become available.

Syntax:

```powershell
Import-Module [-Name] <string[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-MinimumVersion <version>] [-MaximumVersion <string>] [-RequiredVersion <version>] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-Name] <string[]> -PSSession <PSSession> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-MinimumVersion <version>] [-MaximumVersion <string>] [-RequiredVersion <version>] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-Name] <string[]> -CimSession <CimSession> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-MinimumVersion <version>] [-MaximumVersion <string>] [-RequiredVersion <version>] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [-CimResourceUri <uri>] [-CimNamespace <string>] [<CommonParameters>]

Import-Module [-FullyQualifiedName] <ModuleSpecification[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-FullyQualifiedName] <ModuleSpecification[]> -PSSession <PSSession> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-Assembly] <Assembly[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-ModuleInfo] <psmoduleinfo[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]
```

Example:

```powershell
Import-Module .\Tools\MyTools.psm1 -Force
```

### Invoke-Command

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: icm

Runs a script block on local or remote computers (or in PSSessions) and returns the results; can run as a job.

Syntax:

```powershell
Invoke-Command [-ScriptBlock] <scriptblock> [-NoNewScope] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-Session] <PSSession[]>] [-ScriptBlock] <scriptblock> [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-Session] <PSSession[]>] [-FilePath] <string> [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-ComputerName] <string[]>] [-ScriptBlock] <scriptblock> [-Credential <pscredential>] [-Port <int>] [-UseSSL] [-ConfigurationName <string>] [-ApplicationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-SessionName <string[]>] [-HideComputerName] [-JobName <string>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [-CertificateThumbprint <string>] [<CommonParameters>]

Invoke-Command [[-ComputerName] <string[]>] [-FilePath] <string> [-Credential <pscredential>] [-Port <int>] [-UseSSL] [-ConfigurationName <string>] [-ApplicationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-SessionName <string[]>] [-HideComputerName] [-JobName <string>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-ConnectionUri] <uri[]>] [-ScriptBlock] <scriptblock> [-Credential <pscredential>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-HideComputerName] [-JobName <string>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [-CertificateThumbprint <string>] [<CommonParameters>]

Invoke-Command [[-ConnectionUri] <uri[]>] [-FilePath] <string> [-Credential <pscredential>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-HideComputerName] [-JobName <string>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-VMId] <guid[]> [-ScriptBlock] <scriptblock> -Credential <pscredential> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-ScriptBlock] <scriptblock> -Credential <pscredential> -VMName <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-VMId] <guid[]> [-FilePath] <string> -Credential <pscredential> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-FilePath] <string> -Credential <pscredential> -VMName <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-ScriptBlock] <scriptblock> -ContainerId <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-RunAsAdministrator] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-FilePath] <string> -ContainerId <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-RunAsAdministrator] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]
```

Example:

```powershell
Invoke-Command -ComputerName Server01, Server02 -ScriptBlock { Get-Service Spooler }
```

### Invoke-History

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: ihy, r

Runs a command again from the session history, by ID or by the start of its text. Alias: r.

Syntax:

```powershell
Invoke-History [[-Id] <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Invoke-History -Id 12
```

### New-Module

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: nmo

Creates a dynamic module in memory from a script block; useful for quick, temporary function libraries.

Syntax:

```powershell
New-Module [-ScriptBlock] <scriptblock> [-Function <string[]>] [-Cmdlet <string[]>] [-ReturnResult] [-AsCustomObject] [-ArgumentList <Object[]>] [<CommonParameters>]

New-Module [-Name] <string> [-ScriptBlock] <scriptblock> [-Function <string[]>] [-Cmdlet <string[]>] [-ReturnResult] [-AsCustomObject] [-ArgumentList <Object[]>] [<CommonParameters>]
```

Example:

```powershell
New-Module -Name Temp -ScriptBlock { function Hello { 'hi' } } | Import-Module
```

### New-ModuleManifest

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Creates a module manifest (.psd1) that describes a module: version, author, files and exports.

Syntax:

```powershell
New-ModuleManifest [-Path] <string> [-NestedModules <Object[]>] [-Guid <guid>] [-Author <string>] [-CompanyName <string>] [-Copyright <string>] [-RootModule <string>] [-ModuleVersion <version>] [-Description <string>] [-ProcessorArchitecture <ProcessorArchitecture>] [-PowerShellVersion <version>] [-ClrVersion <version>] [-DotNetFrameworkVersion <version>] [-PowerShellHostName <string>] [-PowerShellHostVersion <version>] [-RequiredModules <Object[]>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-ScriptsToProcess <string[]>] [-RequiredAssemblies <string[]>] [-FileList <string[]>] [-ModuleList <Object[]>] [-FunctionsToExport <string[]>] [-AliasesToExport <string[]>] [-VariablesToExport <string[]>] [-CmdletsToExport <string[]>] [-DscResourcesToExport <string[]>] [-CompatiblePSEditions <string[]>] [-PrivateData <Object>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string>] [-HelpInfoUri <string>] [-PassThru] [-DefaultCommandPrefix <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-ModuleManifest -Path .\MyTools.psd1 -RootModule MyTools.psm1 -ModuleVersion 1.0.0 -Author 'Ann'
```

### New-PSRoleCapabilityFile

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Creates a role capability file (.psrc) that lists the commands a JEA role may use.

Syntax:

```powershell
New-PSRoleCapabilityFile [-Path] <string> [-Guid <guid>] [-Author <string>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-ModulesToImport <Object[]>] [-VisibleAliases <string[]>] [-VisibleCmdlets <Object[]>] [-VisibleFunctions <Object[]>] [-VisibleExternalCommands <string[]>] [-VisibleProviders <string[]>] [-ScriptsToProcess <string[]>] [-AliasDefinitions <IDictionary[]>] [-FunctionDefinitions <IDictionary[]>] [-VariableDefinitions <Object>] [-EnvironmentVariables <IDictionary>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-AssembliesToLoad <string[]>] [<CommonParameters>]
```

Example:

```powershell
New-PSRoleCapabilityFile -Path .\HelpDesk.psrc -VisibleCmdlets Get-Service, Restart-Service
```

### New-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: nsn

Opens a persistent connection (PSSession) to a local or remote computer that several commands can reuse.

Syntax:

```powershell
New-PSSession [[-ComputerName] <string[]>] [-Credential <pscredential>] [-Name <string[]>] [-EnableNetworkAccess] [-ConfigurationName <string>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-ThrottleLimit <int>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

New-PSSession [-ConnectionUri] <uri[]> [-Credential <pscredential>] [-Name <string[]>] [-EnableNetworkAccess] [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

New-PSSession [-VMId] <guid[]> -Credential <pscredential> [-Name <string[]>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [<CommonParameters>]

New-PSSession -Credential <pscredential> -VMName <string[]> [-Name <string[]>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [<CommonParameters>]

New-PSSession [[-Session] <PSSession[]>] [-Name <string[]>] [-EnableNetworkAccess] [-ThrottleLimit <int>] [<CommonParameters>]

New-PSSession -ContainerId <string[]> [-Name <string[]>] [-ConfigurationName <string>] [-RunAsAdministrator] [-ThrottleLimit <int>] [<CommonParameters>]
```

Example:

```powershell
$s = New-PSSession -ComputerName Server01
```

### New-PSSessionConfigurationFile

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: npssc

Creates a session configuration file (.pssc) that defines a remoting endpoint (language mode, roles, visible commands).

Syntax:

```powershell
New-PSSessionConfigurationFile [-Path] <string> [-SchemaVersion <version>] [-Guid <guid>] [-Author <string>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-SessionType <SessionType>] [-TranscriptDirectory <string>] [-RunAsVirtualAccount] [-RunAsVirtualAccountGroups <string[]>] [-MountUserDrive] [-UserDriveMaximumSize <long>] [-GroupManagedServiceAccount <string>] [-ScriptsToProcess <string[]>] [-RoleDefinitions <IDictionary>] [-RequiredGroups <IDictionary>] [-LanguageMode <PSLanguageMode>] [-ExecutionPolicy <ExecutionPolicy>] [-PowerShellVersion <version>] [-ModulesToImport <Object[]>] [-VisibleAliases <string[]>] [-VisibleCmdlets <Object[]>] [-VisibleFunctions <Object[]>] [-VisibleExternalCommands <string[]>] [-VisibleProviders <string[]>] [-AliasDefinitions <IDictionary[]>] [-FunctionDefinitions <IDictionary[]>] [-VariableDefinitions <Object>] [-EnvironmentVariables <IDictionary>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-AssembliesToLoad <string[]>] [-Full] [<CommonParameters>]
```

Example:

```powershell
New-PSSessionConfigurationFile -Path .\JEA.pssc -SessionType RestrictedRemoteServer
```

### New-PSSessionOption

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Creates an object with advanced PSSession options (timeouts, proxy, culture) to pass to New-PSSession and similar.

Syntax:

```powershell
New-PSSessionOption [-MaximumRedirection <int>] [-NoCompression] [-NoMachineProfile] [-Culture <cultureinfo>] [-UICulture <cultureinfo>] [-MaximumReceivedDataSizePerCommand <int>] [-MaximumReceivedObjectSize <int>] [-OutputBufferingMode <OutputBufferingMode>] [-MaxConnectionRetryCount <int>] [-ApplicationArguments <psprimitivedictionary>] [-OpenTimeout <int>] [-CancelTimeout <int>] [-IdleTimeout <int>] [-ProxyAccessType <ProxyAccessType>] [-ProxyAuthentication <AuthenticationMechanism>] [-ProxyCredential <pscredential>] [-SkipCACheck] [-SkipCNCheck] [-SkipRevocationCheck] [-OperationTimeout <int>] [-NoEncryption] [-UseUTF16] [-IncludePortInSPN] [<CommonParameters>]
```

Example:

```powershell
$o = New-PSSessionOption -OpenTimeout 60000; New-PSSession -ComputerName Server01 -SessionOption $o
```

### New-PSTransportOption

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Creates an object with advanced transport options (quotas, idle timeout) for a session configuration.

Syntax:

```powershell
New-PSTransportOption [-MaxIdleTimeoutSec <int>] [-ProcessIdleTimeoutSec <int>] [-MaxSessions <int>] [-MaxConcurrentCommandsPerSession <int>] [-MaxSessionsPerUser <int>] [-MaxMemoryPerSessionMB <int>] [-MaxProcessesPerSession <int>] [-MaxConcurrentUsers <int>] [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [<CommonParameters>]
```

Example:

```powershell
$t = New-PSTransportOption -IdleTimeoutSec 7200; Set-PSSessionConfiguration -Name Microsoft.PowerShell -TransportOption $t
```

### Out-Default

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Sends output to the default formatter and output cmdlet; PowerShell adds it to the end of every pipeline automatically.

Syntax:

```powershell
Out-Default [-Transcript] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Out-Default
```

### Out-Host

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: oh

Writes output to the console, optionally one screen at a time with -Paging.

Syntax:

```powershell
Out-Host [-Paging] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Help about_Operators -Full | Out-Host -Paging
```

### Out-Null

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Discards output instead of sending it down the pipeline or showing it.

Syntax:

```powershell
Out-Null [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
New-Item -ItemType Directory .\Temp | Out-Null
```

### Receive-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rcjb

Gets the results produced so far by background jobs; -Keep leaves them available for later.

Syntax:

```powershell
Receive-Job [-Job] <Job[]> [[-Location] <string[]>] [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Job] <Job[]> [[-ComputerName] <string[]>] [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Job] <Job[]> [[-Session] <PSSession[]>] [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Name] <string[]> [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-InstanceId] <guid[]> [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Id] <int[]> [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]
```

Example:

```powershell
Receive-Job -Id 5 -Keep
```

### Receive-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rcsn

Gets the results of commands that ran in a disconnected session and reconnects to it.

Syntax:

```powershell
Receive-PSSession [-Session] <PSSession> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-Id] <int> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ComputerName] <string> -Name <string> [-ApplicationName <string>] [-ConfigurationName <string>] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ComputerName] <string> -InstanceId <guid> [-ApplicationName <string>] [-ConfigurationName <string>] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ConnectionUri] <uri> -InstanceId <guid> [-ConfigurationName <string>] [-AllowRedirection] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ConnectionUri] <uri> -Name <string> [-ConfigurationName <string>] [-AllowRedirection] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-InstanceId] <guid> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-Name] <string> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Receive-PSSession -ComputerName Server01 -Name Nightly
```

### Register-ArgumentCompleter

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Registers a script block that supplies tab-completion values for a command's parameter.

Syntax:

```powershell
Register-ArgumentCompleter -CommandName <string[]> -ScriptBlock <scriptblock> [-Native] [<CommonParameters>]

Register-ArgumentCompleter -ParameterName <string> -ScriptBlock <scriptblock> [-CommandName <string[]>] [<CommonParameters>]
```

Example:

```powershell
Register-ArgumentCompleter -CommandName Set-Color -ParameterName Name -ScriptBlock { 'Red','Green','Blue' }
```

### Register-PSSessionConfiguration

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Creates and registers a new session configuration (remoting endpoint) on this computer.

Syntax:

```powershell
Register-PSSessionConfiguration [-Name] <string> [-ProcessorArchitecture <string>] [-SessionType <PSSessionType>] [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-PSSessionConfiguration [-Name] <string> [-AssemblyName] <string> [-ConfigurationTypeName] <string> [-ProcessorArchitecture <string>] [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-PSSessionConfiguration [-Name] <string> -Path <string> [-ProcessorArchitecture <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-TransportOption <PSTransportOption>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Register-PSSessionConfiguration -Name HelpDesk -Path .\JEA.pssc
```

### Remove-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rjb

Deletes background jobs and their stored results from the session.

Syntax:

```powershell
Remove-Job [-Id] <int[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Job] <Job[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-InstanceId] <guid[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Name] <string[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Filter] <hashtable> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-State] <JobState> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Command <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-Job -State Completed | Remove-Job
```

### Remove-Module

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rmo

Unloads a module from the session (the module files are not deleted).

Syntax:

```powershell
Remove-Module [-Name] <string[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Module [-FullyQualifiedName] <ModuleSpecification[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Module [-ModuleInfo] <psmoduleinfo[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-Module MyTools
```

### Remove-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rsn

Closes PSSessions and frees their resources on the remote computer.

Syntax:

```powershell
Remove-PSSession [-Id] <int[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession [-Session] <PSSession[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -ContainerId <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -VMId <guid[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -VMName <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -InstanceId <guid[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -Name <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession [-ComputerName] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-PSSession | Remove-PSSession
```

### Remove-PSSnapin

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rsnp

Removes a snap-in from the current session (it stays installed).

Syntax:

```powershell
Remove-PSSnapin [-Name] <string[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-PSSnapin Microsoft.Exchange.Management.PowerShell.SnapIn
```

### Resume-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: rujb

Restarts workflow jobs that were suspended.

Syntax:

```powershell
Resume-Job [-Id] <int[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-Job] <Job[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-Name] <string[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-InstanceId] <guid[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-State] <JobState> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-Filter] <hashtable> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Resume-Job -Name Backup
```

### Save-Help

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Downloads the newest help files for modules to a folder, for installing on computers without internet.

Syntax:

```powershell
Save-Help [-DestinationPath] <string[]> [[-Module] <psmoduleinfo[]>] [[-UICulture] <cultureinfo[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [<CommonParameters>]

Save-Help [[-Module] <psmoduleinfo[]>] [[-UICulture] <cultureinfo[]>] -LiteralPath <string[]> [-FullyQualifiedModule <ModuleSpecification[]>] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [<CommonParameters>]
```

Example:

```powershell
Save-Help -DestinationPath \\Server\Help -Module * -Force
```

### Set-PSDebug

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Turns script tracing (-Trace 1/2), stepping (-Step) and strict variable checks on or off.

Syntax:

```powershell
Set-PSDebug [-Trace <int>] [-Step] [-Strict] [<CommonParameters>]

Set-PSDebug [-Off] [<CommonParameters>]
```

Example:

```powershell
Set-PSDebug -Trace 1
```

### Set-PSSessionConfiguration

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Changes the properties of a registered session configuration (permissions, startup script, quotas).

Syntax:

```powershell
Set-PSSessionConfiguration [-Name] <string> [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-PSSessionConfiguration [-Name] <string> [-AssemblyName] <string> [-ConfigurationTypeName] <string> [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-PSSessionConfiguration [-Name] <string> -Path <string> [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-TransportOption <PSTransportOption>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-PSSessionConfiguration -Name Microsoft.PowerShell -ShowSecurityDescriptorUI
```

### Set-StrictMode

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Makes the current scope stricter: errors for uninitialized variables, missing properties and bad function calls.

Syntax:

```powershell
Set-StrictMode -Version <version> [<CommonParameters>]

Set-StrictMode -Off [<CommonParameters>]
```

Example:

```powershell
Set-StrictMode -Version Latest
```

### Start-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: sajb

Starts a background job that runs a script block or script without blocking the console.

Syntax:

```powershell
Start-Job [-ScriptBlock] <scriptblock> [[-InitializationScript] <scriptblock>] [-Name <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-RunAs32] [-PSVersion <version>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Start-Job [-DefinitionName] <string> [[-DefinitionPath] <string>] [[-Type] <string>] [<CommonParameters>]

Start-Job [-FilePath] <string> [[-InitializationScript] <scriptblock>] [-Name <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-RunAs32] [-PSVersion <version>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Start-Job [[-InitializationScript] <scriptblock>] -LiteralPath <string> [-Name <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-RunAs32] [-PSVersion <version>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]
```

Example:

```powershell
Start-Job -Name Hash -ScriptBlock { Get-FileHash C:\Data\*.zip }
```

### Stop-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: spjb

Stops running background jobs.

Syntax:

```powershell
Stop-Job [-Id] <int[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-Job] <Job[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-Name] <string[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-InstanceId] <guid[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-State] <JobState> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-Filter] <hashtable> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Stop-Job -Name Hash
```

### Suspend-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: sujb

Pauses workflow jobs at the next checkpoint so they can be resumed later.

Syntax:

```powershell
Suspend-Job [-Id] <int[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-Job] <Job[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-Name] <string[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-Filter] <hashtable> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-State] <JobState> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-InstanceId] <guid[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Suspend-Job -Name Backup
```

### TabExpansion2

PowerShell 5.1 · module Microsoft.PowerShell.Core · function

Built-in function that computes tab completions for an input string; editors and hosts call it.

Syntax:

```powershell
TabExpansion2 [-inputScript] <string> [-cursorColumn] <int> [[-options] <hashtable>] [<CommonParameters>]

TabExpansion2 [-ast] <Ast> [-tokens] <Token[]> [-positionOfCursor] <IScriptPosition> [[-options] <hashtable>] [<CommonParameters>]
```

Example:

```powershell
TabExpansion2 -inputScript 'Get-Ch' -cursorColumn 6 | Select-Object -ExpandProperty CompletionMatches
```

### Test-ModuleManifest

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Checks that a module manifest (.psd1) is valid and the files it lists exist.

Syntax:

```powershell
Test-ModuleManifest [-Path] <string> [<CommonParameters>]
```

Example:

```powershell
Test-ModuleManifest .\MyTools.psd1
```

### Test-PSSessionConfigurationFile

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Validates the keys and values of a session configuration file (.pssc).

Syntax:

```powershell
Test-PSSessionConfigurationFile [-Path] <string> [<CommonParameters>]
```

Example:

```powershell
Test-PSSessionConfigurationFile -Path .\JEA.pssc
```

### Unregister-PSSessionConfiguration

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Deletes registered session configurations from the computer.

Syntax:

```powershell
Unregister-PSSessionConfiguration [-Name] <string> [-Force] [-NoServiceRestart] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Unregister-PSSessionConfiguration -Name HelpDesk -Force
```

### Update-Help

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet

Downloads and installs the newest help files for modules (needs internet or a -SourcePath share).

Syntax:

```powershell
Update-Help [[-Module] <string[]>] [[-SourcePath] <string[]>] [[-UICulture] <cultureinfo[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Recurse] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-Help [[-Module] <string[]>] [[-UICulture] <cultureinfo[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-LiteralPath <string[]>] [-Recurse] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-Help -Module Microsoft.PowerShell* -UICulture en-US
```

### Wait-Job

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: wjb

Waits until jobs finish (or a timeout passes) before continuing.

Syntax:

```powershell
Wait-Job [-Id] <int[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-Job] <Job[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-Name] <string[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-InstanceId] <guid[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-State] <JobState> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-Filter] <hashtable> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]
```

Example:

```powershell
Start-Job { Start-Sleep 5 } | Wait-Job -Timeout 30
```

### Where-Object

PowerShell 5.1 · module Microsoft.PowerShell.Core · cmdlet · aliases: ?, where

Filters pipeline objects, keeping those whose property values match a condition. Alias: where, ?.

Syntax:

```powershell
Where-Object [-Property] <string> [[-Value] <Object>] [-InputObject <psobject>] [-EQ] [<CommonParameters>]

Where-Object [-FilterScript] <scriptblock> [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotLike [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CEQ [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -GT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CGT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -LT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CLT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -GE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CGE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -LE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CLE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Like [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CLike [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotLike [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Match [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CMatch [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotMatch [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotMatch [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Contains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CContains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotContains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotContains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -In [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CIn [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotIn [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotIn [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Is [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -IsNot [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Service | Where-Object Status -eq 'Running'
```

## Microsoft.PowerShell.Management

### Add-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Joins the local (or a remote) computer to a domain or workgroup; a restart is needed to finish.

Syntax:

```powershell
Add-Computer [-DomainName] <string> -Credential <pscredential> [-ComputerName <string[]>] [-LocalCredential <pscredential>] [-UnjoinDomainCredential <pscredential>] [-OUPath <string>] [-Server <string>] [-Unsecure] [-Options <JoinOptions>] [-Restart] [-PassThru] [-NewName <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Add-Computer [-WorkgroupName] <string> [-ComputerName <string[]>] [-LocalCredential <pscredential>] [-Credential <pscredential>] [-Restart] [-PassThru] [-NewName <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Add-Computer -DomainName contoso.com -Credential CONTOSO\Admin -Restart
```

### Add-Content

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: ac

Appends text or other content to a file (creates the file if it does not exist). Alias: ac.

Syntax:

```powershell
Add-Content [-Path] <string[]> [-Value] <Object[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]

Add-Content [-Value] <Object[]> -LiteralPath <string[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]
```

Example:

```powershell
Add-Content -Path .\log.txt -Value "$(Get-Date) started"
```

### Checkpoint-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Creates a System Restore point on the local computer (needs an elevated session).

Syntax:

```powershell
Checkpoint-Computer [-Description] <string> [[-RestorePointType] <string>] [<CommonParameters>]
```

Example:

```powershell
Checkpoint-Computer -Description 'Before driver update' -RestorePointType MODIFY_SETTINGS
```

### Clear-Content

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: clc

Empties a file (or other item's content) without deleting the item. Alias: clc.

Syntax:

```powershell
Clear-Content [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string>] [<CommonParameters>]

Clear-Content -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string>] [<CommonParameters>]
```

Example:

```powershell
Clear-Content -Path .\log.txt
```

### Clear-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Deletes all entries from classic event logs on the local or remote computers.

Syntax:

```powershell
Clear-EventLog [-LogName] <string[]> [[-ComputerName] <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Clear-EventLog -LogName Application
```

### Clear-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: cli

Clears an item's value without deleting it (for example a variable or registry value). Alias: cli.

Syntax:

```powershell
Clear-Item [-Path] <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Clear-Item -LiteralPath <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Clear-Item -Path Env:\TEMPVAR
```

### Clear-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: clp

Clears the value of an item's property, keeping the property itself (often registry values). Alias: clp.

Syntax:

```powershell
Clear-ItemProperty [-Path] <string[]> [-Name] <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Clear-ItemProperty [-Name] <string> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Clear-ItemProperty -Path HKCU:\Software\MyApp -Name LastRun
```

### Clear-RecycleBin

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Empties the Recycle Bin for one drive or all drives.

Syntax:

```powershell
Clear-RecycleBin [[-DriveLetter] <string[]>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Clear-RecycleBin -DriveLetter C -Force
```

### Complete-Transaction

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Commits the active transaction (registry provider transactions started with Start-Transaction).

Syntax:

```powershell
Complete-Transaction [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Complete-Transaction
```

### Convert-Path

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: cvpa

Converts a PowerShell path (with drives like HKLM: or ~) into the provider's native path. Alias: cvpa.

Syntax:

```powershell
Convert-Path [-Path] <string[]> [-UseTransaction] [<CommonParameters>]

Convert-Path -LiteralPath <string[]> [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Convert-Path ~\Documents
```

### Copy-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: copy, cp, cpi

Copies files, folders, registry keys or other items to a new location. Aliases: copy, cp, cpi.

Syntax:

```powershell
Copy-Item [-Path] <string[]> [[-Destination] <string>] [-Container] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-FromSession <PSSession>] [-ToSession <PSSession>] [<CommonParameters>]

Copy-Item [[-Destination] <string>] -LiteralPath <string[]> [-Container] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-FromSession <PSSession>] [-ToSession <PSSession>] [<CommonParameters>]
```

Example:

```powershell
Copy-Item -Path .\src -Destination D:\Backup\src -Recurse -Force
```

### Copy-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: cpp

Copies a property and its value from one item to another (often between registry keys). Alias: cpp.

Syntax:

```powershell
Copy-ItemProperty [-Path] <string[]> [-Destination] <string> [-Name] <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Copy-ItemProperty [-Destination] <string> [-Name] <string> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Copy-ItemProperty -Path HKCU:\Software\A -Destination HKCU:\Software\B -Name Theme
```

### Debug-Process

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Attaches the registered debugger to running processes.

Syntax:

```powershell
Debug-Process [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Process [-Id] <int[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Process -InputObject <Process[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Debug-Process -Name notepad
```

### Disable-ComputerRestore

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Turns off System Restore on the given drives.

Syntax:

```powershell
Disable-ComputerRestore [-Drive] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Disable-ComputerRestore -Drive 'D:\'
```

### Enable-ComputerRestore

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Turns on System Restore on the given drives.

Syntax:

```powershell
Enable-ComputerRestore [-Drive] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-ComputerRestore -Drive 'C:\'
```

### Get-ChildItem

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: dir, gci, ls

Lists the files and folders (or registry keys, certificates...) in a location, optionally recursively and filtered. Aliases: dir, ls, gci.

Syntax:

```powershell
Get-ChildItem [[-Path] <string[]>] [[-Filter] <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Depth <uint32>] [-Force] [-Name] [-UseTransaction] [-Attributes <FlagsExpression[FileAttributes]>] [-FollowSymlink] [-Directory] [-File] [-Hidden] [-ReadOnly] [-System] [<CommonParameters>]

Get-ChildItem [[-Filter] <string>] -LiteralPath <string[]> [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Depth <uint32>] [-Force] [-Name] [-UseTransaction] [-Attributes <FlagsExpression[FileAttributes]>] [-FollowSymlink] [-Directory] [-File] [-Hidden] [-ReadOnly] [-System] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem -Path C:\Projects -Filter *.ps1 -Recurse -File
```

### Get-Clipboard

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gcb

Reads the current Windows clipboard (text, files, image) into PowerShell. Alias: gcb.

Syntax:

```powershell
Get-Clipboard [-Format <ClipboardFormat>] [-TextFormatType <TextDataFormat>] [-Raw] [<CommonParameters>]
```

Example:

```powershell
Get-Clipboard -Format Text
```

### Get-ComputerInfo

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gin

Returns one object with many operating system and hardware properties (OS version, BIOS, memory...).

Syntax:

```powershell
Get-ComputerInfo [[-Property] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-ComputerInfo -Property OsName, OsVersion, CsTotalPhysicalMemory
```

### Get-ComputerRestorePoint

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Lists the System Restore points on the local computer.

Syntax:

```powershell
Get-ComputerRestorePoint [[-RestorePoint] <int[]>] [<CommonParameters>]

Get-ComputerRestorePoint -LastStatus [<CommonParameters>]
```

Example:

```powershell
Get-ComputerRestorePoint | Select-Object SequenceNumber, Description, CreationTime
```

### Get-Content

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: cat, gc, type

Reads the content of a file (one string per line by default), or of other items. Aliases: cat, type, gc.

Syntax:

```powershell
Get-Content [-Path] <string[]> [-ReadCount <long>] [-TotalCount <long>] [-Tail <int>] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Delimiter <string>] [-Wait] [-Raw] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]

Get-Content -LiteralPath <string[]> [-ReadCount <long>] [-TotalCount <long>] [-Tail <int>] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Delimiter <string>] [-Wait] [-Raw] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]
```

Example:

```powershell
Get-Content -Path .\app.log -Tail 20 -Wait
```

### Get-ControlPanelItem

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Lists Control Panel items, filtered by name or category.

Syntax:

```powershell
Get-ControlPanelItem [[-Name] <string[]>] [-Category <string[]>] [<CommonParameters>]

Get-ControlPanelItem -CanonicalName <string[]> [-Category <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-ControlPanelItem -Category 'System and Security'
```

### Get-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Reads entries from classic event logs (Application, System, Security...) on local or remote computers.

Syntax:

```powershell
Get-EventLog [-LogName] <string> [[-InstanceId] <long[]>] [-ComputerName <string[]>] [-Newest <int>] [-After <datetime>] [-Before <datetime>] [-UserName <string[]>] [-Index <int[]>] [-EntryType <string[]>] [-Source <string[]>] [-Message <string>] [-AsBaseObject] [<CommonParameters>]

Get-EventLog [-ComputerName <string[]>] [-List] [-AsString] [<CommonParameters>]
```

Example:

```powershell
Get-EventLog -LogName System -EntryType Error -Newest 20
```

### Get-HotFix

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Lists the Windows updates (hotfixes) installed on local or remote computers.

Syntax:

```powershell
Get-HotFix [[-Id] <string[]>] [-ComputerName <string[]>] [-Credential <pscredential>] [<CommonParameters>]

Get-HotFix [-Description <string[]>] [-ComputerName <string[]>] [-Credential <pscredential>] [<CommonParameters>]
```

Example:

```powershell
Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 5
```

### Get-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gi

Gets the item at a path (a file, folder, registry key...) without listing its contents. Alias: gi.

Syntax:

```powershell
Get-Item [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]

Get-Item -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Item -Path C:\Windows\notepad.exe | Select-Object Name, Length, LastWriteTime
```

### Get-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gp

Gets the properties of an item, typically registry values or file attributes. Alias: gp.

Syntax:

```powershell
Get-ItemProperty [-Path] <string[]> [[-Name] <string[]>] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Get-ItemProperty [[-Name] <string[]>] -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name ProductName
```

### Get-ItemPropertyValue

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gpv

Gets just the value of one or more properties of an item. Alias: gpv.

Syntax:

```powershell
Get-ItemPropertyValue [[-Path] <string[]>] [-Name] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Get-ItemPropertyValue [-Name] <string[]> -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuild
```

### Get-Location

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gl, pwd

Shows the current working location, or the location stack. Aliases: pwd, gl.

Syntax:

```powershell
Get-Location [-PSProvider <string[]>] [-PSDrive <string[]>] [-UseTransaction] [<CommonParameters>]

Get-Location [-Stack] [-StackName <string[]>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Get-Location
```

### Get-Process

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gps, ps

Lists running processes on the local (or, in 5.1, a remote) computer. Aliases: ps, gps.

Syntax:

```powershell
Get-Process [[-Name] <string[]>] [-ComputerName <string[]>] [-Module] [-FileVersionInfo] [<CommonParameters>]

Get-Process [[-Name] <string[]>] -IncludeUserName [<CommonParameters>]

Get-Process -Id <int[]> [-ComputerName <string[]>] [-Module] [-FileVersionInfo] [<CommonParameters>]

Get-Process -Id <int[]> -IncludeUserName [<CommonParameters>]

Get-Process -InputObject <Process[]> -IncludeUserName [<CommonParameters>]

Get-Process -InputObject <Process[]> [-ComputerName <string[]>] [-Module] [-FileVersionInfo] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 10 Name, Id, WorkingSet64
```

### Get-PSDrive

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gdr

Lists the PowerShell drives in the session (file system, registry, Env:, Cert:...) with used and free space. Alias: gdr.

Syntax:

```powershell
Get-PSDrive [[-Name] <string[]>] [-Scope <string>] [-PSProvider <string[]>] [-UseTransaction] [<CommonParameters>]

Get-PSDrive [-LiteralName] <string[]> [-Scope <string>] [-PSProvider <string[]>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Get-PSDrive -PSProvider FileSystem
```

### Get-PSProvider

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Lists the PowerShell providers (FileSystem, Registry, Certificate...) and their drives.

Syntax:

```powershell
Get-PSProvider [[-PSProvider] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-PSProvider
```

### Get-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gsv

Lists Windows services and their status, on the local (or, in 5.1, remote) computer. Alias: gsv.

Syntax:

```powershell
Get-Service [[-Name] <string[]>] [-ComputerName <string[]>] [-DependentServices] [-RequiredServices] [-Include <string[]>] [-Exclude <string[]>] [<CommonParameters>]

Get-Service -DisplayName <string[]> [-ComputerName <string[]>] [-DependentServices] [-RequiredServices] [-Include <string[]>] [-Exclude <string[]>] [<CommonParameters>]

Get-Service [-ComputerName <string[]>] [-DependentServices] [-RequiredServices] [-Include <string[]>] [-Exclude <string[]>] [-InputObject <ServiceController[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Service -Name W* | Where-Object Status -eq Stopped
```

### Get-TimeZone

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gtz

Shows the current time zone, or lists the time zones available. Alias: gtz.

Syntax:

```powershell
Get-TimeZone [[-Name] <string[]>] [<CommonParameters>]

Get-TimeZone -Id <string[]> [<CommonParameters>]

Get-TimeZone -ListAvailable [<CommonParameters>]
```

Example:

```powershell
Get-TimeZone -ListAvailable | Where-Object Id -like '*Eastern*'
```

### Get-Transaction

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Gets the active transaction (for providers that support transactions, such as the registry).

Syntax:

```powershell
Get-Transaction [<CommonParameters>]
```

Example:

```powershell
Get-Transaction
```

### Get-WmiObject

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: gwmi

(Windows PowerShell only) Gets WMI class instances or class info; replaced by Get-CimInstance in PowerShell 7. Alias: gwmi.

Syntax:

```powershell
Get-WmiObject [-Class] <string> [[-Property] <string[]>] [-Filter <string>] [-Amended] [-DirectRead] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject [[-Class] <string>] [-Recurse] [-Amended] [-List] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject -Query <string> [-Amended] [-DirectRead] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject [-Amended] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject [-Amended] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]
```

Example:

```powershell
Get-WmiObject -Class Win32_LogicalDisk -Filter "DriveType=3"
```

### Invoke-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: ii

Performs the default action on an item, such as opening a file in its associated program. Alias: ii.

Syntax:

```powershell
Invoke-Item [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Invoke-Item -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Invoke-Item .\report.xlsx
```

### Invoke-WmiMethod

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: iwmi

(Windows PowerShell only) Calls a WMI method; use Invoke-CimMethod in PowerShell 7.

Syntax:

```powershell
Invoke-WmiMethod [-Class] <string> [-Name] <string> [[-ArgumentList] <Object[]>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> -InputObject <wmi> [-ArgumentList <Object[]>] [-AsJob] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> -Path <string> [-ArgumentList <Object[]>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Invoke-WmiMethod -Class Win32_Process -Name Create -ArgumentList 'notepad.exe'
```

### Join-Path

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Combines a parent path and child path(s) into one path, adding separators correctly.

Syntax:

```powershell
Join-Path [-Path] <string[]> [-ChildPath] <string> [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Join-Path -Path $env:USERPROFILE -ChildPath 'Documents\notes.txt'
```

### Limit-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Sets the maximum size, overflow action and retention of classic event logs.

Syntax:

```powershell
Limit-EventLog [-LogName] <string[]> [-ComputerName <string[]>] [-RetentionDays <int>] [-OverflowAction <OverflowAction>] [-MaximumSize <long>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Limit-EventLog -LogName Application -MaximumSize 64MB -OverflowAction OverwriteAsNeeded
```

### Move-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: mi, move, mv

Moves (or renames by moving) files, folders, registry keys and other items. Aliases: move, mv, mi.

Syntax:

```powershell
Move-Item [-Path] <string[]> [[-Destination] <string>] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Move-Item [[-Destination] <string>] -LiteralPath <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Move-Item -Path .\*.log -Destination .\Archive
```

### Move-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: mp

Moves a property from one item to another (for example between registry keys). Alias: mp.

Syntax:

```powershell
Move-ItemProperty [-Path] <string[]> [-Destination] <string> [-Name] <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Move-ItemProperty [-Destination] <string> [-Name] <string[]> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Move-ItemProperty -Path HKCU:\Software\Old -Destination HKCU:\Software\New -Name Setting1
```

### New-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Creates a classic event log and/or registers an event source for writing events.

Syntax:

```powershell
New-EventLog [-LogName] <string> [-Source] <string[]> [[-ComputerName] <string[]>] [-CategoryResourceFile <string>] [-MessageResourceFile <string>] [-ParameterResourceFile <string>] [<CommonParameters>]
```

Example:

```powershell
New-EventLog -LogName Application -Source MyScript
```

### New-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: ni

Creates a new item: a file, folder, registry key, symbolic link, variable... Alias: ni.

Syntax:

```powershell
New-Item [-Path] <string[]> [-ItemType <string>] [-Value <Object>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

New-Item [[-Path] <string[]>] -Name <string> [-ItemType <string>] [-Value <Object>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
New-Item -Path .\logs\today.txt -ItemType File -Force
```

### New-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Creates a new property on an item and sets its value (for example a registry value of a given type).

Syntax:

```powershell
New-ItemProperty [-Path] <string[]> [-Name] <string> [-PropertyType <string>] [-Value <Object>] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

New-ItemProperty [-Name] <string> -LiteralPath <string[]> [-PropertyType <string>] [-Value <Object>] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
New-ItemProperty -Path HKCU:\Software\MyApp -Name Enabled -Value 1 -PropertyType DWord
```

### New-PSDrive

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: mount, ndr

Creates a PowerShell drive mapped to a location (folder, registry key, network share), optionally persistent. Aliases: mount, ndr.

Syntax:

```powershell
New-PSDrive [-Name] <string> [-PSProvider] <string> [-Root] <string> [-Description <string>] [-Scope <string>] [-Persist] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
New-PSDrive -Name P -PSProvider FileSystem -Root \\Server\Share -Persist
```

### New-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Creates a new Windows service entry for an executable (registry entry and service database).

Syntax:

```powershell
New-Service [-Name] <string> [-BinaryPathName] <string> [-DisplayName <string>] [-Description <string>] [-StartupType <ServiceStartMode>] [-Credential <pscredential>] [-DependsOn <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-Service -Name MySvc -BinaryPathName 'C:\Svc\mysvc.exe' -StartupType Automatic
```

### New-WebServiceProxy

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

(Windows PowerShell only) Creates a proxy object for calling a SOAP web service.

Syntax:

```powershell
New-WebServiceProxy [-Uri] <uri> [[-Class] <string>] [[-Namespace] <string>] [<CommonParameters>]

New-WebServiceProxy [-Uri] <uri> [[-Class] <string>] [[-Namespace] <string>] [-Credential <pscredential>] [<CommonParameters>]

New-WebServiceProxy [-Uri] <uri> [[-Class] <string>] [[-Namespace] <string>] [-UseDefaultCredential] [<CommonParameters>]
```

Example:

```powershell
$p = New-WebServiceProxy -Uri 'http://server/service.asmx?WSDL'
```

### Pop-Location

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: popd

Returns to the location most recently saved by Push-Location. Alias: popd.

Syntax:

```powershell
Pop-Location [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Pop-Location
```

### Push-Location

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: pushd

Saves the current location on a stack and optionally moves to a new one. Alias: pushd.

Syntax:

```powershell
Push-Location [[-Path] <string>] [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]

Push-Location [-LiteralPath <string>] [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Push-Location C:\Windows\System32
```

### Register-WmiEvent

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

(Windows PowerShell only) Subscribes to a WMI event, with an optional action script block.

Syntax:

```powershell
Register-WmiEvent [-Class] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-Credential <pscredential>] [-ComputerName <string>] [-Timeout <long>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-WmiEvent [-Query] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-Credential <pscredential>] [-ComputerName <string>] [-Timeout <long>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

Example:

```powershell
Register-WmiEvent -Class Win32_ProcessStartTrace -SourceIdentifier ProcStart -Action { $Event.SourceEventArgs.NewEvent.ProcessName }
```

### Remove-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Removes the local (or remote) computer from its domain, optionally joining a workgroup.

Syntax:

```powershell
Remove-Computer [[-UnjoinDomainCredential] <pscredential>] [-Restart] [-Force] [-PassThru] [-WorkgroupName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Computer -UnjoinDomainCredential <pscredential> [-LocalCredential <pscredential>] [-Restart] [-ComputerName <string[]>] [-Force] [-PassThru] [-WorkgroupName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-Computer -UnjoinDomainCredential CONTOSO\Admin -WorkgroupName WORKGROUP -Restart
```

### Remove-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Deletes a classic event log or unregisters an event source.

Syntax:

```powershell
Remove-EventLog [-LogName] <string[]> [[-ComputerName] <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-EventLog [[-ComputerName] <string[]>] [-Source <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-EventLog -Source MyScript
```

### Remove-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: del, erase, rd, ri, rm, rmdir

Deletes files, folders, registry keys, variables and other items. Aliases: del, rm, rmdir, rd, erase, ri.

Syntax:

```powershell
Remove-Item [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]

Remove-Item -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]
```

Example:

```powershell
Remove-Item -Path .\Temp -Recurse -Force
```

### Remove-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: rp

Deletes a property and its value from an item (for example a registry value). Alias: rp.

Syntax:

```powershell
Remove-ItemProperty [-Path] <string[]> [-Name] <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Remove-ItemProperty [-Name] <string[]> -LiteralPath <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Remove-ItemProperty -Path HKCU:\Software\MyApp -Name LastRun
```

### Remove-PSDrive

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: rdr

Removes PowerShell drives and disconnects mapped network drives. Alias: rdr.

Syntax:

```powershell
Remove-PSDrive [-Name] <string[]> [-PSProvider <string[]>] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Remove-PSDrive [-LiteralName] <string[]> [-PSProvider <string[]>] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Remove-PSDrive -Name P
```

### Remove-WmiObject

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: rwmi

(Windows PowerShell only) Deletes a WMI instance; use Remove-CimInstance in PowerShell 7.

Syntax:

```powershell
Remove-WmiObject [-Class] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject -InputObject <wmi> [-AsJob] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject -Path <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-WmiObject Win32_Process -Filter "Name='notepad.exe'" | Remove-WmiObject
```

### Rename-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Renames the local or a remote computer (takes effect after a restart).

Syntax:

```powershell
Rename-Computer [-NewName] <string> [-ComputerName <string>] [-PassThru] [-DomainCredential <pscredential>] [-LocalCredential <pscredential>] [-Force] [-Restart] [-WsmanAuthentication <string>] [-Protocol <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Rename-Computer -NewName WS-042 -Restart
```

### Rename-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: ren, rni

Renames a file, folder, registry key or other item in place. Aliases: ren, rni.

Syntax:

```powershell
Rename-Item [-Path] <string> [-NewName] <string> [-Force] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Rename-Item [-NewName] <string> -LiteralPath <string> [-Force] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Rename-Item -Path .\draft.txt -NewName final.txt
```

### Rename-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: rnp

Renames a property of an item (for example a registry value). Alias: rnp.

Syntax:

```powershell
Rename-ItemProperty [-Path] <string> [-Name] <string> [-NewName] <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Rename-ItemProperty [-Name] <string> [-NewName] <string> -LiteralPath <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Rename-ItemProperty -Path HKCU:\Software\MyApp -Name OldName -NewName NewName
```

### Reset-ComputerMachinePassword

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Resets the computer's machine account password in its domain.

Syntax:

```powershell
Reset-ComputerMachinePassword [-Server <string>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Reset-ComputerMachinePassword -Server DC01 -Credential CONTOSO\Admin
```

### Resolve-Path

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: rvpa

Resolves wildcards and relative paths into full paths. Alias: rvpa.

Syntax:

```powershell
Resolve-Path [-Path] <string[]> [-Relative] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Resolve-Path -LiteralPath <string[]> [-Relative] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Resolve-Path .\*.csv
```

### Restart-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Restarts the local or remote computers, optionally waiting until they come back.

Syntax:

```powershell
Restart-Computer [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-DcomAuthentication <AuthenticationLevel>] [-Impersonation <ImpersonationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-Force] [-Wait] [-Timeout <int>] [-For <WaitForServiceTypes>] [-Delay <int16>] [-WhatIf] [-Confirm] [<CommonParameters>]

Restart-Computer [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-Impersonation <ImpersonationLevel>] [-Force] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Restart-Computer -ComputerName Server01 -Force -Wait -For PowerShell
```

### Restart-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Stops and then starts one or more services.

Syntax:

```powershell
Restart-Service [-InputObject] <ServiceController[]> [-Force] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Restart-Service [-Name] <string[]> [-Force] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Restart-Service -DisplayName <string[]> [-Force] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Restart-Service -Name Spooler -Force
```

### Restore-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Starts a System Restore to a restore point on the local computer (restarts it).

Syntax:

```powershell
Restore-Computer [-RestorePoint] <int> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Restore-Computer -RestorePoint 253
```

### Resume-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Resumes paused services.

Syntax:

```powershell
Resume-Service [-InputObject] <ServiceController[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Service [-Name] <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Service -DisplayName <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Resume-Service -Name Spooler
```

### Set-Clipboard

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: scb

Puts text or file paths on the Windows clipboard, replacing or appending. Alias: scb.

Syntax:

```powershell
Set-Clipboard [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Clipboard [-Value] <string[]> [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Clipboard -Path <string[]> [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Clipboard -LiteralPath <string[]> [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-Content .\notes.txt | Set-Clipboard
```

### Set-Content

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: sc

Writes content to a file, replacing what was there (creates the file if missing). Alias: sc (5.1 only).

Syntax:

```powershell
Set-Content [-Path] <string[]> [-Value] <Object[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]

Set-Content [-Value] <Object[]> -LiteralPath <string[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]
```

Example:

```powershell
Set-Content -Path .\config.txt -Value 'mode=fast' -Encoding UTF8
```

### Set-Item

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: si

Changes the value of an item (variable, alias, environment variable, registry key default). Alias: si.

Syntax:

```powershell
Set-Item [-Path] <string[]> [[-Value] <Object>] [-Force] [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-Item [[-Value] <Object>] -LiteralPath <string[]> [-Force] [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Set-Item -Path Env:\APP_MODE -Value Production
```

### Set-ItemProperty

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: sp

Creates or changes the value of an item's property, often a registry value or file attribute. Alias: sp.

Syntax:

```powershell
Set-ItemProperty [-Path] <string[]> [-Name] <string> [-Value] <Object> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-ItemProperty [-Path] <string[]> -InputObject <psobject> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-ItemProperty [-Name] <string> [-Value] <Object> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-ItemProperty -LiteralPath <string[]> -InputObject <psobject> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Set-ItemProperty -Path HKCU:\Software\MyApp -Name Enabled -Value 0
```

### Set-Location

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: cd, chdir, sl

Changes the current working location (folder, registry key, drive). Aliases: cd, chdir, sl.

Syntax:

```powershell
Set-Location [[-Path] <string>] [-PassThru] [-UseTransaction] [<CommonParameters>]

Set-Location -LiteralPath <string> [-PassThru] [-UseTransaction] [<CommonParameters>]

Set-Location [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Set-Location -Path HKLM:\SOFTWARE
```

### Set-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Changes a service's properties (startup type, display name, description) or starts/stops/pauses it.

Syntax:

```powershell
Set-Service [-Name] <string> [-ComputerName <string[]>] [-DisplayName <string>] [-Description <string>] [-StartupType <ServiceStartMode>] [-Status <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Service [-ComputerName <string[]>] [-DisplayName <string>] [-Description <string>] [-StartupType <ServiceStartMode>] [-Status <string>] [-InputObject <ServiceController>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-Service -Name Spooler -StartupType Manual
```

### Set-TimeZone

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: stz

Sets the system time zone. Alias: stz.

Syntax:

```powershell
Set-TimeZone [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-TimeZone -Id <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-TimeZone [-InputObject] <TimeZoneInfo> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-TimeZone -Id 'Eastern Standard Time'
```

### Set-WmiInstance

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: swmi

(Windows PowerShell only) Creates or updates a WMI instance; use Set-CimInstance in PowerShell 7.

Syntax:

```powershell
Set-WmiInstance [-Class] <string> [[-Arguments] <hashtable>] [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance -InputObject <wmi> [-Arguments <hashtable>] [-PutType <PutType>] [-AsJob] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance -Path <string> [-Arguments <hashtable>] [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-WmiInstance -Class Win32_Environment -Arguments @{ Name = 'MYVAR'; VariableValue = '1'; UserName = '<SYSTEM>' }
```

### Show-ControlPanelItem

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Opens Control Panel items by name or canonical name.

Syntax:

```powershell
Show-ControlPanelItem [-Name] <string[]> [<CommonParameters>]

Show-ControlPanelItem -CanonicalName <string[]> [<CommonParameters>]

Show-ControlPanelItem [[-InputObject] <ControlPanelItem[]>] [<CommonParameters>]
```

Example:

```powershell
Show-ControlPanelItem -Name 'Programs and Features'
```

### Show-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Opens Event Viewer showing the event logs of the local or a remote computer.

Syntax:

```powershell
Show-EventLog [[-ComputerName] <string>] [<CommonParameters>]
```

Example:

```powershell
Show-EventLog -ComputerName Server01
```

### Split-Path

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Returns part of a path: the parent folder, leaf name, extension-less leaf, qualifier, or whether it is absolute.

Syntax:

```powershell
Split-Path [-Path] <string[]> [-Parent] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-Leaf] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-Qualifier] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-NoQualifier] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-Resolve] [-IsAbsolute] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path -LiteralPath <string[]> [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Split-Path -Path C:\Data\report.csv -Leaf
```

### Start-Process

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: saps, start

Starts a program or opens a file in a new process, with arguments, working folder, credentials or elevation. Aliases: start, saps.

Syntax:

```powershell
Start-Process [-FilePath] <string> [[-ArgumentList] <string[]>] [-Credential <pscredential>] [-WorkingDirectory <string>] [-LoadUserProfile] [-NoNewWindow] [-PassThru] [-RedirectStandardError <string>] [-RedirectStandardInput <string>] [-RedirectStandardOutput <string>] [-WindowStyle <ProcessWindowStyle>] [-Wait] [-UseNewEnvironment] [<CommonParameters>]

Start-Process [-FilePath] <string> [[-ArgumentList] <string[]>] [-WorkingDirectory <string>] [-PassThru] [-Verb <string>] [-WindowStyle <ProcessWindowStyle>] [-Wait] [<CommonParameters>]
```

Example:

```powershell
Start-Process -FilePath notepad.exe -ArgumentList .\notes.txt -Wait
```

### Start-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: sasv

Starts stopped services. Alias: sasv.

Syntax:

```powershell
Start-Service [-InputObject] <ServiceController[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Service [-Name] <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Service -DisplayName <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Start-Service -Name Spooler
```

### Start-Transaction

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Starts a transaction so supported provider operations (registry) can be committed or rolled back together.

Syntax:

```powershell
Start-Transaction [-Timeout <int>] [-Independent] [-RollbackPreference <RollbackSeverity>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Start-Transaction; New-Item HKCU:\Software\Test -UseTransaction; Complete-Transaction
```

### Stop-Computer

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Shuts down the local or remote computers.

Syntax:

```powershell
Stop-Computer [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-Impersonation <ImpersonationLevel>] [-ThrottleLimit <int>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Stop-Computer -ComputerName Server01 -Force
```

### Stop-Process

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: kill, spps

Stops running processes by name, ID or process object. Aliases: kill, spps.

Syntax:

```powershell
Stop-Process [-Id] <int[]> [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Process -Name <string[]> [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Process [-InputObject] <Process[]> [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Stop-Process -Name notepad -Force
```

### Stop-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet · aliases: spsv

Stops running services (and with -Force, services that depend on them). Alias: spsv.

Syntax:

```powershell
Stop-Service [-InputObject] <ServiceController[]> [-Force] [-NoWait] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Service [-Name] <string[]> [-Force] [-NoWait] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Service -DisplayName <string[]> [-Force] [-NoWait] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Stop-Service -Name Spooler -Force
```

### Suspend-Service

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Pauses running services that support pausing.

Syntax:

```powershell
Suspend-Service [-InputObject] <ServiceController[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Service [-Name] <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Service -DisplayName <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Suspend-Service -Name Spooler
```

### Test-ComputerSecureChannel

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Checks (and with -Repair fixes) the secure channel between the computer and its domain.

Syntax:

```powershell
Test-ComputerSecureChannel [-Repair] [-Server <string>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Test-ComputerSecureChannel -Repair -Credential CONTOSO\Admin
```

### Test-Connection

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Sends ping (ICMP echo) requests to computers; in 7.x also -TcpPort and -Traceroute.

Syntax:

```powershell
Test-Connection [-ComputerName] <string[]> [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-BufferSize <int>] [-Count <int>] [-Impersonation <ImpersonationLevel>] [-ThrottleLimit <int>] [-TimeToLive <int>] [-Delay <int>] [<CommonParameters>]

Test-Connection [-ComputerName] <string[]> [-Source] <string[]> [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-BufferSize <int>] [-Count <int>] [-Credential <pscredential>] [-Impersonation <ImpersonationLevel>] [-ThrottleLimit <int>] [-TimeToLive <int>] [-Delay <int>] [<CommonParameters>]

Test-Connection [-ComputerName] <string[]> [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-BufferSize <int>] [-Count <int>] [-Impersonation <ImpersonationLevel>] [-TimeToLive <int>] [-Delay <int>] [-Quiet] [<CommonParameters>]
```

Example:

```powershell
Test-Connection -ComputerName Server01 -Count 2
```

### Test-Path

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Returns True if a path exists (optionally only files with -PathType Leaf or folders with Container).

Syntax:

```powershell
Test-Path [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PathType <TestPathType>] [-IsValid] [-Credential <pscredential>] [-UseTransaction] [-OlderThan <datetime>] [-NewerThan <datetime>] [<CommonParameters>]

Test-Path -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PathType <TestPathType>] [-IsValid] [-Credential <pscredential>] [-UseTransaction] [-OlderThan <datetime>] [-NewerThan <datetime>] [<CommonParameters>]
```

Example:

```powershell
if (Test-Path -Path .\config.json -PathType Leaf) { 'found' }
```

### Undo-Transaction

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Rolls back the active transaction.

Syntax:

```powershell
Undo-Transaction [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Undo-Transaction
```

### Use-Transaction

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Runs a script block as part of the active transaction.

Syntax:

```powershell
Use-Transaction [-TransactedScript] <scriptblock> [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
Use-Transaction -TransactedScript { $sb.Append('x') } -UseTransaction
```

### Wait-Process

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Waits until the given processes have exited (optionally with a timeout).

Syntax:

```powershell
Wait-Process [-Name] <string[]> [[-Timeout] <int>] [<CommonParameters>]

Wait-Process [-Id] <int[]> [[-Timeout] <int>] [<CommonParameters>]

Wait-Process [[-Timeout] <int>] -InputObject <Process[]> [<CommonParameters>]
```

Example:

```powershell
Start-Process notepad -PassThru | Wait-Process -Timeout 60
```

### Write-EventLog

PowerShell 5.1 · module Microsoft.PowerShell.Management · cmdlet

Writes an entry to a classic event log for a registered source.

Syntax:

```powershell
Write-EventLog [-LogName] <string> [-Source] <string> [-EventId] <int> [[-EntryType] <EventLogEntryType>] [-Message] <string> [-Category <int16>] [-RawData <byte[]>] [-ComputerName <string>] [<CommonParameters>]
```

Example:

```powershell
Write-EventLog -LogName Application -Source MyScript -EventId 100 -EntryType Information -Message 'Done'
```

## Microsoft.PowerShell.Utility

### Add-Member

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Adds custom properties or methods (NoteProperty, ScriptProperty, ScriptMethod...) to an object.

Syntax:

```powershell
Add-Member -InputObject <psobject> -TypeName <string> [-PassThru] [<CommonParameters>]

Add-Member [-MemberType] <PSMemberTypes> [-Name] <string> [[-Value] <Object>] [[-SecondValue] <Object>] -InputObject <psobject> [-TypeName <string>] [-Force] [-PassThru] [<CommonParameters>]

Add-Member [-NotePropertyName] <string> [-NotePropertyValue] <Object> -InputObject <psobject> [-TypeName <string>] [-Force] [-PassThru] [<CommonParameters>]

Add-Member [-NotePropertyMembers] <IDictionary> -InputObject <psobject> [-TypeName <string>] [-Force] [-PassThru] [<CommonParameters>]
```

Example:

```powershell
$o = [pscustomobject]@{ Name = 'a' }; $o | Add-Member -NotePropertyName Size -NotePropertyValue 10
```

### Add-Type

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Compiles C# (or VB) source, or loads an assembly, so its .NET types can be used in the session.

Syntax:

```powershell
Add-Type [-TypeDefinition] <string> [-CodeDomProvider <CodeDomProvider>] [-CompilerParameters <CompilerParameters>] [-Language <Language>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type [-Name] <string> [-MemberDefinition] <string[]> [-CodeDomProvider <CodeDomProvider>] [-CompilerParameters <CompilerParameters>] [-Namespace <string>] [-UsingNamespace <string[]>] [-Language <Language>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type [-Path] <string[]> [-CompilerParameters <CompilerParameters>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type -LiteralPath <string[]> [-CompilerParameters <CompilerParameters>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type -AssemblyName <string[]> [-PassThru] [-IgnoreWarnings] [<CommonParameters>]
```

Example:

```powershell
Add-Type -TypeDefinition 'public static class Calc { public static int Add(int a, int b) { return a + b; } }'; [Calc]::Add(2, 3)
```

### Clear-Variable

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: clv

Empties the value of a variable but keeps the variable. Alias: clv.

Syntax:

```powershell
Clear-Variable [-Name] <string[]> [-Include <string[]>] [-Exclude <string[]>] [-Force] [-PassThru] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Clear-Variable -Name result
```

### Compare-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: compare, diff

Compares two sets of objects and shows which items are only in one side (<= or =>) or in both. Aliases: compare, diff (5.1).

Syntax:

```powershell
Compare-Object [-ReferenceObject] <psobject[]> [-DifferenceObject] <psobject[]> [-SyncWindow <int>] [-Property <Object[]>] [-ExcludeDifferent] [-IncludeEqual] [-PassThru] [-Culture <string>] [-CaseSensitive] [<CommonParameters>]
```

Example:

```powershell
Compare-Object (Get-Content .\a.txt) (Get-Content .\b.txt)
```

### Convert-String

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

(Windows PowerShell only) Reformats strings by following before/after examples.

Syntax:

```powershell
Convert-String -InputObject <string> [-Example <List[psobject]>] [<CommonParameters>]
```

Example:

```powershell
'Ann Smith' | Convert-String -Example 'John Doe=Doe, J.'
```

### ConvertFrom-Csv

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns CSV text (strings) into objects with one property per column.

Syntax:

```powershell
ConvertFrom-Csv [-InputObject] <psobject[]> [[-Delimiter] <char>] [-Header <string[]>] [<CommonParameters>]

ConvertFrom-Csv [-InputObject] <psobject[]> -UseCulture [-Header <string[]>] [<CommonParameters>]
```

Example:

```powershell
'Name,Age','Ann,30' | ConvertFrom-Csv
```

### ConvertFrom-Json

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns a JSON string into PowerShell objects (custom objects, or hash tables with -AsHashtable in 7).

Syntax:

```powershell
ConvertFrom-Json [-InputObject] <string> [<CommonParameters>]
```

Example:

```powershell
Get-Content .\settings.json -Raw | ConvertFrom-Json
```

### ConvertFrom-SddlString

PowerShell 5.1 · module Microsoft.PowerShell.Utility · function

Turns an SDDL security descriptor string into a readable object (owner, group, access rules).

Syntax:

```powershell
ConvertFrom-SddlString [-Sddl] <string> [-Type <Object>] [<CommonParameters>]
```

Example:

```powershell
(Get-Acl C:\Windows).Sddl | ConvertFrom-SddlString
```

### ConvertFrom-String

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: CFS

(Windows PowerShell only) Parses text into objects using delimiters or an example template.

Syntax:

```powershell
ConvertFrom-String [-InputObject] <string> [-Delimiter <string>] [-PropertyNames <string[]>] [<CommonParameters>]

ConvertFrom-String [-InputObject] <string> [-TemplateFile <string[]>] [-TemplateContent <string[]>] [-IncludeExtent] [-UpdateTemplate] [<CommonParameters>]
```

Example:

```powershell
'Ann 30','Bob 25' | ConvertFrom-String -PropertyNames Name, Age
```

### ConvertFrom-StringData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns lines of key=value text into a hash table.

Syntax:

```powershell
ConvertFrom-StringData [-StringData] <string> [<CommonParameters>]
```

Example:

```powershell
"name=Ann`nage=30" | ConvertFrom-StringData
```

### ConvertTo-Csv

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns objects into CSV strings (no file); see Export-Csv to write a file.

Syntax:

```powershell
ConvertTo-Csv [-InputObject] <psobject> [[-Delimiter] <char>] [-NoTypeInformation] [<CommonParameters>]

ConvertTo-Csv [-InputObject] <psobject> [-UseCulture] [-NoTypeInformation] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Select-Object Name, Id | ConvertTo-Csv -NoTypeInformation
```

### ConvertTo-Html

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns objects into an HTML table or page.

Syntax:

```powershell
ConvertTo-Html [[-Property] <Object[]>] [[-Head] <string[]>] [[-Title] <string>] [[-Body] <string[]>] [-InputObject <psobject>] [-As <string>] [-CssUri <uri>] [-PostContent <string[]>] [-PreContent <string[]>] [<CommonParameters>]

ConvertTo-Html [[-Property] <Object[]>] [-InputObject <psobject>] [-As <string>] [-Fragment] [-PostContent <string[]>] [-PreContent <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Service | ConvertTo-Html -Property Name, Status -Title Services | Out-File .\services.html
```

### ConvertTo-Json

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns objects into a JSON string; use -Depth for nested objects (default depth is 2).

Syntax:

```powershell
ConvertTo-Json [-InputObject] <Object> [-Depth <int>] [-Compress] [<CommonParameters>]
```

Example:

```powershell
@{ name = 'Ann'; tags = @('a','b') } | ConvertTo-Json -Depth 3
```

### ConvertTo-Xml

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Creates an XML representation of objects.

Syntax:

```powershell
ConvertTo-Xml [-InputObject] <psobject> [-Depth <int>] [-NoTypeInformation] [-As <string>] [<CommonParameters>]
```

Example:

```powershell
Get-Date | ConvertTo-Xml -As String
```

### Debug-Runspace

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Starts an interactive debugging session with another runspace in the process.

Syntax:

```powershell
Debug-Runspace [-Runspace] <runspace> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Runspace [-Name] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Runspace [-Id] <int> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Runspace [-InstanceId] <guid> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Debug-Runspace -Id 2
```

### Disable-PSBreakpoint

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: dbp

Turns off breakpoints without deleting them. Alias: dbp.

Syntax:

```powershell
Disable-PSBreakpoint [-Breakpoint] <Breakpoint[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-PSBreakpoint [-Id] <int[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-PSBreakpoint | Disable-PSBreakpoint
```

### Disable-RunspaceDebug

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns off runspace debugging and releases a pending debugger stop.

Syntax:

```powershell
Disable-RunspaceDebug [[-RunspaceName] <string[]>] [<CommonParameters>]

Disable-RunspaceDebug [-Runspace] <runspace[]> [<CommonParameters>]

Disable-RunspaceDebug [-RunspaceId] <int[]> [<CommonParameters>]

Disable-RunspaceDebug [-RunspaceInstanceId] <guid[]> [<CommonParameters>]

Disable-RunspaceDebug [[-ProcessName] <string>] [[-AppDomainName] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Disable-RunspaceDebug -RunspaceName Worker
```

### Enable-PSBreakpoint

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: ebp

Turns breakpoints back on. Alias: ebp.

Syntax:

```powershell
Enable-PSBreakpoint [-Id] <int[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-PSBreakpoint [-Breakpoint] <Breakpoint[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-PSBreakpoint -Id 1, 2
```

### Enable-RunspaceDebug

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Enables debugging on runspaces so breakpoints hold until a debugger attaches.

Syntax:

```powershell
Enable-RunspaceDebug [[-RunspaceName] <string[]>] [-BreakAll] [<CommonParameters>]

Enable-RunspaceDebug [-RunspaceId] <int[]> [-BreakAll] [<CommonParameters>]

Enable-RunspaceDebug [-Runspace] <runspace[]> [-BreakAll] [<CommonParameters>]

Enable-RunspaceDebug [-RunspaceInstanceId] <guid[]> [<CommonParameters>]

Enable-RunspaceDebug [[-ProcessName] <string>] [[-AppDomainName] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Enable-RunspaceDebug -BreakAll
```

### Export-Alias

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: epal

Saves the session's aliases to a file (CSV or script) for reuse. Alias: epal.

Syntax:

```powershell
Export-Alias [-Path] <string> [[-Name] <string[]>] [-PassThru] [-As <ExportAliasFormat>] [-Append] [-Force] [-NoClobber] [-Description <string>] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Export-Alias [[-Name] <string[]>] -LiteralPath <string> [-PassThru] [-As <ExportAliasFormat>] [-Append] [-Force] [-NoClobber] [-Description <string>] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Export-Alias -Path .\aliases.ps1 -As Script
```

### Export-Clixml

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Saves objects to an XML (CLIXML) file that Import-Clixml can rebuild with types and values.

Syntax:

```powershell
Export-Clixml [-Path] <string> -InputObject <psobject> [-Depth <int>] [-Force] [-NoClobber] [-Encoding <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Export-Clixml -LiteralPath <string> -InputObject <psobject> [-Depth <int>] [-Force] [-NoClobber] [-Encoding <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Export-Clixml -Path .\procs.xml
```

### Export-Csv

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: epcsv

Writes objects to a CSV file, one row per object. Alias: epcsv.

Syntax:

```powershell
Export-Csv [[-Path] <string>] [[-Delimiter] <char>] -InputObject <psobject> [-LiteralPath <string>] [-Force] [-NoClobber] [-Encoding <string>] [-Append] [-NoTypeInformation] [-WhatIf] [-Confirm] [<CommonParameters>]

Export-Csv [[-Path] <string>] -InputObject <psobject> [-LiteralPath <string>] [-Force] [-NoClobber] [-Encoding <string>] [-Append] [-UseCulture] [-NoTypeInformation] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-Service | Select-Object Name, Status | Export-Csv -Path .\svc.csv -NoTypeInformation
```

### Export-FormatData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Saves formatting data (views) from the session to a .format.ps1xml file.

Syntax:

```powershell
Export-FormatData -InputObject <ExtendedTypeDefinition[]> -Path <string> [-Force] [-NoClobber] [-IncludeScriptBlock] [<CommonParameters>]

Export-FormatData -InputObject <ExtendedTypeDefinition[]> -LiteralPath <string> [-Force] [-NoClobber] [-IncludeScriptBlock] [<CommonParameters>]
```

Example:

```powershell
Get-FormatData -TypeName System.Diagnostics.Process | Export-FormatData -Path .\proc.format.ps1xml
```

### Export-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: epsn

Saves the commands from another session (proxy functions) as a module you can import later.

Syntax:

```powershell
Export-PSSession [-Session] <PSSession> [-OutputModule] <string> [[-CommandName] <string[]>] [[-FormatTypeName] <string[]>] [-Force] [-Encoding <string>] [-AllowClobber] [-ArgumentList <Object[]>] [-CommandType <CommandTypes>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Certificate <X509Certificate2>] [<CommonParameters>]
```

Example:

```powershell
Export-PSSession -Session $s -CommandName Get-Mailbox -OutputModule RemoteMail
```

### Format-Custom

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: fc

Formats output using a custom view (shows object structure). Alias: fc.

Syntax:

```powershell
Format-Custom [[-Property] <Object[]>] [-Depth <int>] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Date | Format-Custom
```

### Format-Hex

PowerShell 5.1 · module Microsoft.PowerShell.Utility · function · aliases: fhx

Shows a file, string or bytes as hexadecimal. Alias: fhx.

Syntax:

```powershell
Format-Hex [-Path] <string[]> [<CommonParameters>]

Format-Hex -LiteralPath <string[]> [<CommonParameters>]

Format-Hex -InputObject <Object> [-Encoding <string>] [-Raw] [<CommonParameters>]
```

Example:

```powershell
Format-Hex -Path .\data.bin | Select-Object -First 4
```

### Format-List

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: fl

Formats output as a list, one property per line. Alias: fl.

Syntax:

```powershell
Format-List [[-Property] <Object[]>] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Process -Id $PID | Format-List *
```

### Format-Table

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: ft

Formats output as a table with chosen columns. Alias: ft.

Syntax:

```powershell
Format-Table [[-Property] <Object[]>] [-AutoSize] [-RepeatHeader] [-HideTableHeaders] [-Wrap] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Format-Table Name, Id, CPU -AutoSize
```

### Format-Wide

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: fw

Formats objects as a wide table showing one property, in columns. Alias: fw.

Syntax:

```powershell
Format-Wide [[-Property] <Object>] [-AutoSize] [-Column <int>] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem | Format-Wide -Column 4
```

### Get-Alias

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: gal

Lists aliases, or the alias for a command with -Definition. Alias: gal.

Syntax:

```powershell
Get-Alias [[-Name] <string[]>] [-Exclude <string[]>] [-Scope <string>] [<CommonParameters>]

Get-Alias [-Exclude <string[]>] [-Scope <string>] [-Definition <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Alias -Definition Get-ChildItem
```

### Get-Culture

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Shows the culture (regional format) settings of the operating system.

Syntax:

```powershell
Get-Culture [<CommonParameters>]
```

Example:

```powershell
(Get-Culture).DateTimeFormat.ShortDatePattern
```

### Get-Date

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Gets the current date and time, or a specific date, optionally formatted.

Syntax:

```powershell
Get-Date [[-Date] <datetime>] [-Year <int>] [-Month <int>] [-Day <int>] [-Hour <int>] [-Minute <int>] [-Second <int>] [-Millisecond <int>] [-DisplayHint <DisplayHintType>] [-Format <string>] [<CommonParameters>]

Get-Date [[-Date] <datetime>] [-Year <int>] [-Month <int>] [-Day <int>] [-Hour <int>] [-Minute <int>] [-Second <int>] [-Millisecond <int>] [-DisplayHint <DisplayHintType>] [-UFormat <string>] [<CommonParameters>]
```

Example:

```powershell
Get-Date -Format 'yyyy-MM-dd HH:mm'
```

### Get-Event

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Gets events in the session's event queue (from Register-*Event subscriptions).

Syntax:

```powershell
Get-Event [[-SourceIdentifier] <string>] [<CommonParameters>]

Get-Event [-EventIdentifier] <int> [<CommonParameters>]
```

Example:

```powershell
Get-Event -SourceIdentifier ProcStart
```

### Get-EventSubscriber

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Lists the event subscriptions in the session.

Syntax:

```powershell
Get-EventSubscriber [[-SourceIdentifier] <string>] [-Force] [<CommonParameters>]

Get-EventSubscriber [-SubscriptionId] <int> [-Force] [<CommonParameters>]
```

Example:

```powershell
Get-EventSubscriber
```

### Get-FileHash

PowerShell 5.1 · module Microsoft.PowerShell.Utility · function

Computes a file's hash (SHA256 by default; also SHA1, SHA384, SHA512, MD5).

Syntax:

```powershell
Get-FileHash [-Path] <string[]> [-Algorithm <string>] [<CommonParameters>]

Get-FileHash -LiteralPath <string[]> [-Algorithm <string>] [<CommonParameters>]

Get-FileHash -InputStream <Stream> [-Algorithm <string>] [<CommonParameters>]
```

Example:

```powershell
Get-FileHash -Path .\setup.exe -Algorithm SHA256
```

### Get-FormatData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Gets the formatting data (views) loaded in the session for a type.

Syntax:

```powershell
Get-FormatData [[-TypeName] <string[]>] [-PowerShellVersion <version>] [<CommonParameters>]
```

Example:

```powershell
Get-FormatData -TypeName System.IO.FileInfo
```

### Get-Host

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Gets the object that represents the current host program (name, version, UI).

Syntax:

```powershell
Get-Host [<CommonParameters>]
```

Example:

```powershell
(Get-Host).Version
```

### Get-Member

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: gm

Lists the properties and methods of objects in the pipeline. Alias: gm.

Syntax:

```powershell
Get-Member [[-Name] <string[]>] [-InputObject <psobject>] [-MemberType <PSMemberTypes>] [-View <PSMemberViewTypes>] [-Static] [-Force] [<CommonParameters>]
```

Example:

```powershell
Get-Service | Get-Member -MemberType Property
```

### Get-PSBreakpoint

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: gbp

Lists the breakpoints set in the session. Alias: gbp.

Syntax:

```powershell
Get-PSBreakpoint [[-Script] <string[]>] [<CommonParameters>]

Get-PSBreakpoint -Variable <string[]> [-Script <string[]>] [<CommonParameters>]

Get-PSBreakpoint -Command <string[]> [-Script <string[]>] [<CommonParameters>]

Get-PSBreakpoint [-Type] <BreakpointType[]> [-Script <string[]>] [<CommonParameters>]

Get-PSBreakpoint [-Id] <int[]> [<CommonParameters>]
```

Example:

```powershell
Get-PSBreakpoint -Script .\deploy.ps1
```

### Get-PSCallStack

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: gcs

Shows the current call stack (useful inside the debugger). Alias: gcs.

Syntax:

```powershell
Get-PSCallStack [<CommonParameters>]
```

Example:

```powershell
Get-PSCallStack
```

### Get-Random

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Returns a random number, or picks random items from a collection.

Syntax:

```powershell
Get-Random [[-Maximum] <Object>] [-SetSeed <int>] [-Minimum <Object>] [<CommonParameters>]

Get-Random [-InputObject] <Object[]> [-SetSeed <int>] [-Count <int>] [<CommonParameters>]
```

Example:

```powershell
Get-Random -Minimum 1 -Maximum 101
```

### Get-Runspace

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Lists the runspaces in the PowerShell host process.

Syntax:

```powershell
Get-Runspace [[-Name] <string[]>] [<CommonParameters>]

Get-Runspace [-Id] <int[]> [<CommonParameters>]

Get-Runspace [-InstanceId] <guid[]> [<CommonParameters>]
```

Example:

```powershell
Get-Runspace
```

### Get-RunspaceDebug

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Shows the debugging options of runspaces.

Syntax:

```powershell
Get-RunspaceDebug [[-RunspaceName] <string[]>] [<CommonParameters>]

Get-RunspaceDebug [-Runspace] <runspace[]> [<CommonParameters>]

Get-RunspaceDebug [-RunspaceId] <int[]> [<CommonParameters>]

Get-RunspaceDebug [-RunspaceInstanceId] <guid[]> [<CommonParameters>]

Get-RunspaceDebug [[-ProcessName] <string>] [[-AppDomainName] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-RunspaceDebug
```

### Get-TraceSource

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Lists PowerShell components that are instrumented for tracing.

Syntax:

```powershell
Get-TraceSource [[-Name] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-TraceSource -Name *Command*
```

### Get-TypeData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Gets the extended type data (added properties and methods) loaded in the session.

Syntax:

```powershell
Get-TypeData [[-TypeName] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-TypeData -TypeName System.IO.FileInfo
```

### Get-UICulture

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Shows the user-interface language culture of the operating system.

Syntax:

```powershell
Get-UICulture [<CommonParameters>]
```

Example:

```powershell
(Get-UICulture).Name
```

### Get-Unique

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: gu

Removes adjacent duplicates from a sorted list. Alias: gu.

Syntax:

```powershell
Get-Unique [-InputObject <psobject>] [-AsString] [<CommonParameters>]

Get-Unique [-InputObject <psobject>] [-OnType] [<CommonParameters>]
```

Example:

```powershell
1,1,2,3,3 | Get-Unique
```

### Get-Variable

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: gv

Lists variables in the session, or gets one by name. Alias: gv.

Syntax:

```powershell
Get-Variable [[-Name] <string[]>] [-ValueOnly] [-Include <string[]>] [-Exclude <string[]>] [-Scope <string>] [<CommonParameters>]
```

Example:

```powershell
Get-Variable -Name PS*
```

### Group-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: group

Groups objects that share a property value, with counts. Alias: group.

Syntax:

```powershell
Group-Object [[-Property] <Object[]>] [-NoElement] [-AsHashTable] [-AsString] [-InputObject <psobject>] [-Culture <string>] [-CaseSensitive] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem | Group-Object Extension | Sort-Object Count -Descending
```

### Import-Alias

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: ipal

Imports aliases from a file created by Export-Alias. Alias: ipal.

Syntax:

```powershell
Import-Alias [-Path] <string> [-Scope <string>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Import-Alias -LiteralPath <string> [-Scope <string>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Import-Alias -Path .\aliases.csv
```

### Import-Clixml

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Rebuilds objects from a CLIXML file made by Export-Clixml.

Syntax:

```powershell
Import-Clixml [-Path] <string[]> [-IncludeTotalCount] [-Skip <uint64>] [-First <uint64>] [<CommonParameters>]

Import-Clixml -LiteralPath <string[]> [-IncludeTotalCount] [-Skip <uint64>] [-First <uint64>] [<CommonParameters>]
```

Example:

```powershell
$procs = Import-Clixml -Path .\procs.xml
```

### Import-Csv

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: ipcsv

Reads a CSV file into objects, one per row. Alias: ipcsv.

Syntax:

```powershell
Import-Csv [[-Path] <string[]>] [[-Delimiter] <char>] [-LiteralPath <string[]>] [-Header <string[]>] [-Encoding <string>] [<CommonParameters>]

Import-Csv [[-Path] <string[]>] -UseCulture [-LiteralPath <string[]>] [-Header <string[]>] [-Encoding <string>] [<CommonParameters>]
```

Example:

```powershell
Import-Csv -Path .\users.csv | Where-Object Department -eq IT
```

### Import-LocalizedData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Loads language-specific strings (from .psd1 files in culture folders) into a script.

Syntax:

```powershell
Import-LocalizedData [[-BindingVariable] <string>] [[-UICulture] <string>] [-BaseDirectory <string>] [-FileName <string>] [-SupportedCommand <string[]>] [<CommonParameters>]
```

Example:

```powershell
Import-LocalizedData -BindingVariable msg -FileName Messages.psd1
```

### Import-PowerShellDataFile

PowerShell 5.1 · module Microsoft.PowerShell.Utility · function

Reads a .psd1 data file into a hash table without running code.

Syntax:

```powershell
Import-PowerShellDataFile [[-Path] <string[]>] [<CommonParameters>]

Import-PowerShellDataFile [-LiteralPath <string[]>] [<CommonParameters>]
```

Example:

```powershell
Import-PowerShellDataFile -Path .\MyTools.psd1
```

### Import-PSSession

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: ipsn

Imports commands from another session (for example a remote server) as local proxy commands.

Syntax:

```powershell
Import-PSSession [-Session] <PSSession> [[-CommandName] <string[]>] [[-FormatTypeName] <string[]>] [-Prefix <string>] [-DisableNameChecking] [-AllowClobber] [-ArgumentList <Object[]>] [-CommandType <CommandTypes>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Certificate <X509Certificate2>] [<CommonParameters>]
```

Example:

```powershell
Import-PSSession -Session $s -Module ActiveDirectory -Prefix Rem
```

### Invoke-Expression

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: iex

Runs a string as a PowerShell command; avoid with untrusted input. Alias: iex.

Syntax:

```powershell
Invoke-Expression [-Command] <string> [<CommonParameters>]
```

Example:

```powershell
Invoke-Expression 'Get-Date'
```

### Invoke-RestMethod

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: irm

Sends an HTTP(S) request to a REST API and turns JSON/XML responses into objects. Alias: irm.

Syntax:

```powershell
Invoke-RestMethod [-Uri] <uri> [-Method <WebRequestMethod>] [-UseBasicParsing] [-WebSession <WebRequestSession>] [-SessionVariable <string>] [-Credential <pscredential>] [-UseDefaultCredentials] [-CertificateThumbprint <string>] [-Certificate <X509Certificate>] [-UserAgent <string>] [-DisableKeepAlive] [-TimeoutSec <int>] [-Headers <IDictionary>] [-MaximumRedirection <int>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-ProxyUseDefaultCredentials] [-Body <Object>] [-ContentType <string>] [-TransferEncoding <string>] [-InFile <string>] [-OutFile <string>] [-PassThru] [<CommonParameters>]
```

Example:

```powershell
Invoke-RestMethod -Uri https://api.example.com/items -Method Get
```

### Invoke-WebRequest

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: curl, iwr, wget

Sends an HTTP(S) request and returns the full response (status, headers, content); can download files. Aliases: iwr, curl/wget (5.1).

Syntax:

```powershell
Invoke-WebRequest [-Uri] <uri> [-UseBasicParsing] [-WebSession <WebRequestSession>] [-SessionVariable <string>] [-Credential <pscredential>] [-UseDefaultCredentials] [-CertificateThumbprint <string>] [-Certificate <X509Certificate>] [-UserAgent <string>] [-DisableKeepAlive] [-TimeoutSec <int>] [-Headers <IDictionary>] [-MaximumRedirection <int>] [-Method <WebRequestMethod>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-ProxyUseDefaultCredentials] [-Body <Object>] [-ContentType <string>] [-TransferEncoding <string>] [-InFile <string>] [-OutFile <string>] [-PassThru] [<CommonParameters>]
```

Example:

```powershell
Invoke-WebRequest -Uri https://example.com/file.zip -OutFile .\file.zip
```

### Measure-Command

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Times how long a script block takes to run.

Syntax:

```powershell
Measure-Command [-Expression] <scriptblock> [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Measure-Command { Get-ChildItem C:\Windows -Recurse -ErrorAction SilentlyContinue }
```

### Measure-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: measure

Counts objects and computes sum, average, min and max of a property, or lines/words/characters of text. Alias: measure.

Syntax:

```powershell
Measure-Object [[-Property] <string[]>] [-InputObject <psobject>] [-Sum] [-Average] [-Maximum] [-Minimum] [<CommonParameters>]

Measure-Object [[-Property] <string[]>] [-InputObject <psobject>] [-Line] [-Word] [-Character] [-IgnoreWhiteSpace] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem -File | Measure-Object Length -Sum -Average
```

### New-Alias

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: nal

Creates a new alias for a command. Alias: nal.

Syntax:

```powershell
New-Alias [-Name] <string> [-Value] <string> [-Description <string>] [-Option <ScopedItemOptions>] [-PassThru] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-Alias -Name np -Value notepad.exe
```

### New-Event

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Raises a custom event into the session's event queue.

Syntax:

```powershell
New-Event [-SourceIdentifier] <string> [[-Sender] <psobject>] [[-EventArguments] <psobject[]>] [[-MessageData] <psobject>] [<CommonParameters>]
```

Example:

```powershell
New-Event -SourceIdentifier Custom -MessageData 'hello'
```

### New-Guid

PowerShell 5.1 · module Microsoft.PowerShell.Utility · function

Creates a new random GUID.

Syntax:

```powershell
New-Guid [<CommonParameters>]
```

Example:

```powershell
(New-Guid).Guid
```

### New-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Creates an instance of a .NET or COM object.

Syntax:

```powershell
New-Object [-TypeName] <string> [[-ArgumentList] <Object[]>] [-Property <IDictionary>] [<CommonParameters>]

New-Object [-ComObject] <string> [-Strict] [-Property <IDictionary>] [<CommonParameters>]
```

Example:

```powershell
$list = New-Object System.Collections.Generic.List[string]
```

### New-TemporaryFile

PowerShell 5.1 · module Microsoft.PowerShell.Utility · function

Creates an empty temporary file in the user's temp folder and returns it.

Syntax:

```powershell
New-TemporaryFile [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
$tmp = New-TemporaryFile
```

### New-TimeSpan

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Creates a TimeSpan (a length of time) from parts or between two dates.

Syntax:

```powershell
New-TimeSpan [[-Start] <datetime>] [[-End] <datetime>] [<CommonParameters>]

New-TimeSpan [-Days <int>] [-Hours <int>] [-Minutes <int>] [-Seconds <int>] [<CommonParameters>]
```

Example:

```powershell
New-TimeSpan -Start (Get-Date '2026-01-01') -End (Get-Date)
```

### New-Variable

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: nv

Creates a variable, optionally read-only, constant or in a given scope. Alias: nv.

Syntax:

```powershell
New-Variable [-Name] <string> [[-Value] <Object>] [-Description <string>] [-Option <ScopedItemOptions>] [-Visibility <SessionStateEntryVisibility>] [-Force] [-PassThru] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-Variable -Name MaxItems -Value 100 -Option ReadOnly
```

### Out-File

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Sends output to a text file, as it would appear in the console.

Syntax:

```powershell
Out-File [-FilePath] <string> [[-Encoding] <string>] [-Append] [-Force] [-NoClobber] [-Width <int>] [-NoNewline] [-InputObject <psobject>] [-WhatIf] [-Confirm] [<CommonParameters>]

Out-File [[-Encoding] <string>] -LiteralPath <string> [-Append] [-Force] [-NoClobber] [-Width <int>] [-NoNewline] [-InputObject <psobject>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Out-File -FilePath .\procs.txt -Encoding utf8
```

### Out-GridView

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: ogv

Shows output in an interactive, filterable grid window; -PassThru returns the rows you select. Alias: ogv.

Syntax:

```powershell
Out-GridView [-InputObject <psobject>] [-Title <string>] [-PassThru] [<CommonParameters>]

Out-GridView [-InputObject <psobject>] [-Title <string>] [-Wait] [<CommonParameters>]

Out-GridView [-InputObject <psobject>] [-Title <string>] [-OutputMode <OutputModeOption>] [<CommonParameters>]
```

Example:

```powershell
Get-Service | Out-GridView -PassThru | Restart-Service
```

### Out-Printer

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: lp

(Windows PowerShell only) Sends output to the default or a named printer.

Syntax:

```powershell
Out-Printer [[-Name] <string>] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Content .\report.txt | Out-Printer
```

### Out-String

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Turns objects into the text they would display as (one string, or lines with -Stream).

Syntax:

```powershell
Out-String [-Stream] [-Width <int>] [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
$text = Get-Process | Out-String
```

### Read-Host

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Reads a line of input from the user; -AsSecureString hides it.

Syntax:

```powershell
Read-Host [[-Prompt] <Object>] [-AsSecureString] [<CommonParameters>]
```

Example:

```powershell
$name = Read-Host -Prompt 'Your name'
```

### Register-EngineEvent

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Subscribes to PowerShell engine events (such as exiting) and New-Event events.

Syntax:

```powershell
Register-EngineEvent [-SourceIdentifier] <string> [[-Action] <scriptblock>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

Example:

```powershell
Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { 'bye' | Out-File .\exit.txt }
```

### Register-ObjectEvent

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Subscribes to an event of a .NET object, such as a timer or file watcher.

Syntax:

```powershell
Register-ObjectEvent [-InputObject] <psobject> [-EventName] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

Example:

```powershell
$t = New-Object Timers.Timer 1000; Register-ObjectEvent $t Elapsed -Action { Write-Host tick }; $t.Start()
```

### Remove-Event

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Deletes events from the session's event queue.

Syntax:

```powershell
Remove-Event [-SourceIdentifier] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Event [-EventIdentifier] <int> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-Event -SourceIdentifier ProcStart
```

### Remove-PSBreakpoint

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: rbp

Deletes breakpoints. Alias: rbp.

Syntax:

```powershell
Remove-PSBreakpoint [-Breakpoint] <Breakpoint[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSBreakpoint [-Id] <int[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-PSBreakpoint | Remove-PSBreakpoint
```

### Remove-TypeData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Removes extended type data from the session.

Syntax:

```powershell
Remove-TypeData -TypeData <TypeData> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-TypeData [-TypeName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-TypeData -Path <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-TypeData -TypeName My.Custom.Type
```

### Remove-Variable

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: rv

Deletes a variable and its value. Alias: rv.

Syntax:

```powershell
Remove-Variable [-Name] <string[]> [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-Variable -Name temp
```

### Select-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: select

Picks properties of objects (including calculated ones), or the first/last/unique objects. Alias: select.

Syntax:

```powershell
Select-Object [[-Property] <Object[]>] [-InputObject <psobject>] [-ExcludeProperty <string[]>] [-ExpandProperty <string>] [-Unique] [-Last <int>] [-First <int>] [-Skip <int>] [-Wait] [<CommonParameters>]

Select-Object [[-Property] <Object[]>] [-InputObject <psobject>] [-ExcludeProperty <string[]>] [-ExpandProperty <string>] [-Unique] [-SkipLast <int>] [<CommonParameters>]

Select-Object [-InputObject <psobject>] [-Unique] [-Wait] [-Index <int[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Select-Object -First 5 Name, @{ Name = 'MB'; Expression = { $_.WorkingSet64 / 1MB } }
```

### Select-String

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: sls

Finds text matching a pattern in strings or files, like grep. Alias: sls.

Syntax:

```powershell
Select-String [-Pattern] <string[]> [-Path] <string[]> [-SimpleMatch] [-CaseSensitive] [-Quiet] [-List] [-Include <string[]>] [-Exclude <string[]>] [-NotMatch] [-AllMatches] [-Encoding <string>] [-Context <int[]>] [<CommonParameters>]

Select-String [-Pattern] <string[]> -InputObject <psobject> [-SimpleMatch] [-CaseSensitive] [-Quiet] [-List] [-Include <string[]>] [-Exclude <string[]>] [-NotMatch] [-AllMatches] [-Encoding <string>] [-Context <int[]>] [<CommonParameters>]

Select-String [-Pattern] <string[]> -LiteralPath <string[]> [-SimpleMatch] [-CaseSensitive] [-Quiet] [-List] [-Include <string[]>] [-Exclude <string[]>] [-NotMatch] [-AllMatches] [-Encoding <string>] [-Context <int[]>] [<CommonParameters>]
```

Example:

```powershell
Select-String -Path .\*.log -Pattern 'error' -SimpleMatch
```

### Select-Xml

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Finds nodes in XML documents or strings with an XPath query.

Syntax:

```powershell
Select-Xml [-XPath] <string> [-Xml] <XmlNode[]> [-Namespace <hashtable>] [<CommonParameters>]

Select-Xml [-XPath] <string> [-Path] <string[]> [-Namespace <hashtable>] [<CommonParameters>]

Select-Xml [-XPath] <string> -LiteralPath <string[]> [-Namespace <hashtable>] [<CommonParameters>]

Select-Xml [-XPath] <string> -Content <string[]> [-Namespace <hashtable>] [<CommonParameters>]
```

Example:

```powershell
Select-Xml -Path .\app.config -XPath '//add[@key]' | ForEach-Object { $_.Node.key }
```

### Send-MailMessage

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Sends an email through an SMTP server (marked obsolete; works but is not recommended for new scripts).

Syntax:

```powershell
Send-MailMessage [-To] <string[]> [-Subject] <string> [[-Body] <string>] [[-SmtpServer] <string>] -From <string> [-Attachments <string[]>] [-Bcc <string[]>] [-BodyAsHtml] [-Encoding <Encoding>] [-Cc <string[]>] [-DeliveryNotificationOption <DeliveryNotificationOptions>] [-Priority <MailPriority>] [-Credential <pscredential>] [-UseSsl] [-Port <int>] [<CommonParameters>]
```

Example:

```powershell
Send-MailMessage -To ann@contoso.com -From bot@contoso.com -Subject Test -Body Hi -SmtpServer smtp.contoso.com
```

### Set-Alias

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: sal

Creates or changes an alias for a command. Alias: sal.

Syntax:

```powershell
Set-Alias [-Name] <string> [-Value] <string> [-Description <string>] [-Option <ScopedItemOptions>] [-PassThru] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-Alias -Name ll -Value Get-ChildItem
```

### Set-Date

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Changes the system date and time (needs elevation).

Syntax:

```powershell
Set-Date [-Date] <datetime> [-DisplayHint <DisplayHintType>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Date [-Adjust] <timespan> [-DisplayHint <DisplayHintType>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-Date -Adjust 00:05:00
```

### Set-PSBreakpoint

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: sbp

Sets a breakpoint on a script line, a command, or a variable read/write. Alias: sbp.

Syntax:

```powershell
Set-PSBreakpoint [-Script] <string[]> [-Line] <int[]> [[-Column] <int>] [-Action <scriptblock>] [<CommonParameters>]

Set-PSBreakpoint [[-Script] <string[]>] -Variable <string[]> [-Action <scriptblock>] [-Mode <VariableAccessMode>] [<CommonParameters>]

Set-PSBreakpoint [[-Script] <string[]>] -Command <string[]> [-Action <scriptblock>] [<CommonParameters>]
```

Example:

```powershell
Set-PSBreakpoint -Script .\deploy.ps1 -Line 42
```

### Set-TraceSource

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Configures, starts and stops tracing of PowerShell components.

Syntax:

```powershell
Set-TraceSource [-Name] <string[]> [[-Option] <PSTraceSourceOptions>] [-ListenerOption <TraceOptions>] [-FilePath <string>] [-Force] [-Debugger] [-PSHost] [-PassThru] [<CommonParameters>]

Set-TraceSource [-Name] <string[]> [-RemoveListener <string[]>] [<CommonParameters>]

Set-TraceSource [-Name] <string[]> [-RemoveFileListener <string[]>] [<CommonParameters>]
```

Example:

```powershell
Set-TraceSource -Name ParameterBinding -Option ExecutionFlow -PSHost
```

### Set-Variable

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: set, sv

Sets a variable's value (creating it if needed), scope or options. Alias: set, sv.

Syntax:

```powershell
Set-Variable [-Name] <string[]> [[-Value] <Object>] [-Include <string[]>] [-Exclude <string[]>] [-Description <string>] [-Option <ScopedItemOptions>] [-Force] [-Visibility <SessionStateEntryVisibility>] [-PassThru] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-Variable -Name count -Value 0 -Scope Script
```

### Show-Command

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: shcm

Opens a graphical window to build and run a command by filling in its parameters.

Syntax:

```powershell
Show-Command [[-Name] <string>] [-Height <double>] [-Width <double>] [-NoCommonParameter] [-ErrorPopup] [-PassThru] [<CommonParameters>]
```

Example:

```powershell
Show-Command Get-ChildItem
```

### Sort-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: sort

Sorts objects by one or more properties, ascending or -Descending; -Unique removes duplicates. Alias: sort.

Syntax:

```powershell
Sort-Object [[-Property] <Object[]>] [-Descending] [-Unique] [-InputObject <psobject>] [-Culture <string>] [-CaseSensitive] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem -File | Sort-Object Length -Descending
```

### Start-Sleep

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: sleep

Pauses for a number of seconds or milliseconds. Alias: sleep.

Syntax:

```powershell
Start-Sleep [-Seconds] <int> [<CommonParameters>]

Start-Sleep -Milliseconds <int> [<CommonParameters>]
```

Example:

```powershell
Start-Sleep -Seconds 5
```

### Tee-Object

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: tee

Saves pipeline output to a file or variable and also passes it on. Alias: tee.

Syntax:

```powershell
Tee-Object [-FilePath] <string> [-InputObject <psobject>] [-Append] [<CommonParameters>]

Tee-Object -LiteralPath <string> [-InputObject <psobject>] [<CommonParameters>]

Tee-Object -Variable <string> [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Process | Tee-Object -FilePath .\procs.txt | Select-Object -First 3
```

### Trace-Command

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: trcm

Traces an expression or command, showing internal steps such as parameter binding.

Syntax:

```powershell
Trace-Command [-Name] <string[]> [-Expression] <scriptblock> [[-Option] <PSTraceSourceOptions>] [-InputObject <psobject>] [-ListenerOption <TraceOptions>] [-FilePath <string>] [-Force] [-Debugger] [-PSHost] [<CommonParameters>]

Trace-Command [-Name] <string[]> [-Command] <string> [[-Option] <PSTraceSourceOptions>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [-ListenerOption <TraceOptions>] [-FilePath <string>] [-Force] [-Debugger] [-PSHost] [<CommonParameters>]
```

Example:

```powershell
Trace-Command -Name ParameterBinding -Expression { Get-Item . } -PSHost
```

### Unblock-File

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Removes the downloaded-from-internet mark (Zone.Identifier) from files so scripts can run.

Syntax:

```powershell
Unblock-File [-Path] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Unblock-File -LiteralPath <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-ChildItem .\Downloads\*.ps1 | Unblock-File
```

### Unregister-Event

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Cancels an event subscription.

Syntax:

```powershell
Unregister-Event [-SourceIdentifier] <string> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-Event [-SubscriptionId] <int> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Unregister-Event -SourceIdentifier ProcStart
```

### Update-FormatData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Reloads or adds formatting files (.format.ps1xml) in the session.

Syntax:

```powershell
Update-FormatData [[-AppendPath] <string[]>] [-PrependPath <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-FormatData -PrependPath .\my.format.ps1xml
```

### Update-List

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Adds or removes items in a property that holds a collection.

Syntax:

```powershell
Update-List [[-Property] <string>] [-Add <Object[]>] [-Remove <Object[]>] [-InputObject <psobject>] [<CommonParameters>]

Update-List [[-Property] <string>] -Replace <Object[]> [-InputObject <psobject>] [<CommonParameters>]
```

Example:

```powershell
Get-Mailbox ann | Update-List -Property EmailAddresses -Add 'smtp:ann2@contoso.com' | Set-Mailbox
```

### Update-TypeData

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Adds extended type data (properties, methods) to types in the session.

Syntax:

```powershell
Update-TypeData [[-AppendPath] <string[]>] [-PrependPath <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-TypeData -TypeName <string> [-MemberType <PSMemberTypes>] [-MemberName <string>] [-Value <Object>] [-SecondValue <Object>] [-TypeConverter <type>] [-TypeAdapter <type>] [-SerializationMethod <string>] [-TargetTypeForDeserialization <type>] [-SerializationDepth <int>] [-DefaultDisplayProperty <string>] [-InheritPropertySerializationSet <bool>] [-StringSerializationSource <string>] [-DefaultDisplayPropertySet <string[]>] [-DefaultKeyPropertySet <string[]>] [-PropertySerializationSet <string[]>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-TypeData [-TypeData] <TypeData[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-TypeData -TypeName System.IO.FileInfo -MemberType ScriptProperty -MemberName SizeKB -Value { $this.Length / 1KB }
```

### Wait-Debugger

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Stops a script in the debugger before the next statement (a breakpoint in code).

Syntax:

```powershell
Wait-Debugger [<CommonParameters>]
```

Example:

```powershell
Wait-Debugger
```

### Wait-Event

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Waits until an event is raised (optionally for one source or with a timeout).

Syntax:

```powershell
Wait-Event [[-SourceIdentifier] <string>] [-Timeout <int>] [<CommonParameters>]
```

Example:

```powershell
Wait-Event -SourceIdentifier Custom -Timeout 30
```

### Write-Debug

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Writes a debug message (shown when $DebugPreference or -Debug allows it).

Syntax:

```powershell
Write-Debug [-Message] <string> [<CommonParameters>]
```

Example:

```powershell
Write-Debug "Value is $x"
```

### Write-Error

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Writes an error to the error stream (non-terminating unless -ErrorAction Stop).

Syntax:

```powershell
Write-Error [-Message] <string> [-Category <ErrorCategory>] [-ErrorId <string>] [-TargetObject <Object>] [-RecommendedAction <string>] [-CategoryActivity <string>] [-CategoryReason <string>] [-CategoryTargetName <string>] [-CategoryTargetType <string>] [<CommonParameters>]

Write-Error -Exception <Exception> [-Message <string>] [-Category <ErrorCategory>] [-ErrorId <string>] [-TargetObject <Object>] [-RecommendedAction <string>] [-CategoryActivity <string>] [-CategoryReason <string>] [-CategoryTargetName <string>] [-CategoryTargetType <string>] [<CommonParameters>]

Write-Error -ErrorRecord <ErrorRecord> [-RecommendedAction <string>] [-CategoryActivity <string>] [-CategoryReason <string>] [-CategoryTargetName <string>] [-CategoryTargetType <string>] [<CommonParameters>]
```

Example:

```powershell
Write-Error -Message 'File not found' -Category ObjectNotFound
```

### Write-Host

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Writes text directly to the console with optional colors (not to the pipeline).

Syntax:

```powershell
Write-Host [[-Object] <Object>] [-NoNewline] [-Separator <Object>] [-ForegroundColor <ConsoleColor>] [-BackgroundColor <ConsoleColor>] [<CommonParameters>]
```

Example:

```powershell
Write-Host 'Done' -ForegroundColor Green
```

### Write-Information

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Writes a message to the information stream (stream 6), shown per $InformationPreference.

Syntax:

```powershell
Write-Information [-MessageData] <Object> [[-Tags] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Write-Information 'Starting' -InformationAction Continue
```

### Write-Output

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet · aliases: echo, write

Sends objects down the pipeline (the default for unassigned values). Aliases: echo, write.

Syntax:

```powershell
Write-Output [-InputObject] <psobject[]> [-NoEnumerate] [<CommonParameters>]
```

Example:

```powershell
Write-Output 'hello'
```

### Write-Progress

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Shows a progress bar with activity, status and percent complete.

Syntax:

```powershell
Write-Progress [-Activity] <string> [[-Status] <string>] [[-Id] <int>] [-PercentComplete <int>] [-SecondsRemaining <int>] [-CurrentOperation <string>] [-ParentId <int>] [-Completed] [-SourceId <int>] [<CommonParameters>]
```

Example:

```powershell
1..100 | ForEach-Object { Write-Progress -Activity Copying -PercentComplete $_; Start-Sleep -Milliseconds 20 }
```

### Write-Verbose

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Writes a verbose message (shown with -Verbose or $VerbosePreference = 'Continue').

Syntax:

```powershell
Write-Verbose [-Message] <string> [<CommonParameters>]
```

Example:

```powershell
Write-Verbose 'Connecting to server' -Verbose
```

### Write-Warning

PowerShell 5.1 · module Microsoft.PowerShell.Utility · cmdlet

Writes a warning message to the warning stream.

Syntax:

```powershell
Write-Warning [-Message] <string> [<CommonParameters>]
```

Example:

```powershell
Write-Warning 'Disk space is low'
```

## Microsoft.PowerShell.Security

### ConvertFrom-SecureString

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Turns a SecureString into an encrypted standard string (DPAPI by default, or with a key) for storing.

Syntax:

```powershell
ConvertFrom-SecureString [-SecureString] <securestring> [[-SecureKey] <securestring>] [<CommonParameters>]

ConvertFrom-SecureString [-SecureString] <securestring> [-Key <byte[]>] [<CommonParameters>]
```

Example:

```powershell
Read-Host -AsSecureString | ConvertFrom-SecureString | Set-Content .\pw.txt
```

### ConvertTo-SecureString

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Turns plain text or an encrypted standard string into a SecureString.

Syntax:

```powershell
ConvertTo-SecureString [-String] <string> [[-SecureKey] <securestring>] [<CommonParameters>]

ConvertTo-SecureString [-String] <string> [-AsPlainText] [-Force] [<CommonParameters>]

ConvertTo-SecureString [-String] <string> [-Key <byte[]>] [<CommonParameters>]
```

Example:

```powershell
$pw = Get-Content .\pw.txt | ConvertTo-SecureString
```

### Get-Acl

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Gets the security descriptor (owner and access rules) of a file, folder or registry key.

Syntax:

```powershell
Get-Acl [[-Path] <string[]>] [-Audit] [-AllCentralAccessPolicies] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-UseTransaction] [<CommonParameters>]

Get-Acl -InputObject <psobject> [-Audit] [-AllCentralAccessPolicies] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-UseTransaction] [<CommonParameters>]

Get-Acl [-LiteralPath <string[]>] [-Audit] [-AllCentralAccessPolicies] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
(Get-Acl C:\Data).Access | Format-Table IdentityReference, FileSystemRights
```

### Get-AuthenticodeSignature

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Gets the Authenticode (digital signature) status of a file.

Syntax:

```powershell
Get-AuthenticodeSignature [-FilePath] <string[]> [<CommonParameters>]

Get-AuthenticodeSignature -LiteralPath <string[]> [<CommonParameters>]

Get-AuthenticodeSignature -SourcePathOrExtension <string[]> -Content <byte[]> [<CommonParameters>]
```

Example:

```powershell
Get-AuthenticodeSignature -FilePath .\script.ps1
```

### Get-CmsMessage

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Reads content encrypted in Cryptographic Message Syntax (CMS) format without decrypting it.

Syntax:

```powershell
Get-CmsMessage [-Content] <string> [<CommonParameters>]

Get-CmsMessage [-Path] <string> [<CommonParameters>]

Get-CmsMessage [-LiteralPath] <string> [<CommonParameters>]
```

Example:

```powershell
Get-CmsMessage -Path .\secret.cms
```

### Get-Credential

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Prompts for a user name and password and returns a PSCredential object.

Syntax:

```powershell
Get-Credential [-Credential] <pscredential> [<CommonParameters>]

Get-Credential [[-UserName] <string>] -Message <string> [<CommonParameters>]
```

Example:

```powershell
$cred = Get-Credential -UserName CONTOSO\Ann -Message 'Server login'
```

### Get-ExecutionPolicy

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Shows the effective script execution policy, or the policy for each scope with -List.

Syntax:

```powershell
Get-ExecutionPolicy [[-Scope] <ExecutionPolicyScope>] [-List] [<CommonParameters>]
```

Example:

```powershell
Get-ExecutionPolicy -List
```

### Get-PfxCertificate

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Reads a .pfx/.cer certificate file and returns the certificate.

Syntax:

```powershell
Get-PfxCertificate [-FilePath] <string[]> [<CommonParameters>]

Get-PfxCertificate -LiteralPath <string[]> [<CommonParameters>]
```

Example:

```powershell
Get-PfxCertificate -FilePath .\cert.pfx
```

### New-FileCatalog

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Creates a Windows catalog file (.cat) with hashes of files in a folder, for later integrity checks.

Syntax:

```powershell
New-FileCatalog [-CatalogFilePath] <string> [[-Path] <string[]>] [-CatalogVersion <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-FileCatalog -Path .\MyModule -CatalogFilePath .\MyModule.cat -CatalogVersion 2.0
```

### Protect-CmsMessage

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Encrypts content with a certificate in CMS format.

Syntax:

```powershell
Protect-CmsMessage [-To] <CmsMessageRecipient[]> [-Content] <psobject> [[-OutFile] <string>] [<CommonParameters>]

Protect-CmsMessage [-To] <CmsMessageRecipient[]> [-Path] <string> [[-OutFile] <string>] [<CommonParameters>]

Protect-CmsMessage [-To] <CmsMessageRecipient[]> [-LiteralPath] <string> [[-OutFile] <string>] [<CommonParameters>]
```

Example:

```powershell
Protect-CmsMessage -To .\recipient.cer -Content 'secret' -OutFile .\secret.cms
```

### Set-Acl

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Applies a security descriptor (owner, access rules) to a file, folder or registry key.

Syntax:

```powershell
Set-Acl [-Path] <string[]> [-AclObject] <Object> [[-CentralAccessPolicy] <string>] [-ClearCentralAccessPolicy] [-Passthru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-Acl [-InputObject] <psobject> [-AclObject] <Object> [-Passthru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-Acl [-AclObject] <Object> [[-CentralAccessPolicy] <string>] -LiteralPath <string[]> [-ClearCentralAccessPolicy] [-Passthru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

Example:

```powershell
$acl = Get-Acl .\a.txt; Set-Acl -Path .\b.txt -AclObject $acl
```

### Set-AuthenticodeSignature

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Signs a script or file with a code-signing certificate.

Syntax:

```powershell
Set-AuthenticodeSignature [-FilePath] <string[]> [-Certificate] <X509Certificate2> [-IncludeChain <string>] [-TimestampServer <string>] [-HashAlgorithm <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-AuthenticodeSignature [-Certificate] <X509Certificate2> -LiteralPath <string[]> [-IncludeChain <string>] [-TimestampServer <string>] [-HashAlgorithm <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-AuthenticodeSignature [-Certificate] <X509Certificate2> -SourcePathOrExtension <string[]> -Content <byte[]> [-IncludeChain <string>] [-TimestampServer <string>] [-HashAlgorithm <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-AuthenticodeSignature -FilePath .\script.ps1 -Certificate (Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert)[0]
```

### Set-ExecutionPolicy

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Sets the script execution policy (Restricted, RemoteSigned, AllSigned, Bypass...) for a scope.

Syntax:

```powershell
Set-ExecutionPolicy [-ExecutionPolicy] <ExecutionPolicy> [[-Scope] <ExecutionPolicyScope>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Test-FileCatalog

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Checks files against a catalog file (.cat) to see whether any were changed.

Syntax:

```powershell
Test-FileCatalog [-CatalogFilePath] <string> [[-Path] <string[]>] [-Detailed] [-FilesToSkip <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Test-FileCatalog -Path .\MyModule -CatalogFilePath .\MyModule.cat
```

### Unprotect-CmsMessage

PowerShell 5.1 · module Microsoft.PowerShell.Security · cmdlet

Decrypts CMS-encrypted content with your certificate's private key.

Syntax:

```powershell
Unprotect-CmsMessage [-EventLogRecord] <EventLogRecord> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]

Unprotect-CmsMessage [-Content] <string> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]

Unprotect-CmsMessage [-Path] <string> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]

Unprotect-CmsMessage [-LiteralPath] <string> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]
```

Example:

```powershell
Unprotect-CmsMessage -Path .\secret.cms
```

## Microsoft.PowerShell.Diagnostics

### Export-Counter

PowerShell 5.1 · module Microsoft.PowerShell.Diagnostics · cmdlet

Saves performance counter samples (from Get-Counter or Import-Counter) to a .blg, .csv or .tsv log file.

Syntax:

```powershell
Export-Counter [-Path] <string> -InputObject <PerformanceCounterSampleSet[]> [-FileFormat <string>] [-MaxSize <uint32>] [-Force] [-Circular] [<CommonParameters>]
```

Example:

```powershell
Get-Counter '\Processor(_Total)\% Processor Time' -MaxSamples 10 | Export-Counter -Path .\cpu.blg
```

### Get-Counter

PowerShell 5.1 · module Microsoft.PowerShell.Diagnostics · cmdlet

Reads performance counter data (CPU, memory, disk...) from the local or remote computers.

Syntax:

```powershell
Get-Counter [[-Counter] <string[]>] [-SampleInterval <int>] [-MaxSamples <long>] [-Continuous] [-ComputerName <string[]>] [<CommonParameters>]

Get-Counter [-ListSet] <string[]> [-ComputerName <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-Counter '\Memory\Available MBytes' -SampleInterval 2 -MaxSamples 5
```

### Get-WinEvent

PowerShell 5.1 · module Microsoft.PowerShell.Diagnostics · cmdlet

Gets events from event logs (classic and modern) and .evtx/.etl files, with fast filtering by hash table or XPath.

Syntax:

```powershell
Get-WinEvent [[-LogName] <string[]>] [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-FilterXPath <string>] [-Force] [-Oldest] [<CommonParameters>]

Get-WinEvent [-ListLog] <string[]> [-ComputerName <string>] [-Credential <pscredential>] [-Force] [<CommonParameters>]

Get-WinEvent [-ListProvider] <string[]> [-ComputerName <string>] [-Credential <pscredential>] [<CommonParameters>]

Get-WinEvent [-ProviderName] <string[]> [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-FilterXPath <string>] [-Force] [-Oldest] [<CommonParameters>]

Get-WinEvent [-Path] <string[]> [-MaxEvents <long>] [-Credential <pscredential>] [-FilterXPath <string>] [-Oldest] [<CommonParameters>]

Get-WinEvent [-FilterHashtable] <hashtable[]> [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-Force] [-Oldest] [<CommonParameters>]

Get-WinEvent [-FilterXml] <xml> [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-Oldest] [<CommonParameters>]
```

Example:

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 2; StartTime = (Get-Date).AddDays(-1) }
```

### Import-Counter

PowerShell 5.1 · module Microsoft.PowerShell.Diagnostics · cmdlet

Reads performance counter log files (.blg, .csv, .tsv) back into counter sample objects.

Syntax:

```powershell
Import-Counter [-Path] <string[]> [-StartTime <datetime>] [-EndTime <datetime>] [-Counter <string[]>] [-MaxSamples <long>] [<CommonParameters>]

Import-Counter [-Path] <string[]> -ListSet <string[]> [<CommonParameters>]

Import-Counter [-Path] <string[]> [-Summary] [<CommonParameters>]
```

Example:

```powershell
Import-Counter -Path .\cpu.blg | Select-Object -ExpandProperty CounterSamples
```

### New-WinEvent

PowerShell 5.1 · module Microsoft.PowerShell.Diagnostics · cmdlet

Writes an event for a registered event provider (by provider name and event ID) to its log.

Syntax:

```powershell
New-WinEvent [-ProviderName] <string> [-Id] <int> [[-Payload] <Object[]>] [-Version <byte>] [<CommonParameters>]
```

Example:

```powershell
New-WinEvent -ProviderName Microsoft-Windows-PowerShell -Id 45090 -Payload @('Test', 'Message')
```

## Microsoft.PowerShell.Host

### Start-Transcript

PowerShell 5.1 · module Microsoft.PowerShell.Host · cmdlet

Records everything typed and shown in the session to a text file until Stop-Transcript.

Syntax:

```powershell
Start-Transcript [[-Path] <string>] [-Append] [-Force] [-NoClobber] [-IncludeInvocationHeader] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Transcript [[-LiteralPath] <string>] [-Append] [-Force] [-NoClobber] [-IncludeInvocationHeader] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Transcript [[-OutputDirectory] <string>] [-Append] [-Force] [-NoClobber] [-IncludeInvocationHeader] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Start-Transcript -Path C:\Logs\session.txt -Append
```

### Stop-Transcript

PowerShell 5.1 · module Microsoft.PowerShell.Host · cmdlet

Stops the transcript started by Start-Transcript and closes the file.

Syntax:

```powershell
Stop-Transcript [<CommonParameters>]
```

Example:

```powershell
Stop-Transcript
```

## Microsoft.PowerShell.Archive

### Compress-Archive

PowerShell 5.1 · module Microsoft.PowerShell.Archive · function

Creates or updates a ZIP file from files and folders.

Syntax:

```powershell
Compress-Archive [-Path] <string[]> [-DestinationPath] <string> [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-Path] <string[]> [-DestinationPath] <string> -Update [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-Path] <string[]> [-DestinationPath] <string> -Force [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-DestinationPath] <string> -LiteralPath <string[]> -Update [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-DestinationPath] <string> -LiteralPath <string[]> -Force [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-DestinationPath] <string> -LiteralPath <string[]> [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Compress-Archive -Path .\Reports\* -DestinationPath .\Reports.zip -Update
```

### Expand-Archive

PowerShell 5.1 · module Microsoft.PowerShell.Archive · function

Extracts the contents of a ZIP file to a folder.

Syntax:

```powershell
Expand-Archive [-Path] <string> [[-DestinationPath] <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Expand-Archive [[-DestinationPath] <string>] -LiteralPath <string> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Expand-Archive -Path .\Reports.zip -DestinationPath .\Reports -Force
```

## Microsoft.PowerShell.LocalAccounts

### Add-LocalGroupMember

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: algm

Adds users or groups to a local group.

Syntax:

```powershell
Add-LocalGroupMember [-Group] <LocalGroup> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Add-LocalGroupMember [-Name] <string> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Add-LocalGroupMember [-SID] <SecurityIdentifier> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Add-LocalGroupMember -Group Administrators -Member CONTOSO\Ann
```

### Disable-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: dlu

Disables a local user account.

Syntax:

```powershell
Disable-LocalUser [-InputObject] <LocalUser[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-LocalUser [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-LocalUser [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Disable-LocalUser -Name Guest
```

### Enable-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: elu

Enables a local user account.

Syntax:

```powershell
Enable-LocalUser [-InputObject] <LocalUser[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-LocalUser [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-LocalUser [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-LocalUser -Name Support
```

### Get-LocalGroup

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: glg

Lists local security groups.

Syntax:

```powershell
Get-LocalGroup [[-Name] <string[]>] [<CommonParameters>]

Get-LocalGroup [[-SID] <SecurityIdentifier[]>] [<CommonParameters>]
```

Example:

```powershell
Get-LocalGroup
```

### Get-LocalGroupMember

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: glgm

Lists the members of a local group.

Syntax:

```powershell
Get-LocalGroupMember [-Name] <string> [[-Member] <string>] [<CommonParameters>]

Get-LocalGroupMember [-Group] <LocalGroup> [[-Member] <string>] [<CommonParameters>]

Get-LocalGroupMember [-SID] <SecurityIdentifier> [[-Member] <string>] [<CommonParameters>]
```

Example:

```powershell
Get-LocalGroupMember -Group Administrators
```

### Get-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: glu

Lists local user accounts.

Syntax:

```powershell
Get-LocalUser [[-Name] <string[]>] [<CommonParameters>]

Get-LocalUser [[-SID] <SecurityIdentifier[]>] [<CommonParameters>]
```

Example:

```powershell
Get-LocalUser | Where-Object Enabled
```

### New-LocalGroup

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: nlg

Creates a local security group.

Syntax:

```powershell
New-LocalGroup [-Name] <string> [-Description <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-LocalGroup -Name Testers -Description 'Test team'
```

### New-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: nlu

Creates a local user account.

Syntax:

```powershell
New-LocalUser [-Name] <string> -Password <securestring> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-Disabled] [-FullName <string>] [-PasswordNeverExpires] [-UserMayNotChangePassword] [-WhatIf] [-Confirm] [<CommonParameters>]

New-LocalUser [-Name] <string> -NoPassword [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-Disabled] [-FullName <string>] [-UserMayNotChangePassword] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-LocalUser -Name Support -Password (Read-Host -AsSecureString) -FullName 'Support Account'
```

### Remove-LocalGroup

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: rlg

Deletes local security groups.

Syntax:

```powershell
Remove-LocalGroup [-InputObject] <LocalGroup[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroup [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroup [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-LocalGroup -Name Testers
```

### Remove-LocalGroupMember

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: rlgm

Removes members from a local group.

Syntax:

```powershell
Remove-LocalGroupMember [-Group] <LocalGroup> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroupMember [-Name] <string> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroupMember [-SID] <SecurityIdentifier> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-LocalGroupMember -Group Administrators -Member CONTOSO\Ann
```

### Remove-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: rlu

Deletes local user accounts.

Syntax:

```powershell
Remove-LocalUser [-InputObject] <LocalUser[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalUser [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalUser [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-LocalUser -Name Support
```

### Rename-LocalGroup

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: rnlg

Renames a local group.

Syntax:

```powershell
Rename-LocalGroup [-InputObject] <LocalGroup> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalGroup [-Name] <string> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalGroup [-SID] <SecurityIdentifier> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Rename-LocalGroup -Name Testers -NewName QA
```

### Rename-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: rnlu

Renames a local user account.

Syntax:

```powershell
Rename-LocalUser [-InputObject] <LocalUser> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalUser [-Name] <string> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalUser [-SID] <SecurityIdentifier> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Rename-LocalUser -Name Support -NewName HelpDesk
```

### Set-LocalGroup

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: slg

Changes a local group's description.

Syntax:

```powershell
Set-LocalGroup [-InputObject] <LocalGroup> -Description <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalGroup [-Name] <string> -Description <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalGroup [-SID] <SecurityIdentifier> -Description <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-LocalGroup -Name QA -Description 'Quality assurance'
```

### Set-LocalUser

PowerShell 5.1 · module Microsoft.PowerShell.LocalAccounts · cmdlet · aliases: slu

Changes a local user account (password, description, expiry...).

Syntax:

```powershell
Set-LocalUser [-Name] <string> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-FullName <string>] [-Password <securestring>] [-PasswordNeverExpires <bool>] [-UserMayChangePassword <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalUser [-InputObject] <LocalUser> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-FullName <string>] [-Password <securestring>] [-PasswordNeverExpires <bool>] [-UserMayChangePassword <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalUser [-SID] <SecurityIdentifier> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-FullName <string>] [-Password <securestring>] [-PasswordNeverExpires <bool>] [-UserMayChangePassword <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-LocalUser -Name HelpDesk -PasswordNeverExpires $true
```

## Microsoft.PowerShell.ODataUtils

### Export-ODataEndpointProxy

PowerShell 5.1 · module Microsoft.PowerShell.ODataUtils · function

Generates a module of cmdlets for managing an OData endpoint's resources.

Syntax:

```powershell
Export-ODataEndpointProxy [-Uri] <string> [-OutputModule] <string> [[-MetadataUri] <string>] [[-Credential] <pscredential>] [[-CreateRequestMethod] <string>] [[-UpdateRequestMethod] <string>] [[-CmdletAdapter] <string>] [[-ResourceNameMapping] <hashtable>] [-Force] [[-CustomData] <hashtable>] [-AllowClobber] [-AllowUnsecureConnection] [[-Headers] <hashtable>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Export-ODataEndpointProxy -Uri 'http://server/odata.svc' -OutputModule .\ODataProxy
```

## Microsoft.PowerShell.Operation.Validation

### Get-OperationValidation

PowerShell 5.1 · module Microsoft.PowerShell.Operation.Validation · function

Finds Pester-based operational validation tests in installed modules.

Syntax:

```powershell
Get-OperationValidation [[-ModuleName] <string[]>] [-TestType <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-OperationValidation -ModuleName MyChecks
```

### Invoke-OperationValidation

PowerShell 5.1 · module Microsoft.PowerShell.Operation.Validation · function

Runs operational validation tests found by Get-OperationValidation.

Syntax:

```powershell
Invoke-OperationValidation [-TestInfo <psobject[]>] [-IncludePesterOutput] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-OperationValidation [-testFilePath <string[]>] [-IncludePesterOutput] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-OperationValidation [-ModuleName <string[]>] [-TestType <string[]>] [-IncludePesterOutput] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-OperationValidation -ModuleName MyChecks | Invoke-OperationValidation
```

## Microsoft.WSMan.Management

### Connect-WSMan

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Connects to the WinRM service on a remote computer so the WSMan: drive can browse it.

Syntax:

```powershell
Connect-WSMan [[-ComputerName] <string>] [-ApplicationName <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Connect-WSMan [-ConnectionURI <uri>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
Connect-WSMan -ComputerName Server01
```

### Disable-WSManCredSSP

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Turns off CredSSP authentication (client or server role).

Syntax:

```powershell
Disable-WSManCredSSP [-Role] <string> [<CommonParameters>]
```

Example:

```powershell
Disable-WSManCredSSP -Role Client
```

### Disconnect-WSMan

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Disconnects from a remote computer's WinRM service.

Syntax:

```powershell
Disconnect-WSMan [[-ComputerName] <string>] [<CommonParameters>]
```

Example:

```powershell
Disconnect-WSMan -ComputerName Server01
```

### Enable-WSManCredSSP

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Enables CredSSP authentication so credentials can be delegated to remote computers.

Syntax:

```powershell
Enable-WSManCredSSP [-Role] <string> [[-DelegateComputer] <string[]>] [-Force] [<CommonParameters>]
```

Example:

```powershell
Enable-WSManCredSSP -Role Client -DelegateComputer Server01.contoso.com
```

### Get-WSManCredSSP

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Shows the computer's CredSSP configuration.

Syntax:

```powershell
Get-WSManCredSSP [<CommonParameters>]
```

Example:

```powershell
Get-WSManCredSSP
```

### Get-WSManInstance

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Gets management information for a resource instance by resource URI.

Syntax:

```powershell
Get-WSManInstance [-ResourceURI] <uri> [-ApplicationName <string>] [-ComputerName <string>] [-ConnectionURI <uri>] [-Dialect <uri>] [-Fragment <string>] [-OptionSet <hashtable>] [-Port <int>] [-SelectorSet <hashtable>] [-SessionOption <SessionOption>] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Get-WSManInstance [-ResourceURI] <uri> -Enumerate [-ApplicationName <string>] [-BasePropertiesOnly] [-ComputerName <string>] [-ConnectionURI <uri>] [-Dialect <uri>] [-Filter <string>] [-OptionSet <hashtable>] [-Port <int>] [-Associations] [-ReturnType <string>] [-SessionOption <SessionOption>] [-Shallow] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
Get-WSManInstance -ResourceURI winrm/config/listener -Enumerate
```

### Invoke-WSManAction

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Invokes an action (method) on a resource by resource URI and selectors.

Syntax:

```powershell
Invoke-WSManAction [-ResourceURI] <uri> [-Action] <string> [[-SelectorSet] <hashtable>] [-ConnectionURI <uri>] [-FilePath <string>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Invoke-WSManAction [-ResourceURI] <uri> [-Action] <string> [[-SelectorSet] <hashtable>] [-ApplicationName <string>] [-ComputerName <string>] [-FilePath <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
Invoke-WSManAction -Action StartService -ResourceURI wmicimv2/win32_service -SelectorSet @{ Name = 'Spooler' }
```

### New-WSManInstance

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Creates a new instance of a management resource (for example a WinRM listener).

Syntax:

```powershell
New-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ApplicationName <string>] [-ComputerName <string>] [-FilePath <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

New-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ConnectionURI <uri>] [-FilePath <string>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
New-WSManInstance -ResourceURI winrm/config/Listener -SelectorSet @{ Address = '*'; Transport = 'HTTPS' } -ValueSet @{ Hostname = 'srv'; CertificateThumbprint = 'AB12...' }
```

### New-WSManSessionOption

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Creates session options (timeouts, proxy, skip checks) for WSMan cmdlets.

Syntax:

```powershell
New-WSManSessionOption [-ProxyAccessType <ProxyAccessType>] [-ProxyAuthentication <ProxyAuthentication>] [-ProxyCredential <pscredential>] [-SkipCACheck] [-SkipCNCheck] [-SkipRevocationCheck] [-SPNPort <int>] [-OperationTimeout <int>] [-NoEncryption] [-UseUTF16] [<CommonParameters>]
```

Example:

```powershell
$o = New-WSManSessionOption -OperationTimeout 30000
```

### Remove-WSManInstance

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Deletes a management resource instance.

Syntax:

```powershell
Remove-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ApplicationName <string>] [-ComputerName <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Remove-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ConnectionURI <uri>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
Remove-WSManInstance -ResourceURI winrm/config/Listener -SelectorSet @{ Address = '*'; Transport = 'HTTPS' }
```

### Set-WSManInstance

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Changes management settings of a resource.

Syntax:

```powershell
Set-WSManInstance [-ResourceURI] <uri> [[-SelectorSet] <hashtable>] [-ApplicationName <string>] [-ComputerName <string>] [-Dialect <uri>] [-FilePath <string>] [-Fragment <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Set-WSManInstance [-ResourceURI] <uri> [[-SelectorSet] <hashtable>] [-ConnectionURI <uri>] [-Dialect <uri>] [-FilePath <string>] [-Fragment <string>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
Set-WSManInstance -ResourceURI winrm/config/service -ValueSet @{ MaxConcurrentOperationsPerUser = 100 }
```

### Set-WSManQuickConfig

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Configures this computer for WS-Management remoting (WinRM service, listener, firewall).

Syntax:

```powershell
Set-WSManQuickConfig [-UseSSL] [-Force] [-SkipNetworkProfileCheck] [<CommonParameters>]
```

Example:

```powershell
Set-WSManQuickConfig -Force
```

### Test-WSMan

PowerShell 5.1 · module Microsoft.WSMan.Management · cmdlet

Tests whether the WinRM service is running and reachable on a computer.

Syntax:

```powershell
Test-WSMan [[-ComputerName] <string>] [-Authentication <AuthenticationMechanism>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-Credential <pscredential>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

Example:

```powershell
Test-WSMan -ComputerName Server01
```

## CimCmdlets

### Export-BinaryMiLog

PowerShell 5.1 · module CimCmdlets · cmdlet

Saves CIM instances to a binary MI log file.

Syntax:

```powershell
Export-BinaryMiLog [-Path] <string> [-InputObject <ciminstance>] [<CommonParameters>]
```

Example:

```powershell
Get-CimInstance Win32_Service | Export-BinaryMiLog -Path .\services.bmil
```

### Get-CimAssociatedInstance

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: gcai

Gets the CIM instances linked to a given instance by an association.

Syntax:

```powershell
Get-CimAssociatedInstance [-InputObject] <ciminstance> [[-Association] <string>] [-ResultClassName <string>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ResourceUri <uri>] [-ComputerName <string[]>] [-KeyOnly] [<CommonParameters>]

Get-CimAssociatedInstance [-InputObject] <ciminstance> [[-Association] <string>] -CimSession <CimSession[]> [-ResultClassName <string>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ResourceUri <uri>] [-KeyOnly] [<CommonParameters>]
```

Example:

```powershell
Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" | Get-CimAssociatedInstance -ResultClassName Win32_DiskPartition
```

### Get-CimClass

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: gcls

Lists CIM classes in a namespace, with their properties and methods.

Syntax:

```powershell
Get-CimClass [[-ClassName] <string>] [[-Namespace] <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-MethodName <string>] [-PropertyName <string>] [-QualifierName <string>] [<CommonParameters>]

Get-CimClass [[-ClassName] <string>] [[-Namespace] <string>] -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-MethodName <string>] [-PropertyName <string>] [-QualifierName <string>] [<CommonParameters>]
```

Example:

```powershell
Get-CimClass -ClassName Win32_*Disk*
```

### Get-CimInstance

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: gcim

Gets instances of a CIM/WMI class (the modern replacement for Get-WmiObject).

Syntax:

```powershell
Get-CimInstance [-ClassName] <string> [-ComputerName <string[]>] [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance -CimSession <CimSession[]> -ResourceUri <uri> [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance -CimSession <CimSession[]> -Query <string> [-ResourceUri <uri>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [<CommonParameters>]

Get-CimInstance [-ClassName] <string> -CimSession <CimSession[]> [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance [-InputObject] <ciminstance> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [<CommonParameters>]

Get-CimInstance [-InputObject] <ciminstance> [-ResourceUri <uri>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [<CommonParameters>]

Get-CimInstance -ResourceUri <uri> [-ComputerName <string[]>] [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance -Query <string> [-ResourceUri <uri>] [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [<CommonParameters>]
```

Example:

```powershell
Get-CimInstance -ClassName Win32_OperatingSystem | Select-Object Caption, LastBootUpTime
```

### Get-CimSession

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: gcms

Lists the CIM sessions created in this session.

Syntax:

```powershell
Get-CimSession [[-ComputerName] <string[]>] [<CommonParameters>]

Get-CimSession [-Id] <uint32[]> [<CommonParameters>]

Get-CimSession -InstanceId <guid[]> [<CommonParameters>]

Get-CimSession -Name <string[]> [<CommonParameters>]
```

Example:

```powershell
Get-CimSession
```

### Import-BinaryMiLog

PowerShell 5.1 · module CimCmdlets · cmdlet

Re-creates CIM objects from a binary MI log file.

Syntax:

```powershell
Import-BinaryMiLog [-Path] <string> [<CommonParameters>]
```

Example:

```powershell
Import-BinaryMiLog -Path .\services.bmil
```

### Invoke-CimMethod

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: icim

Calls a method of a CIM class or instance.

Syntax:

```powershell
Invoke-CimMethod [-ClassName] <string> [[-Arguments] <IDictionary>] [-MethodName] <string> [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-ClassName] <string> [[-Arguments] <IDictionary>] [-MethodName] <string> -CimSession <CimSession[]> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -ResourceUri <uri> [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-InputObject] <ciminstance> [[-Arguments] <IDictionary>] [-MethodName] <string> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-InputObject] <ciminstance> [[-Arguments] <IDictionary>] [-MethodName] <string> [-ResourceUri <uri>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -ResourceUri <uri> -CimSession <CimSession[]> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-CimClass] <cimclass> [[-Arguments] <IDictionary>] [-MethodName] <string> [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-CimClass] <cimclass> [[-Arguments] <IDictionary>] [-MethodName] <string> -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -Query <string> [-QueryDialect <string>] [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -Query <string> -CimSession <CimSession[]> [-QueryDialect <string>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = 'notepad.exe' }
```

### New-CimInstance

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: ncim

Creates a new CIM instance on a CIM server.

Syntax:

```powershell
New-CimInstance [-ClassName] <string> [[-Property] <IDictionary>] [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [-ClassName] <string> [[-Property] <IDictionary>] -CimSession <CimSession[]> [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [[-Property] <IDictionary>] -ResourceUri <uri> -CimSession <CimSession[]> [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [[-Property] <IDictionary>] -ResourceUri <uri> [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [-CimClass] <cimclass> [[-Property] <IDictionary>] -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [-CimClass] <cimclass> [[-Property] <IDictionary>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-CimInstance -ClassName Win32_Environment -Property @{ Name = 'MYVAR'; VariableValue = '1'; UserName = '<SYSTEM>' }
```

### New-CimSession

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: ncms

Opens a CIM session to a local or remote computer (WSMan or DCOM) for CIM cmdlets.

Syntax:

```powershell
New-CimSession [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-Authentication <PasswordAuthenticationMechanism>] [-Name <string>] [-OperationTimeoutSec <uint32>] [-SkipTestConnection] [-Port <uint32>] [-SessionOption <CimSessionOptions>] [<CommonParameters>]

New-CimSession [[-ComputerName] <string[]>] [-CertificateThumbprint <string>] [-Name <string>] [-OperationTimeoutSec <uint32>] [-SkipTestConnection] [-Port <uint32>] [-SessionOption <CimSessionOptions>] [<CommonParameters>]
```

Example:

```powershell
$cs = New-CimSession -ComputerName Server01
```

### New-CimSessionOption

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: ncso

Creates options for New-CimSession, such as using DCOM instead of WSMan.

Syntax:

```powershell
New-CimSessionOption [-Protocol] <ProtocolType> [-UICulture <cultureinfo>] [-Culture <cultureinfo>] [<CommonParameters>]

New-CimSessionOption [-NoEncryption] [-SkipCACheck] [-SkipCNCheck] [-SkipRevocationCheck] [-EncodePortInServicePrincipalName] [-Encoding <PacketEncoding>] [-HttpPrefix <uri>] [-MaxEnvelopeSizeKB <uint32>] [-ProxyAuthentication <PasswordAuthenticationMechanism>] [-ProxyCertificateThumbprint <string>] [-ProxyCredential <pscredential>] [-ProxyType <ProxyType>] [-UseSsl] [-UICulture <cultureinfo>] [-Culture <cultureinfo>] [<CommonParameters>]

New-CimSessionOption [-Impersonation <ImpersonationType>] [-PacketIntegrity] [-PacketPrivacy] [-UICulture <cultureinfo>] [-Culture <cultureinfo>] [<CommonParameters>]
```

Example:

```powershell
New-CimSession -ComputerName OldServer -SessionOption (New-CimSessionOption -Protocol Dcom)
```

### Register-CimIndicationEvent

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: rcie

Subscribes to CIM indications (events) with a class name or WQL query.

Syntax:

```powershell
Register-CimIndicationEvent [-ClassName] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-CimIndicationEvent [-ClassName] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] -CimSession <CimSession> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-CimIndicationEvent [-Query] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] -CimSession <CimSession> [-Namespace <string>] [-QueryDialect <string>] [-OperationTimeoutSec <uint32>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-CimIndicationEvent [-Query] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-QueryDialect <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

Example:

```powershell
Register-CimIndicationEvent -ClassName Win32_ProcessStartTrace -SourceIdentifier ProcStart
```

### Remove-CimInstance

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: rcim

Deletes a CIM instance.

Syntax:

```powershell
Remove-CimInstance [-InputObject] <ciminstance> [-ResourceUri <uri>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimInstance [-InputObject] <ciminstance> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimInstance [-Query] <string> [[-Namespace] <string>] -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimInstance [-Query] <string> [[-Namespace] <string>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-CimInstance Win32_Environment -Filter "Name='MYVAR'" | Remove-CimInstance
```

### Remove-CimSession

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: rcms

Closes CIM sessions.

Syntax:

```powershell
Remove-CimSession [-CimSession] <CimSession[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession [-ComputerName] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession [-Id] <uint32[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession -InstanceId <guid[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession -Name <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-CimSession | Remove-CimSession
```

### Set-CimInstance

PowerShell 5.1 · module CimCmdlets · cmdlet · aliases: scim

Changes properties of a CIM instance.

Syntax:

```powershell
Set-CimInstance [-InputObject] <ciminstance> [-ComputerName <string[]>] [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-Property <IDictionary>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-CimInstance [-Query] <string> -CimSession <CimSession[]> -Property <IDictionary> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-CimInstance [-InputObject] <ciminstance> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-Property <IDictionary>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-CimInstance [-Query] <string> -Property <IDictionary> [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-CimInstance Win32_Environment -Filter "Name='MYVAR'" | Set-CimInstance -Property @{ VariableValue = '2' }
```

## PackageManagement

### Find-Package

PowerShell 5.1 · module PackageManagement · cmdlet

Searches registered package sources for software packages.

Syntax:

```powershell
Find-Package [[-Name] <string[]>] [-IncludeDependencies] [-AllVersions] [-Source <string[]>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [<CommonParameters>]
```

Example:

```powershell
Find-Package -Name jquery -ProviderName NuGet
```

### Find-PackageProvider

PowerShell 5.1 · module PackageManagement · cmdlet

Lists package providers available to install.

Syntax:

```powershell
Find-PackageProvider [[-Name] <string[]>] [-AllVersions] [-Source <string[]>] [-IncludeDependencies] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Force] [-ForceBootstrap] [<CommonParameters>]
```

Example:

```powershell
Find-PackageProvider -Name NuGet
```

### Get-Package

PowerShell 5.1 · module PackageManagement · cmdlet

Lists packages installed through PackageManagement (and programs on Windows).

Syntax:

```powershell
Get-Package [[-Name] <string[]>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Get-Package [[-Name] <string[]>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-AdditionalArguments <string[]>] [<CommonParameters>]

Get-Package [[-Name] <string[]>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-PackageManagementProvider <string>] [-Type <string>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]
```

Example:

```powershell
Get-Package -ProviderName Programs
```

### Get-PackageProvider

PowerShell 5.1 · module PackageManagement · cmdlet

Lists the package providers loaded or available on the computer.

Syntax:

```powershell
Get-PackageProvider [[-Name] <string[]>] [-ListAvailable] [-Force] [-ForceBootstrap] [<CommonParameters>]
```

Example:

```powershell
Get-PackageProvider -ListAvailable
```

### Get-PackageSource

PowerShell 5.1 · module PackageManagement · cmdlet

Lists the package sources registered for providers.

Syntax:

```powershell
Get-PackageSource [[-Name] <string>] [-Location <string>] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

Example:

```powershell
Get-PackageSource
```

### Import-PackageProvider

PowerShell 5.1 · module PackageManagement · cmdlet

Loads a package provider into the session.

Syntax:

```powershell
Import-PackageProvider [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Force] [-ForceBootstrap] [<CommonParameters>]
```

Example:

```powershell
Import-PackageProvider -Name NuGet
```

### Install-Package

PowerShell 5.1 · module PackageManagement · cmdlet

Installs software packages from a package source.

Syntax:

```powershell
Install-Package [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Source <string[]>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string[]>] [<CommonParameters>]

Install-Package [-InputObject] <SoftwareIdentity[]> [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]
```

Example:

```powershell
Install-Package -Name Newtonsoft.Json -ProviderName NuGet -Scope CurrentUser
```

### Install-PackageProvider

PowerShell 5.1 · module PackageManagement · cmdlet

Installs a package provider such as NuGet.

Syntax:

```powershell
Install-PackageProvider [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Credential <pscredential>] [-Scope <string>] [-Source <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-PackageProvider [-InputObject] <SoftwareIdentity[]> [-Scope <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force
```

### Register-PackageSource

PowerShell 5.1 · module PackageManagement · cmdlet

Adds a package source for a provider.

Syntax:

```powershell
Register-PackageSource [[-Name] <string>] [[-Location] <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string>] [<CommonParameters>]

Register-PackageSource [[-Name] <string>] [[-Location] <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

Example:

```powershell
Register-PackageSource -Name MyFeed -Location \\Server\Packages -ProviderName NuGet -Trusted
```

### Save-Package

PowerShell 5.1 · module PackageManagement · cmdlet

Downloads packages to a folder without installing them.

Syntax:

```powershell
Save-Package [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Source <string[]>] [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string[]>] [<CommonParameters>]

Save-Package -InputObject <SoftwareIdentity> [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Package [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [<CommonParameters>]

Save-Package [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [<CommonParameters>]
```

Example:

```powershell
Save-Package -Name Newtonsoft.Json -ProviderName NuGet -Path .\pkgs
```

### Set-PackageSource

PowerShell 5.1 · module PackageManagement · cmdlet

Changes a registered package source (name, location, trust).

Syntax:

```powershell
Set-PackageSource [[-Name] <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Location <string>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string>] [<CommonParameters>]

Set-PackageSource -InputObject <PackageSource> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-PackageSource [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]

Set-PackageSource [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

Example:

```powershell
Set-PackageSource -Name MyFeed -Trusted
```

### Uninstall-Package

PowerShell 5.1 · module PackageManagement · cmdlet

Uninstalls packages installed through PackageManagement.

Syntax:

```powershell
Uninstall-Package [-InputObject] <SoftwareIdentity[]> [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Uninstall-Package [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string[]>] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-Type <string>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-Type <string>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]
```

Example:

```powershell
Uninstall-Package -Name Newtonsoft.Json
```

### Unregister-PackageSource

PowerShell 5.1 · module PackageManagement · cmdlet

Removes a registered package source.

Syntax:

```powershell
Unregister-PackageSource [[-Source] <string>] [-Location <string>] [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string>] [<CommonParameters>]

Unregister-PackageSource -InputObject <PackageSource[]> [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-PackageSource [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]

Unregister-PackageSource [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

Example:

```powershell
Unregister-PackageSource -Name MyFeed
```

## PowerShellGet

### Find-Command

PowerShell 5.1 · module PowerShellGet · function

Finds commands in modules published in a repository (PowerShell Gallery by default).

Syntax:

```powershell
Find-Command [[-Name] <string[]>] [-ModuleName <string>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-Tag <string[]>] [-Filter <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [<CommonParameters>]
```

Example:

```powershell
Find-Command -Name Get-ADUser -Repository PSGallery
```

### Find-DscResource

PowerShell 5.1 · module PowerShellGet · function

Finds DSC resources in modules published in a repository.

Syntax:

```powershell
Find-DscResource [[-Name] <string[]>] [-ModuleName <string>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-Tag <string[]>] [-Filter <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [<CommonParameters>]
```

Example:

```powershell
Find-DscResource -Name xFirewall
```

### Find-Module

PowerShell 5.1 · module PowerShellGet · function · aliases: fimo

Finds modules in a repository by name, tag or other criteria.

Syntax:

```powershell
Find-Module [[-Name] <string[]>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-IncludeDependencies] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [-Credential <pscredential>] [<CommonParameters>]
```

Example:

```powershell
Find-Module -Name Pester -AllVersions
```

### Find-RoleCapability

PowerShell 5.1 · module PowerShellGet · function

Finds JEA role capabilities in published modules.

Syntax:

```powershell
Find-RoleCapability [[-Name] <string[]>] [-ModuleName <string>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-Tag <string[]>] [-Filter <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [<CommonParameters>]
```

Example:

```powershell
Find-RoleCapability -Name HelpDesk
```

### Find-Script

PowerShell 5.1 · module PowerShellGet · function

Finds scripts in a repository.

Syntax:

```powershell
Find-Script [[-Name] <string[]>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-IncludeDependencies] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-Command <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [-Credential <pscredential>] [<CommonParameters>]
```

Example:

```powershell
Find-Script -Name Install-Git
```

### Get-InstalledModule

PowerShell 5.1 · module PowerShellGet · function

Lists modules installed by PowerShellGet (Install-Module).

Syntax:

```powershell
Get-InstalledModule [[-Name] <string[]>] [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-AllVersions] [<CommonParameters>]
```

Example:

```powershell
Get-InstalledModule
```

### Get-InstalledScript

PowerShell 5.1 · module PowerShellGet · function

Lists scripts installed by PowerShellGet (Install-Script).

Syntax:

```powershell
Get-InstalledScript [[-Name] <string[]>] [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [<CommonParameters>]
```

Example:

```powershell
Get-InstalledScript
```

### Get-PSRepository

PowerShell 5.1 · module PowerShellGet · function

Lists registered PowerShell repositories (PSGallery and others).

Syntax:

```powershell
Get-PSRepository [[-Name] <string[]>] [<CommonParameters>]
```

Example:

```powershell
Get-PSRepository
```

### Install-Module

PowerShell 5.1 · module PowerShellGet · function · aliases: inmo

Downloads and installs modules from a repository.

Syntax:

```powershell
Install-Module [-Name] <string[]> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Credential <pscredential>] [-Scope <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllowClobber] [-SkipPublisherCheck] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-Module [-InputObject] <psobject[]> [-Credential <pscredential>] [-Scope <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllowClobber] [-SkipPublisherCheck] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Install-Module -Name Pester -Scope CurrentUser -Force
```

### Install-Script

PowerShell 5.1 · module PowerShellGet · function

Downloads and installs scripts from a repository.

Syntax:

```powershell
Install-Script [-Name] <string[]> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Scope <string>] [-NoPathUpdate] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-Script [-InputObject] <psobject[]> [-Scope <string>] [-NoPathUpdate] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Install-Script -Name Install-Git -Scope CurrentUser
```

### New-ScriptFileInfo

PowerShell 5.1 · module PowerShellGet · function

Creates a script file with a metadata comment block (version, author, description) for publishing.

Syntax:

```powershell
New-ScriptFileInfo [[-Path] <string>] -Description <string> [-Version <version>] [-Author <string>] [-Guid <guid>] [-CompanyName <string>] [-Copyright <string>] [-RequiredModules <Object[]>] [-ExternalModuleDependencies <string[]>] [-RequiredScripts <string[]>] [-ExternalScriptDependencies <string[]>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-ScriptFileInfo -Path .\Deploy.ps1 -Version 1.0 -Author Ann -Description 'Deploys the app'
```

### Publish-Module

PowerShell 5.1 · module PowerShellGet · function · aliases: pumo

Publishes a module to a repository such as the PowerShell Gallery.

Syntax:

```powershell
Publish-Module -Name <string> [-RequiredVersion <version>] [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-FormatVersion <version>] [-ReleaseNotes <string[]>] [-Tags <string[]>] [-LicenseUri <uri>] [-IconUri <uri>] [-ProjectUri <uri>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Publish-Module -Path <string> [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-FormatVersion <version>] [-ReleaseNotes <string[]>] [-Tags <string[]>] [-LicenseUri <uri>] [-IconUri <uri>] [-ProjectUri <uri>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Publish-Module -Path .\MyTools -NuGetApiKey $key
```

### Publish-Script

PowerShell 5.1 · module PowerShellGet · function

Publishes a script to a repository.

Syntax:

```powershell
Publish-Script -Path <string> [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Publish-Script -LiteralPath <string> [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Publish-Script -Path .\Deploy.ps1 -NuGetApiKey $key
```

### Register-PSRepository

PowerShell 5.1 · module PowerShellGet · function

Registers a new PowerShell repository (for example an internal NuGet feed or file share).

Syntax:

```powershell
Register-PSRepository [-Name] <string> [-SourceLocation] <uri> [-PublishLocation <uri>] [-ScriptSourceLocation <uri>] [-ScriptPublishLocation <uri>] [-Credential <pscredential>] [-InstallationPolicy <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-PackageManagementProvider <string>] [<CommonParameters>]

Register-PSRepository -Default [-InstallationPolicy <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [<CommonParameters>]
```

Example:

```powershell
Register-PSRepository -Name Internal -SourceLocation \\Server\PSRepo -InstallationPolicy Trusted
```

### Save-Module

PowerShell 5.1 · module PowerShellGet · function

Downloads a module and its dependencies to a folder without installing.

Syntax:

```powershell
Save-Module [-Name] <string[]> -Path <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Module [-Name] <string[]> -LiteralPath <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Module [-InputObject] <psobject[]> -LiteralPath <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Module [-InputObject] <psobject[]> -Path <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Save-Module -Name Pester -Path .\Modules
```

### Save-Script

PowerShell 5.1 · module PowerShellGet · function

Downloads a script to a folder without installing.

Syntax:

```powershell
Save-Script [-Name] <string[]> -Path <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Script [-Name] <string[]> -LiteralPath <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Script [-InputObject] <psobject[]> -LiteralPath <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Script [-InputObject] <psobject[]> -Path <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Save-Script -Name Install-Git -Path .\Scripts
```

### Set-PSRepository

PowerShell 5.1 · module PowerShellGet · function

Changes a registered repository (location, installation policy).

Syntax:

```powershell
Set-PSRepository [-Name] <string> [[-SourceLocation] <uri>] [-PublishLocation <uri>] [-ScriptSourceLocation <uri>] [-ScriptPublishLocation <uri>] [-Credential <pscredential>] [-InstallationPolicy <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-PackageManagementProvider <string>] [<CommonParameters>]
```

Example:

```powershell
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
```

### Test-ScriptFileInfo

PowerShell 5.1 · module PowerShellGet · function

Validates a script's metadata comment block.

Syntax:

```powershell
Test-ScriptFileInfo [-Path] <string> [<CommonParameters>]

Test-ScriptFileInfo -LiteralPath <string> [<CommonParameters>]
```

Example:

```powershell
Test-ScriptFileInfo -Path .\Deploy.ps1
```

### Uninstall-Module

PowerShell 5.1 · module PowerShellGet · function

Uninstalls a module installed by PowerShellGet.

Syntax:

```powershell
Uninstall-Module [-Name] <string[]> [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-AllVersions] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Uninstall-Module [-InputObject] <psobject[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Uninstall-Module -Name Pester -AllVersions
```

### Uninstall-Script

PowerShell 5.1 · module PowerShellGet · function

Uninstalls a script installed by PowerShellGet.

Syntax:

```powershell
Uninstall-Script [-Name] <string[]> [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Uninstall-Script [-InputObject] <psobject[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Uninstall-Script -Name Install-Git
```

### Unregister-PSRepository

PowerShell 5.1 · module PowerShellGet · function

Removes a registered repository.

Syntax:

```powershell
Unregister-PSRepository [-Name] <string[]> [<CommonParameters>]
```

Example:

```powershell
Unregister-PSRepository -Name Internal
```

### Update-Module

PowerShell 5.1 · module PowerShellGet · function · aliases: upmo

Installs the newest version of modules installed with Install-Module.

Syntax:

```powershell
Update-Module [[-Name] <string[]>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-Module -Name Pester
```

### Update-ModuleManifest

PowerShell 5.1 · module PowerShellGet · function

Changes values in a module manifest (.psd1).

Syntax:

```powershell
Update-ModuleManifest [-Path] <string> [-NestedModules <Object[]>] [-Guid <guid>] [-Author <string>] [-CompanyName <string>] [-Copyright <string>] [-RootModule <string>] [-ModuleVersion <version>] [-Description <string>] [-ProcessorArchitecture <ProcessorArchitecture>] [-CompatiblePSEditions <string[]>] [-PowerShellVersion <version>] [-ClrVersion <version>] [-DotNetFrameworkVersion <version>] [-PowerShellHostName <string>] [-PowerShellHostVersion <version>] [-RequiredModules <Object[]>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-ScriptsToProcess <string[]>] [-RequiredAssemblies <string[]>] [-FileList <string[]>] [-ModuleList <Object[]>] [-FunctionsToExport <string[]>] [-AliasesToExport <string[]>] [-VariablesToExport <string[]>] [-CmdletsToExport <string[]>] [-DscResourcesToExport <string[]>] [-PrivateData <hashtable>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-HelpInfoUri <uri>] [-PassThru] [-DefaultCommandPrefix <string>] [-ExternalModuleDependencies <string[]>] [-PackageManagementProviders <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-ModuleManifest -Path .\MyTools.psd1 -ModuleVersion 1.1.0
```

### Update-Script

PowerShell 5.1 · module PowerShellGet · function

Installs the newest version of scripts installed with Install-Script.

Syntax:

```powershell
Update-Script [[-Name] <string[]>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-Script -Name Install-Git
```

### Update-ScriptFileInfo

PowerShell 5.1 · module PowerShellGet · function

Changes values in a script's metadata comment block.

Syntax:

```powershell
Update-ScriptFileInfo [-Path] <string> [-Version <version>] [-Author <string>] [-Guid <guid>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-RequiredModules <Object[]>] [-ExternalModuleDependencies <string[]>] [-RequiredScripts <string[]>] [-ExternalScriptDependencies <string[]>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-ScriptFileInfo [-LiteralPath] <string> [-Version <version>] [-Author <string>] [-Guid <guid>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-RequiredModules <Object[]>] [-ExternalModuleDependencies <string[]>] [-RequiredScripts <string[]>] [-ExternalScriptDependencies <string[]>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-ScriptFileInfo -Path .\Deploy.ps1 -Version 1.1
```

## PSDesiredStateConfiguration

### Configuration

PowerShell 5.1 · module PSDesiredStateConfiguration · function

DSC keyword (exposed as a function) that defines a Desired State Configuration; running it compiles .mof files.

Syntax:

```powershell
Configuration [[-ResourceModuleTuplesToImport] <List[Tuple[string[],ModuleSpecification[],version]]>] [[-OutputPath] <Object>] [[-Name] <Object>] [[-Body] <scriptblock>] [[-ArgsToBody] <hashtable>] [[-ConfigurationData] <hashtable>] [[-InstanceName] <string>] [<CommonParameters>]
```

Example:

```powershell
Configuration Web { Node localhost { WindowsFeature IIS { Name = 'Web-Server'; Ensure = 'Present' } } }; Web -OutputPath .\Web
```

### Disable-DscDebug

PowerShell 5.1 · module PSDesiredStateConfiguration · function

Stops debugging of DSC resources.

Syntax:

```powershell
Disable-DscDebug [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Disable-DscDebug
```

### Enable-DscDebug

PowerShell 5.1 · module PSDesiredStateConfiguration · function

Starts debugging of DSC resources (breaks into resources as they run).

Syntax:

```powershell
Enable-DscDebug [-BreakAll] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-DscDebug -BreakAll
```

### Get-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · function · aliases: gcfg

Gets the current state of the configuration applied to the node.

Syntax:

```powershell
Get-DscConfiguration [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]
```

Example:

```powershell
Get-DscConfiguration
```

### Get-DscConfigurationStatus

PowerShell 5.1 · module PSDesiredStateConfiguration · function · aliases: gcfgs

Shows the status of recent configuration runs (success, failure, resources).

Syntax:

```powershell
Get-DscConfigurationStatus [-All] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]
```

Example:

```powershell
Get-DscConfigurationStatus -All
```

### Get-DscLocalConfigurationManager

PowerShell 5.1 · module PSDesiredStateConfiguration · function · aliases: glcm

Shows the Local Configuration Manager (LCM) settings of the node.

Syntax:

```powershell
Get-DscLocalConfigurationManager [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]
```

Example:

```powershell
Get-DscLocalConfigurationManager
```

### Get-DscResource

PowerShell 5.1 · module PSDesiredStateConfiguration · function

Lists DSC resources installed on the computer and their properties.

Syntax:

```powershell
Get-DscResource [[-Name] <string[]>] [[-Module] <Object>] [-Syntax] [<CommonParameters>]
```

Example:

```powershell
Get-DscResource -Name File -Syntax
```

### Invoke-DscResource

PowerShell 5.1 · module PSDesiredStateConfiguration · cmdlet

Runs one method (Get, Set or Test) of a DSC resource directly.

Syntax:

```powershell
Invoke-DscResource [-Name] <string> [-Method] <string> -ModuleName <ModuleSpecification> -Property <hashtable> [<CommonParameters>]
```

Example:

```powershell
Invoke-DscResource -Name File -Method Test -ModuleName PSDesiredStateConfiguration -Property @{ DestinationPath = 'C:\x.txt'; Contents = 'hi' }
```

### New-DscChecksum

PowerShell 5.1 · module PSDesiredStateConfiguration · function

Creates checksum files for DSC configuration documents and resources (for pull servers).

Syntax:

```powershell
New-DscChecksum [-Path] <string[]> [[-OutPath] <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
New-DscChecksum -Path .\Web -Force
```

### Publish-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · cmdlet · aliases: pbcfg

Sends a configuration to nodes without applying it yet.

Syntax:

```powershell
Publish-DscConfiguration [-Path] <string> [[-ComputerName] <string[]>] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Publish-DscConfiguration [-Path] <string> -CimSession <CimSession[]> [-Force] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Publish-DscConfiguration -Path .\Web
```

### Remove-DscConfigurationDocument

PowerShell 5.1 · module PSDesiredStateConfiguration · function

Removes a configuration document (Current, Pending or Previous) from the node.

Syntax:

```powershell
Remove-DscConfigurationDocument -Stage <Stage> [-Force] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Remove-DscConfigurationDocument -Stage Pending
```

### Restore-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · function · aliases: rtcfg

Rolls the node back to its previous configuration.

Syntax:

```powershell
Restore-DscConfiguration [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Restore-DscConfiguration
```

### Set-DscLocalConfigurationManager

PowerShell 5.1 · module PSDesiredStateConfiguration · cmdlet · aliases: slcm

Applies LCM settings (meta-configuration) to nodes.

Syntax:

```powershell
Set-DscLocalConfigurationManager [-Path] <string> [[-ComputerName] <string[]>] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-DscLocalConfigurationManager [-Path] <string> -CimSession <CimSession[]> [-Force] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Set-DscLocalConfigurationManager -Path .\LcmConfig
```

### Start-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · cmdlet · aliases: sacfg

Applies configurations (.mof files) to nodes.

Syntax:

```powershell
Start-DscConfiguration [[-Path] <string>] [[-ComputerName] <string[]>] [-Wait] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-DscConfiguration [[-Path] <string>] -CimSession <CimSession[]> [-Wait] [-Force] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-DscConfiguration [[-ComputerName] <string[]>] -UseExisting [-Wait] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-DscConfiguration -CimSession <CimSession[]> -UseExisting [-Wait] [-Force] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Start-DscConfiguration -Path .\Web -Wait -Verbose
```

### Stop-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · function

Stops a configuration that is currently running.

Syntax:

```powershell
Stop-DscConfiguration [-Force] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Stop-DscConfiguration -Force
```

### Test-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · cmdlet · aliases: tcfg

Tests whether the node matches its configuration (True/False or detailed).

Syntax:

```powershell
Test-DscConfiguration [[-ComputerName] <string[]>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-AsJob] [-Detailed] [<CommonParameters>]

Test-DscConfiguration [-Path] <string> [[-ComputerName] <string[]>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration [[-ComputerName] <string[]>] -ReferenceConfiguration <string> [-Credential <pscredential>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration [-Path] <string> -CimSession <CimSession[]> [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration -CimSession <CimSession[]> -ReferenceConfiguration <string> [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration -CimSession <CimSession[]> [-ThrottleLimit <int>] [-AsJob] [-Detailed] [<CommonParameters>]
```

Example:

```powershell
Test-DscConfiguration -Detailed
```

### Update-DscConfiguration

PowerShell 5.1 · module PSDesiredStateConfiguration · cmdlet · aliases: upcfg

Makes the node check its pull server for configuration changes now.

Syntax:

```powershell
Update-DscConfiguration [[-ComputerName] <string[]>] [-Wait] [-JobName <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-DscConfiguration -CimSession <CimSession[]> [-Wait] [-JobName <string>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Update-DscConfiguration -Wait
```

## PSDiagnostics

### Disable-PSTrace

PowerShell 5.1 · module PSDiagnostics · function

Turns off the Microsoft-Windows-PowerShell event provider's analytic and debug logs.

Syntax:

```powershell
Disable-PSTrace [-AnalyticOnly]
```

Example:

```powershell
Disable-PSTrace
```

### Disable-PSWSManCombinedTrace

PowerShell 5.1 · module PSDiagnostics · function

Stops the combined PowerShell and WSMan trace session.

Syntax:

```powershell
Disable-PSWSManCombinedTrace
```

Example:

```powershell
Disable-PSWSManCombinedTrace
```

### Disable-WSManTrace

PowerShell 5.1 · module PSDiagnostics · function

Stops the WSMan trace session.

Syntax:

```powershell
Disable-WSManTrace
```

Example:

```powershell
Disable-WSManTrace
```

### Enable-PSTrace

PowerShell 5.1 · module PSDiagnostics · function

Turns on the Microsoft-Windows-PowerShell analytic and debug event logs.

Syntax:

```powershell
Enable-PSTrace [-Force] [-AnalyticOnly]
```

Example:

```powershell
Enable-PSTrace -Force
```

### Enable-PSWSManCombinedTrace

PowerShell 5.1 · module PSDiagnostics · function

Starts a trace session with both PowerShell and WSMan providers.

Syntax:

```powershell
Enable-PSWSManCombinedTrace [-DoNotOverwriteExistingTrace]
```

Example:

```powershell
Enable-PSWSManCombinedTrace
```

### Enable-WSManTrace

PowerShell 5.1 · module PSDiagnostics · function

Starts a trace session for the WSMan providers.

Syntax:

```powershell
Enable-WSManTrace
```

Example:

```powershell
Enable-WSManTrace
```

### Get-LogProperties

PowerShell 5.1 · module PSDiagnostics · function

Gets the properties of a Windows event log (enabled, size, retention).

Syntax:

```powershell
Get-LogProperties [-Name] <Object> [<CommonParameters>]
```

Example:

```powershell
Get-LogProperties -Name 'Microsoft-Windows-PowerShell/Operational'
```

### Set-LogProperties

PowerShell 5.1 · module PSDiagnostics · function

Changes the properties of a Windows event log.

Syntax:

```powershell
Set-LogProperties [-LogDetails] <LogDetails> [-Force] [<CommonParameters>]
```

Example:

```powershell
$p = Get-LogProperties 'Windows PowerShell'; $p.MaxLogSize = 20MB; Set-LogProperties -LogDetails $p
```

### Start-Trace

PowerShell 5.1 · module PSDiagnostics · function

Starts an Event Tracing for Windows (ETW) session.

Syntax:

```powershell
Start-Trace [-SessionName] <string> [[-OutputFilePath] <string>] [[-ProviderFilePath] <string>] [-ETS] [-Format <Object>] [-MinBuffers <int>] [-MaxBuffers <int>] [-BufferSizeInKB <int>] [-MaxLogFileSizeInMB <int>] [<CommonParameters>]
```

Example:

```powershell
Start-Trace -SessionName MyTrace -OutputFilePath .\trace.etl -ProviderFilePath .\providers.txt
```

### Stop-Trace

PowerShell 5.1 · module PSDiagnostics · function

Stops an ETW session.

Syntax:

```powershell
Stop-Trace [-SessionName] <Object> [-ETS] [<CommonParameters>]
```

Example:

```powershell
Stop-Trace -SessionName MyTrace
```

## PSReadLine

### Get-PSReadLineKeyHandler

PowerShell 5.1 · module PSReadLine · cmdlet

Lists the key bindings of PSReadLine (the command-line editor).

Syntax:

```powershell
Get-PSReadLineKeyHandler [-Bound] [-Unbound] [<CommonParameters>]
```

Example:

```powershell
Get-PSReadLineKeyHandler -Bound
```

### Get-PSReadLineOption

PowerShell 5.1 · module PSReadLine · cmdlet

Shows the current PSReadLine options (edit mode, colors, history settings).

Syntax:

```powershell
Get-PSReadLineOption [<CommonParameters>]
```

Example:

```powershell
Get-PSReadLineOption
```

### PSConsoleHostReadLine

PowerShell 5.1 · module PSReadLine · function

The function the console calls to read each command line through PSReadLine; not meant to be called directly.

Syntax:

```powershell
PSConsoleHostReadLine
```

Example:

```powershell
PSConsoleHostReadLine
```

### Remove-PSReadLineKeyHandler

PowerShell 5.1 · module PSReadLine · cmdlet

Removes a PSReadLine key binding.

Syntax:

```powershell
Remove-PSReadLineKeyHandler [-Chord] <string[]> [-ViMode <ViMode>] [<CommonParameters>]
```

Example:

```powershell
Remove-PSReadLineKeyHandler -Chord Ctrl+d
```

### Set-PSReadLineKeyHandler

PowerShell 5.1 · module PSReadLine · cmdlet

Binds a key or chord to a PSReadLine function or your own script block.

Syntax:

```powershell
Set-PSReadLineKeyHandler [-Chord] <string[]> [-ScriptBlock] <scriptblock> [-BriefDescription <string>] [-Description <string>] [-ViMode <ViMode>] [<CommonParameters>]

Set-PSReadLineKeyHandler [-Chord] <string[]> [-Function] <string> [-ViMode <ViMode>] [<CommonParameters>]
```

Example:

```powershell
Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete
```

### Set-PSReadLineOption

PowerShell 5.1 · module PSReadLine · cmdlet

Changes PSReadLine behavior: edit mode, colors, history, prediction.

Syntax:

```powershell
Set-PSReadLineOption [-EditMode <EditMode>] [-ContinuationPrompt <string>] [-HistoryNoDuplicates] [-AddToHistoryHandler <Func[string,Object]>] [-CommandValidationHandler <Action[CommandAst]>] [-HistorySearchCursorMovesToEnd] [-MaximumHistoryCount <int>] [-MaximumKillRingCount <int>] [-ShowToolTips] [-ExtraPromptLineCount <int>] [-DingTone <int>] [-DingDuration <int>] [-BellStyle <BellStyle>] [-CompletionQueryItems <int>] [-WordDelimiters <string>] [-HistorySearchCaseSensitive] [-HistorySaveStyle <HistorySaveStyle>] [-HistorySavePath <string>] [-AnsiEscapeTimeout <int>] [-PromptText <string[]>] [-ViModeIndicator <ViModeStyle>] [-ViModeChangeHandler <scriptblock>] [-Colors <hashtable>] [<CommonParameters>]
```

Example:

```powershell
Set-PSReadLineOption -EditMode Emacs -HistoryNoDuplicates
```

## PSScheduledJob

### Add-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Adds triggers (when to run) to scheduled jobs.

Syntax:

```powershell
Add-JobTrigger [-InputObject] <ScheduledJobDefinition[]> [-Trigger] <ScheduledJobTrigger[]> [<CommonParameters>]

Add-JobTrigger [-Id] <int[]> [-Trigger] <ScheduledJobTrigger[]> [<CommonParameters>]

Add-JobTrigger [-Name] <string[]> [-Trigger] <ScheduledJobTrigger[]> [<CommonParameters>]
```

Example:

```powershell
Add-JobTrigger -Name Backup -Trigger (New-JobTrigger -Daily -At 3am)
```

### Disable-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Disables triggers of scheduled jobs without deleting them.

Syntax:

```powershell
Disable-JobTrigger [-InputObject] <ScheduledJobTrigger[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-JobTrigger -Name Backup | Disable-JobTrigger
```

### Disable-ScheduledJob

PowerShell 5.1 · module PSScheduledJob · cmdlet

Disables a scheduled job so its triggers no longer start it.

Syntax:

```powershell
Disable-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-ScheduledJob [-Id] <int> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-ScheduledJob [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Disable-ScheduledJob -Name Backup
```

### Enable-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Re-enables triggers of scheduled jobs.

Syntax:

```powershell
Enable-JobTrigger [-InputObject] <ScheduledJobTrigger[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Get-JobTrigger -Name Backup | Enable-JobTrigger
```

### Enable-ScheduledJob

PowerShell 5.1 · module PSScheduledJob · cmdlet

Re-enables a disabled scheduled job.

Syntax:

```powershell
Enable-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-ScheduledJob [-Id] <int> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-ScheduledJob [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Enable-ScheduledJob -Name Backup
```

### Get-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Gets the triggers of scheduled jobs.

Syntax:

```powershell
Get-JobTrigger [-InputObject] <ScheduledJobDefinition> [[-TriggerId] <int[]>] [<CommonParameters>]

Get-JobTrigger [-Id] <int> [[-TriggerId] <int[]>] [<CommonParameters>]

Get-JobTrigger [-Name] <string> [[-TriggerId] <int[]>] [<CommonParameters>]
```

Example:

```powershell
Get-JobTrigger -Name Backup
```

### Get-ScheduledJob

PowerShell 5.1 · module PSScheduledJob · cmdlet

Lists scheduled jobs on the local computer.

Syntax:

```powershell
Get-ScheduledJob [[-Id] <int[]>] [<CommonParameters>]

Get-ScheduledJob [-Name] <string[]> [<CommonParameters>]
```

Example:

```powershell
Get-ScheduledJob
```

### Get-ScheduledJobOption

PowerShell 5.1 · module PSScheduledJob · cmdlet

Gets the options (conditions, idle settings) of scheduled jobs.

Syntax:

```powershell
Get-ScheduledJobOption [-InputObject] <ScheduledJobDefinition> [<CommonParameters>]

Get-ScheduledJobOption [-Id] <int> [<CommonParameters>]

Get-ScheduledJobOption [-Name] <string> [<CommonParameters>]
```

Example:

```powershell
Get-ScheduledJobOption -Name Backup
```

### New-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Creates a trigger (once, daily, weekly, at startup, at logon) for a scheduled job.

Syntax:

```powershell
New-JobTrigger -Once -At <datetime> [-RandomDelay <timespan>] [-RepetitionInterval <timespan>] [-RepetitionDuration <timespan>] [-RepeatIndefinitely] [<CommonParameters>]

New-JobTrigger -Daily -At <datetime> [-DaysInterval <int>] [-RandomDelay <timespan>] [<CommonParameters>]

New-JobTrigger -Weekly -At <datetime> -DaysOfWeek <DayOfWeek[]> [-WeeksInterval <int>] [-RandomDelay <timespan>] [<CommonParameters>]

New-JobTrigger -AtStartup [-RandomDelay <timespan>] [<CommonParameters>]

New-JobTrigger -AtLogOn [-RandomDelay <timespan>] [-User <string>] [<CommonParameters>]
```

Example:

```powershell
New-JobTrigger -Weekly -DaysOfWeek Monday -At '9:00 AM'
```

### New-ScheduledJobOption

PowerShell 5.1 · module PSScheduledJob · cmdlet

Creates options for a scheduled job (run elevated, wake to run, network conditions).

Syntax:

```powershell
New-ScheduledJobOption [-RunElevated] [-HideInTaskScheduler] [-RestartOnIdleResume] [-MultipleInstancePolicy <TaskMultipleInstancePolicy>] [-DoNotAllowDemandStart] [-RequireNetwork] [-StopIfGoingOffIdle] [-WakeToRun] [-ContinueIfGoingOnBattery] [-StartIfOnBattery] [-IdleTimeout <timespan>] [-IdleDuration <timespan>] [-StartIfIdle] [<CommonParameters>]
```

Example:

```powershell
New-ScheduledJobOption -RunElevated -WakeToRun
```

### Register-ScheduledJob

PowerShell 5.1 · module PSScheduledJob · cmdlet

Creates a scheduled job: a script block or script run by Task Scheduler on triggers.

Syntax:

```powershell
Register-ScheduledJob [-Name] <string> [-ScriptBlock] <scriptblock> [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-ArgumentList <Object[]>] [-MaxResultCount <int>] [-RunNow] [-RunEvery <timespan>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-ScheduledJob [-Name] <string> [-FilePath] <string> [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-ArgumentList <Object[]>] [-MaxResultCount <int>] [-RunNow] [-RunEvery <timespan>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Register-ScheduledJob -Name Backup -ScriptBlock { Copy-Item C:\Data D:\Backup -Recurse } -Trigger (New-JobTrigger -Daily -At 2am)
```

### Remove-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Deletes triggers from scheduled jobs.

Syntax:

```powershell
Remove-JobTrigger [-InputObject] <ScheduledJobDefinition[]> [-TriggerId <int[]>] [<CommonParameters>]

Remove-JobTrigger [-Id] <int[]> [-TriggerId <int[]>] [<CommonParameters>]

Remove-JobTrigger [-Name] <string[]> [-TriggerId <int[]>] [<CommonParameters>]
```

Example:

```powershell
Remove-JobTrigger -Name Backup -TriggerId 1
```

### Set-JobTrigger

PowerShell 5.1 · module PSScheduledJob · cmdlet

Changes a scheduled job's trigger.

Syntax:

```powershell
Set-JobTrigger [-InputObject] <ScheduledJobTrigger[]> [-DaysInterval <int>] [-WeeksInterval <int>] [-RandomDelay <timespan>] [-At <datetime>] [-User <string>] [-DaysOfWeek <DayOfWeek[]>] [-AtStartup] [-AtLogOn] [-Once] [-RepetitionInterval <timespan>] [-RepetitionDuration <timespan>] [-RepeatIndefinitely] [-Daily] [-Weekly] [-PassThru] [<CommonParameters>]
```

Example:

```powershell
Get-JobTrigger -Name Backup | Set-JobTrigger -At 4am
```

### Set-ScheduledJob

PowerShell 5.1 · module PSScheduledJob · cmdlet

Changes a scheduled job (script, triggers, credentials, options).

Syntax:

```powershell
Set-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-Name <string>] [-ScriptBlock <scriptblock>] [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-MaxResultCount <int>] [-PassThru] [-ArgumentList <Object[]>] [-RunNow] [-RunEvery <timespan>] [<CommonParameters>]

Set-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-Name <string>] [-FilePath <string>] [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-MaxResultCount <int>] [-PassThru] [-ArgumentList <Object[]>] [-RunNow] [-RunEvery <timespan>] [<CommonParameters>]

Set-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-ClearExecutionHistory] [-PassThru] [<CommonParameters>]
```

Example:

```powershell
Set-ScheduledJob -Name Backup -MaxResultCount 20
```

### Set-ScheduledJobOption

PowerShell 5.1 · module PSScheduledJob · cmdlet

Changes the options of a scheduled job.

Syntax:

```powershell
Set-ScheduledJobOption [-InputObject] <ScheduledJobOptions> [-PassThru] [-RunElevated] [-HideInTaskScheduler] [-RestartOnIdleResume] [-MultipleInstancePolicy <TaskMultipleInstancePolicy>] [-DoNotAllowDemandStart] [-RequireNetwork] [-StopIfGoingOffIdle] [-WakeToRun] [-ContinueIfGoingOnBattery] [-StartIfOnBattery] [-IdleTimeout <timespan>] [-IdleDuration <timespan>] [-StartIfIdle] [<CommonParameters>]
```

Example:

```powershell
Get-ScheduledJobOption -Name Backup | Set-ScheduledJobOption -RequireNetwork
```

### Unregister-ScheduledJob

PowerShell 5.1 · module PSScheduledJob · cmdlet

Deletes scheduled jobs and their results.

Syntax:

```powershell
Unregister-ScheduledJob [-InputObject] <ScheduledJobDefinition[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-ScheduledJob [-Id] <int[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-ScheduledJob [-Name] <string[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

Example:

```powershell
Unregister-ScheduledJob -Name Backup
```

## PSWorkflow

### New-PSWorkflowExecutionOption

PowerShell 5.1 · module PSWorkflow · cmdlet

Creates options for a workflow session configuration (persistence, limits).

Syntax:

```powershell
New-PSWorkflowExecutionOption [-PersistencePath <string>] [-MaxPersistenceStoreSizeGB <long>] [-PersistWithEncryption] [-MaxRunningWorkflows <int>] [-AllowedActivity <string[]>] [-OutOfProcessActivity <string[]>] [-EnableValidation] [-MaxDisconnectedSessions <int>] [-MaxConnectedSessions <int>] [-MaxSessionsPerWorkflow <int>] [-MaxSessionsPerRemoteNode <int>] [-MaxActivityProcesses <int>] [-ActivityProcessIdleTimeoutSec <int>] [-RemoteNodeSessionIdleTimeoutSec <int>] [-SessionThrottleLimit <int>] [-WorkflowShutdownTimeoutMSec <int>] [<CommonParameters>]
```

Example:

```powershell
New-PSWorkflowExecutionOption -MaxSessionsPerWorkflow 10
```

### New-PSWorkflowSession

PowerShell 5.1 · module PSWorkflow · function · aliases: nwsn

Creates a PSSession that uses the workflow session configuration. Alias: nwsn.

Syntax:

```powershell
New-PSWorkflowSession [[-ComputerName] <string[]>] [-Credential <Object>] [-Name <string[]>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-ThrottleLimit <int>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-EnableNetworkAccess] [<CommonParameters>]
```

Example:

```powershell
$ws = New-PSWorkflowSession -ComputerName Server01
```

## PSWorkflowUtility

### Invoke-AsWorkflow

PowerShell 5.1 · module PSWorkflowUtility · workflow

Runs a command or expression as a Windows PowerShell workflow.

Syntax:

```powershell
Invoke-AsWorkflow [-CommandName <string>] [-Parameter <hashtable>] [<WorkflowCommonParameters>] [<CommonParameters>]

Invoke-AsWorkflow [-Expression <string>] [<WorkflowCommonParameters>] [<CommonParameters>]
```

Example:

```powershell
Invoke-AsWorkflow -Expression 'Get-Process' -PSComputerName Server01
```

## ISE

### Get-IseSnippet

PowerShell 5.1 · module ISE · function

(PowerShell ISE only) Lists the code snippets you created in ISE.

Syntax:

```powershell
Get-IseSnippet [<CommonParameters>]
```

Example:

```powershell
Get-IseSnippet
```

### Import-IseSnippet

PowerShell 5.1 · module ISE · function

(PowerShell ISE only) Loads snippets from a folder or module into the ISE session.

Syntax:

```powershell
Import-IseSnippet [-Path] <String> [-Recurse] [<CommonParameters>]

Import-IseSnippet [-Recurse] -Module <String> [-ListAvailable] [<CommonParameters>]
```

Example:

```powershell
Import-IseSnippet -Path \\Server\Share\Snippets -Recurse
```

### New-IseSnippet

PowerShell 5.1 · module ISE · function

(PowerShell ISE only) Creates a reusable code snippet for ISE.

Syntax:

```powershell
New-IseSnippet [-Title] <String> [-Description] <String> [-Text] <String> [-Author <String>] [-CaretOffset <Int32>] [-Force] [<CommonParameters>]
```

Example:

```powershell
New-IseSnippet -Title Try-Catch -Description 'Try/catch block' -Text 'try { } catch { }'
```

