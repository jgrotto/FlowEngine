#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Analyzes C# files for compliance with the one-type-per-file rule.

.DESCRIPTION
    This script scans all C# source files in the FlowEngine solution and identifies:
    - Files with multiple public types (violations)
    - Suggested refactoring for large multi-type files
    - Overall compliance statistics

.PARAMETER Path
    The root path to analyze. Defaults to the current directory.

.PARAMETER ShowDetails
    Show detailed analysis of each violation.

.PARAMETER OutputFormat
    Output format: Console, Json, or Csv. Defaults to Console.

.EXAMPLE
    .\AnalyzeFileOrganization.ps1 -ShowDetails
    
.EXAMPLE
    .\AnalyzeFileOrganization.ps1 -OutputFormat Json > violations.json
#>

param(
    [string]$Path = ".",
    [switch]$ShowDetails,
    [ValidateSet("Console", "Json", "Csv")]
    [string]$OutputFormat = "Console"
)

class FileAnalysis {
    [string]$FilePath
    [int]$PublicTypeCount
    [string[]]$PublicTypes
    [int]$LineCount
    [string]$Recommendation
}

function Get-PublicTypes {
    param([string]$Content)
    
    # Remove comments and strings to avoid false positives
    $cleaned = $Content -replace '//.*', ''
    $cleaned = $cleaned -replace '/\*[\s\S]*?\*/', ''
    $cleaned = $cleaned -replace '"[^"\\]*(?:\\.[^"\\]*)*"', ''
    $cleaned = $cleaned -replace "'[^'\\]*(?:\\.[^'\\]*)*'", ''
    
    # Patterns for public type declarations
    $patterns = @(
        'public\s+(class|interface|enum|record|struct)\s+(\w+)',
        'public\s+sealed\s+(class|record)\s+(\w+)',
        'public\s+abstract\s+class\s+(\w+)',
        'public\s+static\s+class\s+(\w+)',
        'public\s+readonly\s+(record\s+)?struct\s+(\w+)'
    )
    
    $types = @()
    foreach ($pattern in $patterns) {
        $matches = [regex]::Matches($cleaned, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        foreach ($match in $matches) {
            $typeName = if ($match.Groups.Count -gt 2) { $match.Groups[2].Value } else { $match.Groups[1].Value }
            $types += $typeName
        }
    }
    
    return $types
}

function Get-Recommendation {
    param([FileAnalysis]$Analysis)
    
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($Analysis.FilePath)
    
    if ($Analysis.PublicTypeCount -eq 0) {
        return "OK - No public types or internal-only file"
    }
    elseif ($Analysis.PublicTypeCount -eq 1) {
        return "OK - Follows one-type-per-file rule"
    }
    elseif ($Analysis.PublicTypeCount -le 3) {
        return "MINOR - Consider splitting into separate files for better organization"
    }
    elseif ($Analysis.PublicTypeCount -le 5) {
        return "MODERATE - Should be split into logical groupings (e.g., /Results/, /Enums/)"
    }
    else {
        return "MAJOR - High priority for refactoring into separate files"
    }
}

function Analyze-Files {
    param([string]$RootPath)
    
    $sourceFiles = Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | 
        Where-Object { 
            $_.FullName -notmatch "\\(obj|bin)\\" -and 
            $_.Name -notmatch "\.(g|Designer)\.cs$" 
        }
    
    $analyses = @()
    
    foreach ($file in $sourceFiles) {
        try {
            $content = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
            if (-not $content) { continue }
            
            $publicTypes = Get-PublicTypes -Content $content
            $lineCount = ($content -split "`n").Count
            
            $analysis = [FileAnalysis]@{
                FilePath = $file.FullName
                PublicTypeCount = $publicTypes.Count
                PublicTypes = $publicTypes
                LineCount = $lineCount
                Recommendation = ""
            }
            
            $analysis.Recommendation = Get-Recommendation -Analysis $analysis
            $analyses += $analysis
        }
        catch {
            Write-Warning "Failed to analyze file: $($file.FullName) - $($_.Exception.Message)"
        }
    }
    
    return $analyses
}

function Show-ConsoleReport {
    param([FileAnalysis[]]$Analyses)
    
    $violations = $Analyses | Where-Object { $_.PublicTypeCount -gt 1 }
    $compliant = $Analyses | Where-Object { $_.PublicTypeCount -le 1 }
    $total = $Analyses.Count
    
    Write-Host "FlowEngine File Organization Analysis" -ForegroundColor Cyan
    Write-Host "=" * 50
    Write-Host ""
    
    # Summary
    Write-Host "Summary:" -ForegroundColor Yellow
    Write-Host "  Total files analyzed: $total"
    $compliantPercent = [math]::Round(($compliant.Count / $total) * 100, 1)
    $violationPercent = [math]::Round(($violations.Count / $total) * 100, 1)
    Write-Host "  Compliant files: $($compliant.Count) ($compliantPercent percent)"
    Write-Host "  Files with violations: $($violations.Count) ($violationPercent percent)"
    Write-Host ""
    
    if ($violations.Count -eq 0) {
        Write-Host "All files comply with the one-type-per-file rule!" -ForegroundColor Green
        return
    }
    
    # Violations by severity
    $major = $violations | Where-Object { $_.PublicTypeCount -gt 5 }
    $moderate = $violations | Where-Object { $_.PublicTypeCount -in 4..5 }
    $minor = $violations | Where-Object { $_.PublicTypeCount -in 2..3 }
    
    Write-Host "Violations by Priority:" -ForegroundColor Yellow
    if ($major.Count -gt 0) {
        Write-Host "  MAJOR (>5 types): $($major.Count) files" -ForegroundColor Red
    }
    if ($moderate.Count -gt 0) {
        Write-Host "  MODERATE (4-5 types): $($moderate.Count) files" -ForegroundColor Yellow
    }
    if ($minor.Count -gt 0) {
        Write-Host "  MINOR (2-3 types): $($minor.Count) files" -ForegroundColor DarkYellow
    }
    Write-Host ""
    
    # Top violations
    Write-Host "Top Violations (Refactor First):" -ForegroundColor Yellow
    $topViolations = $violations | Sort-Object -Property PublicTypeCount -Descending | Select-Object -First 10
    
    foreach ($violation in $topViolations) {
        $relativePath = $violation.FilePath -replace [regex]::Escape($Path), "."
        $color = if ($violation.PublicTypeCount -gt 5) { "Red" } 
                elseif ($violation.PublicTypeCount -gt 3) { "Yellow" } 
                else { "DarkYellow" }
        
        Write-Host "  $relativePath" -ForegroundColor $color
        Write-Host "     Types: $($violation.PublicTypeCount) | Lines: $($violation.LineCount)"
        
        if ($ShowDetails -and $violation.PublicTypes.Count -gt 0) {
            Write-Host "     Public types: $($violation.PublicTypes -join ', ')" -ForegroundColor Gray
        }
        
        Write-Host "     Recommendation: $($violation.Recommendation)" -ForegroundColor Gray
        Write-Host ""
    }
    
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Review docs/CODING_STANDARDS.md for detailed guidance"
    Write-Host "  2. Start with MAJOR violations (red files above)"
    Write-Host "  3. Create logical groupings: /Results/, /Enums/, /Exceptions/"
    Write-Host "  4. Enable file organization checking in new projects"
}

function Export-JsonReport {
    param([FileAnalysis[]]$Analyses, [string]$OutputPath)
    
    $report = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        Summary = @{
            TotalFiles = $Analyses.Count
            CompliantFiles = ($Analyses | Where-Object { $_.PublicTypeCount -le 1 }).Count
            ViolationFiles = ($Analyses | Where-Object { $_.PublicTypeCount -gt 1 }).Count
        }
        Violations = $Analyses | Where-Object { $_.PublicTypeCount -gt 1 } | ForEach-Object {
            @{
                FilePath = $_.FilePath
                PublicTypeCount = $_.PublicTypeCount
                PublicTypes = $_.PublicTypes
                LineCount = $_.LineCount
                Recommendation = $_.Recommendation
            }
        }
    }
    
    $json = $report | ConvertTo-Json -Depth 4
    if ($OutputPath) {
        $json | Out-File -FilePath $OutputPath -Encoding utf8
        Write-Host "Report exported to: $OutputPath"
    } else {
        return $json
    }
}

# Main execution
try {
    $resolvedPath = Resolve-Path -Path $Path
    Write-Host "Analyzing files in: $resolvedPath" -ForegroundColor Green
    
    $analyses = Analyze-Files -RootPath $resolvedPath
    
    switch ($OutputFormat) {
        "Console" { Show-ConsoleReport -Analyses $analyses }
        "Json" { Export-JsonReport -Analyses $analyses }
        "Csv" { 
            $analyses | Where-Object { $_.PublicTypeCount -gt 1 } | 
                Select-Object FilePath, PublicTypeCount, LineCount, Recommendation | 
                ConvertTo-Csv -NoTypeInformation
        }
    }
}
catch {
    Write-Error "Analysis failed: $($_.Exception.Message)"
    exit 1
}