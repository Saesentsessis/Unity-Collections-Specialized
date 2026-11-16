#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Automated version bumping script for Unity Package project

.DESCRIPTION
    Updates version numbers across all project files automatically to prevent human errors.
    Supports preview mode for safe testing.

.PARAMETER NewVersion
    The new version number in semver format (e.g., "0.18.0")

.PARAMETER WhatIf
    Preview changes without applying them

.EXAMPLE
    .\bump-version.ps1 -NewVersion "0.18.0"

.EXAMPLE
    .\bump-version.ps1 -NewVersion "0.18.0" -WhatIf
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$NewVersion,

    [switch]$WhatIf
)

# Set location to repository root (parent of commands folder)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
Push-Location $repoRoot

# Script configuration
$ErrorActionPreference = "Stop"

# Version file locations (relative to script root)
$VersionFiles = @(
    @{
        Path        = "Unity-Collections-Specialized/Assets/root/package.json"
        Pattern     = '"version":\s*"[\d\.]+"'
        Replace     = '"version": "{VERSION}"'
        Description = "Unity package version"
    },
    @{
        Path        = "Installer/Assets/Unity Collections Specialized Installer/Installer.cs"
        Pattern     = 'public const string Version = "[\d\.]+";'
        Replace     = 'public const string Version = "{VERSION}";'
        Description = "Installer C# version constant"
    },
    @{
        Path        = "Unity-Collections-Specialized/Assets/root/README.md"
        Pattern     = "https://github\.com/Saesentsessis/Unity-Collections-Specialized/releases/download/[\d\.]+/Unity-Collections-Specialized-Installer\.unitypackage"
        Replace     = "https://github.com/Saesentsessis/Unity-Collections-Specialized/releases/download/{VERSION}/Unity-Collections-Specialized-Installer.unitypackage"
        Description = "Package README download URL"
    },
    @{
        Path        = "README.md"
        Pattern     = "https://github\.com/Saesentsessis/Unity-Collections-Specialized/releases/download/[\d\.]+/Unity-Collections-Specialized-Installer\.unitypackage"
        Replace     = "https://github.com/Saesentsessis/Unity-Collections-Specialized/releases/download/{VERSION}/Unity-Collections-Specialized-Installer.unitypackage"
        Description = "Repository README download URL"
    },
    @{
        Path        = "Unity-Collections-Specialized/Assets/root/README.md"
        Pattern     = '"com\.saesentsessis\.unity-collections-specialized":\s*"[\d\.]+"'
        Replace     = '"com.saesentsessis.unity-collections-specialized": "{VERSION}"'
        Description = "Package README manifest.json example"
    },
    @{
        Path        = "README.md"
        Pattern     = '"com\.saesentsessis\.unity-collections-specialized":\s*"[\d\.]+"'
        Replace     = '"com.saesentsessis.unity-collections-specialized": "{VERSION}"'
        Description = "Repository README manifest.json example"
    },
    @{
        Path        = "Unity-Collections-Specialized/Assets/root/README.md"
        Pattern     = "Unity-Collections-Specialized\.git\?path=Unity-Collections-Specialized/Assets/root#[\d\.]+"
        Replace     = "Unity-Collections-Specialized.git?path=Unity-Collections-Specialized/Assets/root#{VERSION}"
        Description = "Package README git URL tag"
    },
    @{
        Path        = "README.md"
        Pattern     = "Unity-Collections-Specialized\.git\?path=Unity-Collections-Specialized/Assets/root#[\d\.]+"
        Replace     = "Unity-Collections-Specialized.git?path=Unity-Collections-Specialized/Assets/root#{VERSION}"
        Description = "Repository README git URL tag"
    }
)

function Write-ColorText {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
}

function Get-TotalReplacements {
    param([object[]]$Changes)

    # Sum explicitly: 'Measure-Object -Property' cannot read hashtable keys in Windows PowerShell 5.1.
    $total = 0
    foreach ($change in $Changes) {
        $total += $change.Matches
    }
    return $total
}

function Get-ChangedFileCount {
    param([object[]]$Changes)

    return @($Changes | ForEach-Object { $_.Path } | Select-Object -Unique).Count
}

function Test-SemanticVersion {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $false
    }

    # Basic semver pattern: major.minor.patch (with optional prerelease/build)
    $pattern = '^\d+\.\d+\.\d+(-[a-zA-Z0-9\-\.]+)?(\+[a-zA-Z0-9\-\.]+)?$'
    return $Version -match $pattern
}

function Get-CurrentVersion {
    # Extract current version from package.json
    $packageJsonPath = "Unity-Collections-Specialized/Assets/root/package.json"
    if (-not (Test-Path $packageJsonPath)) {
        throw "Could not find package.json at: $packageJsonPath"
    }

    $content = Get-Content $packageJsonPath -Raw
    if ($content -match '"version":\s*"([\d\.]+)"') {
        return $Matches[1]
    }

    throw "Could not extract current version from package.json"
}

function Update-VersionFiles {
    param([string]$OldVersion, [string]$NewVersion, [bool]$PreviewOnly = $false)

    $changes = @()

    # Working copy per file, so several patterns on the same file accumulate instead of overwriting each other.
    $pendingContent = [ordered]@{}

    foreach ($file in $VersionFiles) {
        $fullPath = $file.Path

        if (-not (Test-Path $fullPath)) {
            Write-ColorText "??  File not found: $($file.Path)" "Yellow"
            continue
        }

        if (-not $pendingContent.Contains($fullPath)) {
            $pendingContent[$fullPath] = Get-Content $fullPath -Raw
        }

        $originalContent = $pendingContent[$fullPath]

        # Create the replacement string
        $replacement = $file.Replace -replace '\{VERSION\}', $NewVersion

        # Apply the replacement
        $newContent = $originalContent -replace $file.Pattern, $replacement

        # Check if any changes were made
        if ($originalContent -ne $newContent) {
            # Count matches for reporting
            $regexMatches = [regex]::Matches($originalContent, $file.Pattern)

            $pendingContent[$fullPath] = $newContent

            $changes += @{
                Path        = $file.Path
                Description = $file.Description
                Matches     = $regexMatches.Count
            }

            Write-ColorText "?? $($file.Description): $($regexMatches.Count) occurrence(s)" "Green"

            # Show the actual changes
            foreach ($match in $regexMatches) {
                $newValue = $match.Value -replace $file.Pattern, $replacement
                Write-ColorText "   $($match.Value) > $newValue" "Gray"
            }
        }
        else {
            Write-ColorText "??  No matches found in: $($file.Path)" "Yellow"
            Write-ColorText "   Pattern: $($file.Pattern)" "Gray"
        }
    }

    if ($changes.Count -eq 0) {
        Write-ColorText "? No version references found to update!" "Red"
        Pop-Location
        exit 1
    }

    if ($PreviewOnly) {
        Write-ColorText "`n?? Preview Summary:" "Cyan"
        Write-ColorText "Files to be modified: $(Get-ChangedFileCount $changes)" "White"
        Write-ColorText "Total replacements: $(Get-TotalReplacements $changes)" "White"
        return $null
    }

    # Apply changes: write each changed file once, with all of its replacements.
    $changedPaths = @($changes | ForEach-Object { $_.Path } | Select-Object -Unique)
    foreach ($fullPath in $changedPaths) {
        Set-Content -Path $fullPath -Value $pendingContent[$fullPath] -NoNewline
    }

    return $changes
}

# Main execution
try {
    Write-ColorText "?? Package Version Bump Script" "Cyan"
    Write-ColorText "=================================" "Cyan"

    # Validate semantic version format
    if (-not (Test-SemanticVersion $NewVersion)) {
        Write-ColorText "? Invalid semantic version format: $NewVersion" "Red"
        Write-ColorText "Expected format: major.minor.patch (e.g., '1.2.3')" "Yellow"
        Pop-Location
        exit 1
    }

    # Get current version
    $currentVersion = Get-CurrentVersion
    Write-ColorText "?? Current version: $currentVersion" "White"
    Write-ColorText "?? New version: $NewVersion" "White"

    if ($currentVersion -eq $NewVersion) {
        Write-ColorText "??  New version is the same as current version" "Yellow"
        Pop-Location
        exit 0
    }

    Write-ColorText "`n?? Scanning for version references..." "Cyan"

    # Update version files
    # @() keeps a single change as an array (a bare hashtable's .Count would count its keys).
    $changes = @(Update-VersionFiles -OldVersion $currentVersion -NewVersion $NewVersion -PreviewOnly $WhatIf)

    if ($WhatIf) {
        Write-ColorText "`n? Preview completed. Use without -WhatIf to apply changes." "Green"
        Pop-Location
        exit 0
    }

    if ($changes -and $changes.Count -gt 0) {
        Write-ColorText "`n?? Version bump completed successfully!" "Green"
        Write-ColorText "   Updated $(Get-ChangedFileCount $changes) files" "White"
        Write-ColorText "   Total replacements: $(Get-TotalReplacements $changes)" "White"
        Write-ColorText "   Version: $currentVersion > $NewVersion" "White"
        Write-ColorText "`n?? Remember to commit these changes to git" "Cyan"
    }

    Pop-Location
}
catch {
    Write-ColorText "`n? Script failed: $($_.Exception.Message)" "Red"
    Pop-Location
    exit 1
}