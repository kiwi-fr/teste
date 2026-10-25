[CmdletBinding()]
param(
    # Ajoute l'execution de tes tests existants.
    [switch]$RunTests
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($root)) {
    $root = (Get-Location).Path
}

$solution = Join-Path $root "Coclico.sln"
$auditScript = Join-Path $root "Audit-Complet.ps1"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "SDK .NET introuvable."
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution introuvable : $solution"
}

if (-not (Test-Path -LiteralPath $auditScript)) {
    throw "Le script Audit-Complet.ps1 doit etre dans le meme dossier."
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$globalLog = Join-Path $root "audit-solution-$stamp.txt"
$statusFile = Join-Path $root "audit-lancement-$stamp.txt"

Push-Location $root

try {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " ETAPE 1 : AUDIT GLOBAL DE COCLICO.SLN" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan

    $buildExitCode = -1
    $oldPreference = $ErrorActionPreference

    try {
        # Ta commande d'origine, avec un journal horodate.
        # Les erreurs de compilation ne doivent pas bloquer la suite.
        $ErrorActionPreference = "Continue"

        & dotnet build ".\Coclico.sln" `
            --no-incremental `
            -p:EnableNETAnalyzers=true `
            -p:RunAnalyzersDuringBuild=true `
            -p:AnalysisLevel=latest `
            -p:AnalysisMode=All `
            -p:EnforceCodeStyleInBuild=true `
            2>&1 |
            ForEach-Object { "$_" } |
            Tee-Object -FilePath $globalLog |
            Out-Host

        $buildExitCode = $LASTEXITCODE
    }
    catch {
        $_ | Out-String |
            Add-Content -LiteralPath $globalLog -Encoding UTF8

        Write-Host "Echec de la premiere etape : voir le journal." `
            -ForegroundColor Yellow
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " ETAPE 2 : AUDIT DETAILLE DU DEPOT" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan

    $scriptStatus = "Non termine"

    try {
        # Lance le script complet fourni precedemment.
        & $auditScript -Root $root -RunTests:$RunTests.IsPresent

        $scriptStatus = "Termine : consulter les codes de chaque etape dans etapes.csv"
    }
    catch {
        $scriptStatus = "Interrompu : $($_.Exception.Message)"

        Write-Host $scriptStatus -ForegroundColor Red
    }

    @(
        "LANCEMENT DE L'AUDIT"
        "==================="
        ""
        "Date : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Solution : $solution"
        "Code retour de la compilation globale : $buildExitCode"
        "Journal global : $globalLog"
        "Audit detaille : $scriptStatus"
        "Tests demandes : $($RunTests.IsPresent)"
        ""
        "IMPORTANT"
        "- Les rapports detailles sont dans audit-reports."
        "- Verifier etapes.csv pour connaitre les controles ayant abouti."
        "- Une compilation en echec peut limiter les analyses suivantes."
        "- Plusieurs rapports peuvent contenir le meme diagnostic."
        "- Le nombre de messages n'est pas un nombre de bugs uniques."
        "- Aucun analyseur supplementaire n'a ete installe."
        "- Aucun correctif automatique de code n'a ete demande."
    ) | Set-Content -LiteralPath $statusFile -Encoding UTF8

    Write-Host ""
    Write-Host "Lancement termine." -ForegroundColor Green
    Write-Host "Compilation globale : code $buildExitCode"
    Write-Host "Audit detaille : $scriptStatus"
    Write-Host "Bilan : $statusFile"
}
finally {
    Pop-Location
}