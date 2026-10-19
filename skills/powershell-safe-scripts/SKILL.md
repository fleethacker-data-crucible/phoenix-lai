---
name: powershell-safe-scripts
description: This skill generates PowerShell 5.1 scripts that adhere to strict security and best practices for locked-down environments, ensuring no external modules are used, enforcing strict mode, implementing robust error handling, and including comment-based help blocks. Use when you need to write a reliable, self-contained PowerShell script for a restricted Windows server environment.
---

# Safe PowerShell 5.1 Script Generator

This skill ensures all generated PowerShell 5.1 code is safe, adheres to strict standards, and is fully documented for execution in locked-down environments.

## When to use

Use this skill when you are writing a PowerShell script intended to run on a Windows server where network access or installation of external modules is prohibited.

## Steps

1.  **Determine Script Purpose:** Ask the user for the exact functionality the script must perform (e.g., "Check service status," "Process a file").
2.  **Apply Strict Mode:** Ensure `Set-StrictMode -Version Latest` is placed at the beginning of the script scope.
3.  **Implement Error Handling:** Wrap critical operations in `try/catch` blocks, ensuring `$ErrorActionPreference = 'Stop'` is set beforehand to catch non-terminating errors.
4.  **Add Help Block:** Include a comprehensive comment-based help block (`<# ... #>`) detailing the script's purpose, parameters, and usage.
5.  **Generate Code:** Write the core logic using only built-in PowerShell 5.1 cmdlets, strictly adhering to syntax rules (e.g., no aliases, proper comparison operators).
6.  **Review Against Constraints:** Verify the generated code meets all rules listed in the "Rules" section below.

## Rules

*   **No External Dependencies:** Never use cmdlets or modules that require installation from the internet (e.g., `Install-Module`). Only use core PowerShell 5.1 cmdlets.
*   **Strict Mode Enforcement:** Always include `Set-StrictMode -Version Latest` near the start of the script execution scope.
*   **Error Handling:** Before any `try/catch`, set `$ErrorActionPreference = 'Stop'`. Every major block of code should be wrapped in `try { ... } catch { ... }`.
*   **Syntax Compliance (PS 5.1):**
    *   Use full cmdlet names (e.g., `Get-Service`, never `gsv`).
    *   Use proper comparison operators (`-eq`, `-ne`, etc.).
    *   Do not use PowerShell 7+ syntax like `?.`, `&&`, or `ForEach-Object -Parallel`.
    *   When dealing with paths, use quotes correctly; if a path contains special characters that might be interpreted by the shell, consider using the call operator (`&`) if executing an external program.
*   **Documentation:** Every script must begin with a detailed comment-based help block describing its function, parameters (if any), and examples.

## Output format

The output must be a single, complete, runnable PowerShell script file content, ready to be saved as a `.ps1` file.

## Example

**Input:** "Write a script that checks if the 'Spooler' service is running and reports its status."

**Expected Output:** (A full PowerShell script block)

```powershell
<#
.SYNOPSIS
    Checks the operational status of the Windows Print Spooler service.

.DESCRIPTION
    This script queries the local system for the 'Spooler' service and outputs
    its current state, ensuring strict error handling is in place.

.EXAMPLE
    .\Check-SpoolerStatus.ps1
#>
$ErrorActionPreference = 'Stop'

try {
    # Set strict mode to catch uninitialized variables or bad calls
    Set-StrictMode -Version Latest

    $ServiceName = "Spooler"
    Write-Host "Checking status of service: $($ServiceName)..."

    $Service = Get-Service -Name $ServiceName -ErrorAction Stop

    if ($Service.Status -eq 'Running') {
        Write-Host "SUCCESS: The '$ServiceName' service is currently running." -ForegroundColor Green
    } else {
        Write-Host "WARNING: The '$ServiceName' service is not running. Current status: $($Service.Status)" -ForegroundColor Yellow
    }

} catch {
    Write-Error "An error occurred while trying to check the service status."
    Write-Error "Details: $($_.Exception.Message)"
}
```

## Checklist

- [ ] Is `$ErrorActionPreference = 'Stop'` set before `try/catch`?
- [ ] Is `Set-StrictMode -Version Latest` called?
- [ ] Does the script contain a complete comment-based help block (`<# ... #>`)?
- [ ] Are all cmdlets used native to PowerShell 5.1 and avoiding aliases?
- [ ] Is error handling implemented for critical operations?
