[CmdletBinding()]
param(
    [string]$Root = $PSScriptRoot,

    # Execute aussi les tests deja presents.
    [switch]$RunTests
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Get-Location).Path
}

$Root = (Resolve-Path -LiteralPath $Root).Path

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "SDK .NET introuvable. Installe le SDK adapte au projet."
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportRoot = Join-Path $Root "audit-reports"
$out = Join-Path $reportRoot $stamp

New-Item -ItemType Directory -Path $out -Force | Out-Null

$steps = [System.Collections.Generic.List[object]]::new()
$diagnostics = [System.Collections.Generic.List[object]]::new()

# Ignore les dossiers de compilation, de publication et les dependances.
$excluded = '[\\/](bin|obj|\.git|\.vs|node_modules|packages|publish|publish-portable|TestResults|audit-reports)[\\/]'

$projects = @(
    Get-ChildItem -LiteralPath $Root -Filter "*.csproj" -Recurse -File |
        Where-Object { $_.FullName -notmatch $excluded } |
        Sort-Object FullName
)

if ($projects.Count -eq 0) {
    throw "Aucun fichier .csproj trouve dans $Root"
}

function Invoke-AuditStep {
    param(
        [string]$Name,
        [string[]]$CliArgs,
        [string]$LogPath
    )

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan

    $start = Get-Date
    $exitCode = -1
    $oldPreference = $ErrorActionPreference

    try {
        # Les erreurs natives sont journalisees sans interrompre l'audit.
        $ErrorActionPreference = "Continue"

        & dotnet @CliArgs 2>&1 |
            ForEach-Object { "$_" } |
            Tee-Object -FilePath $LogPath |
            Out-Host

        $exitCode = $LASTEXITCODE
    }
    catch {
        $_ | Out-String |
            Add-Content -LiteralPath $LogPath -Encoding UTF8
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    $steps.Add([pscustomobject]@{
        Etape       = $Name
        CodeRetour  = $exitCode
        DureeSecondes = [math]::Round(
            ((Get-Date) - $start).TotalSeconds, 1
        )
        Commande    = "dotnet " + ($CliArgs -join " ")
        Journal     = $LogPath
    })

    if ($exitCode -eq 0) {
        Write-Host "Termine : code 0" -ForegroundColor Green
    }
    else {
        Write-Host (
            "Code $exitCode : consulter le journal. " +
            "Cela peut signaler des diagnostics ou un echec de l'outil."
        ) -ForegroundColor Yellow
    }
}

Push-Location $Root

try {
    # Inventaire et environnement.
    $projects.FullName |
        Set-Content -LiteralPath (
            Join-Path $out "projets.txt"
        ) -Encoding UTF8

    Invoke-AuditStep `
        -Name "Informations SDK .NET" `
        -CliArgs @("--info") `
        -LogPath (Join-Path $out "dotnet-info.log")

    if (Get-Command git -ErrorAction SilentlyContinue) {
        & git -C $Root status --short 2>&1 |
            Out-File (
                Join-Path $out "git-avant.txt"
            ) -Encoding UTF8
    }

    $index = 0

    foreach ($project in $projects) {
        $index++

        $safeName = $project.BaseName -replace '[^\w.-]', '_'
        $projectOut = Join-Path $out (
            "{0:D3}-{1}" -f $index, $safeName
        )

        New-Item -ItemType Directory `
            -Path $projectOut -Force | Out-Null

        $projectPath = $project.FullName

        $projectPath |
            Set-Content -LiteralPath (
                Join-Path $projectOut "projet.txt"
            ) -Encoding UTF8

        # Restore explicite. Peut necessiter Internet ou des acces NuGet.
        Invoke-AuditStep `
            -Name "$safeName : restauration" `
            -CliArgs @(
                "restore",
                $projectPath
            ) `
            -LogPath (Join-Path $projectOut "restore.log")

        # Analyse du code dans les deux configurations.
        foreach ($configuration in @("Debug", "Release")) {
            Invoke-AuditStep `
                -Name "$safeName : analyse $configuration" `
                -CliArgs @(
                    "build",
                    $projectPath,
                    "--configuration", $configuration,
                    "--no-incremental",
                    "--nologo",
                    "--verbosity", "minimal",
                    "-p:EnableNETAnalyzers=true",
                    "-p:RunAnalyzersDuringBuild=true",
                    "-p:AnalysisLevel=latest",
                    "-p:AnalysisMode=All",
                    "-p:EnforceCodeStyleInBuild=true"
                ) `
                -LogPath (
                    Join-Path $projectOut "build-$configuration.log"
                )
        }

        # Aucun correctif de formatage n'est applique.
        # Chaque passe conserve son propre rapport.
        foreach ($kind in @("whitespace", "style", "analyzers")) {
            $formatOut = Join-Path $projectOut "format-$kind"

            New-Item -ItemType Directory `
                -Path $formatOut -Force | Out-Null

            $formatArgs = @(
                "format",
                $kind,
                $projectPath,
                "--verify-no-changes",
                "--verbosity", "normal",
                "--report", $formatOut
            )

            if ($kind -ne "whitespace") {
                $formatArgs += @("--severity", "info")
            }

            Invoke-AuditStep `
                -Name "$safeName : verification $kind" `
                -CliArgs $formatArgs `
                -LogPath (
                    Join-Path $projectOut "format-$kind.log"
                )
        }

        # Audit des packages, sans les mettre a jour.
        foreach ($kind in @("vulnerable", "outdated", "deprecated")) {
            Invoke-AuditStep `
                -Name "$safeName : packages $kind" `
                -CliArgs @(
                    "list",
                    $projectPath,
                    "package",
                    "--$kind",
                    "--include-transitive"
                ) `
                -LogPath (
                    Join-Path $projectOut "packages-$kind.log"
                )
        }
    }

    if ($RunTests) {
        # Utilise explicitement Coclico.sln si elle existe.
        # Sinon, execute les tests via chaque projet detecte.
        $solution = Join-Path $Root "Coclico.sln"

        if (Test-Path -LiteralPath $solution) {
            $testTargets = @($solution)
        }
        else {
            $testTargets = @($projects.FullName)
        }

        $testIndex = 0

        foreach ($target in $testTargets) {
            $testIndex++

            Invoke-AuditStep `
                -Name "Tests : $target" `
                -CliArgs @(
                    "test",
                    $target,
                    "--configuration", "Debug"
                ) `
                -LogPath (
                    Join-Path $out "tests-$testIndex.log"
                )
        }
    }

    # Extrait les diagnostics des journaux de compilation.
    # Le CSV conserve les lignes d'origine pour ne pas perdre le contexte.
    $pattern = '(?i)\b(error|warning|erreur|avertissement)\s+([A-Z]+\d+)\s*:'

    $buildLogs = @(
        Get-ChildItem -LiteralPath $out `
            -Filter "build-*.log" -Recurse -File
    )

    foreach ($log in $buildLogs) {
        foreach ($line in Get-Content -LiteralPath $log.FullName) {
            if ($line -match $pattern) {
                $diagnostics.Add([pscustomobject]@{
                    Severite = $Matches[1].ToLowerInvariant()
                    Regle    = $Matches[2].ToUpperInvariant()
                    Diagnostic = $line.Trim()
                    Journal  = $log.FullName
                })
            }
        }
    }

    # Evite les repetitions exactes d'un diagnostic dans le meme journal.
    $uniqueDiagnostics = @(
        $diagnostics |
            Sort-Object -Property Journal, Diagnostic -Unique
    )

    if ($uniqueDiagnostics.Count -gt 0) {
        $uniqueDiagnostics |
            Export-Csv -LiteralPath (
                Join-Path $out "diagnostics.csv"
            ) -NoTypeInformation -Encoding UTF8

        $uniqueDiagnostics |
            Group-Object Regle |
            Sort-Object Count -Descending |
            Select-Object @{
                Name = "Regle"; Expression = { $_.Name }
            }, @{
                Name = "Occurrences"; Expression = { $_.Count }
            } |
            Export-Csv -LiteralPath (
                Join-Path $out "regles-par-frequence.csv"
            ) -NoTypeInformation -Encoding UTF8
    }

    $steps |
        Export-Csv -LiteralPath (
            Join-Path $out "etapes.csv"
        ) -NoTypeInformation -Encoding UTF8

    $nonZero = @(
        $steps | Where-Object { $_.CodeRetour -ne 0 }
    )

    $summary = @(
        "AUDIT DU DEPOT"
        "=============="
        ""
        "Date : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Racine : $Root"
        "Projets detectes : $($projects.Count)"
        "Etapes executees : $($steps.Count)"
        "Etapes avec code non nul : $($nonZero.Count)"
        "Entrees de diagnostics de compilation : $($uniqueDiagnostics.Count)"
        "Tests demandes : $($RunTests.IsPresent)"
        ""
        "COMMENT LIRE LES RESULTATS"
        "-------------------------"
        "- etapes.csv : commandes, codes retour et journaux."
        "- diagnostics.csv : diagnostics extraits des compilations."
        "- regles-par-frequence.csv : regroupement par identifiant."
        "- Chaque dossier de projet contient ses journaux."
        ""
        "LIMITES IMPORTANTES"
        "------------------"
        "- Une etape en erreur ne signifie pas que le controle a abouti."
        "- Un code non nul de dotnet format peut signaler des changements proposes."
        "- Les codes retour des audits de packages ne remplacent pas leur lecture."
        "- Debug, Release et les projets dependants peuvent repeter des diagnostics."
        "- Le nombre d'entrees n'est donc pas un nombre de bugs uniques."
        "- Les configurations existantes peuvent desactiver certaines regles."
        "- Les projets non chargeables doivent etre analyses apres correction."
        "- Pas de garantie d'absence de bugs, failles ou code mort."
        "- Aucun package d'analyse n'est ajoute automatiquement."
        "- Pas d'analyse exhaustive des bindings XAML ou du comportement runtime."
        "- Aucun correctif automatique n'a ete demande au formateur."
        "- Restore/build peuvent executer les targets MSBuild du depot."
    )

    $summary |
        Set-Content -LiteralPath (
            Join-Path $out "RESUME.txt"
        ) -Encoding UTF8

    if (Get-Command git -ErrorAction SilentlyContinue) {
        & git -C $Root status --short 2>&1 |
            Out-File (
                Join-Path $out "git-apres.txt"
            ) -Encoding UTF8
    }

    Write-Host ""
    Write-Host "Audit termine." -ForegroundColor Green
    Write-Host "Rapports : $out" -ForegroundColor Cyan
    Write-Host "Commence par RESUME.txt et etapes.csv."
}
finally {
    Pop-Location
}