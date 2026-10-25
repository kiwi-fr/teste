[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$SkipTests,
    [switch]$IncludeCuda,
    [switch]$SkipClean,
    [switch]$SkipPortable,
    [string]$OutputDir = "artifacts\installer"
)

$ErrorActionPreference = "Stop"

chcp 65001 > $null 2>&1
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$autoLang = if ($PSUICulture -like "fr*") { "FR" } else { "EN" }

Write-Host ""
Write-Host "  ==============================" -ForegroundColor Cyan
Write-Host "        C O C L I C O           " -ForegroundColor Cyan
Write-Host "     Installer Builder          " -ForegroundColor Cyan
Write-Host "  ==============================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Francais" -ForegroundColor White
Write-Host "  2. English" -ForegroundColor White
Write-Host ""
Write-Host "  [$autoLang]" -ForegroundColor DarkGray -NoNewline
$langChoice = Read-Host " Choix / Choice (1-2, Entree = auto)"
Write-Host ""

switch ($langChoice) {
    "1" { $Lang = "FR" }
    "2" { $Lang = "EN" }
    default { $Lang = $autoLang }
}

function T {
    param([string]$Fr, [string]$En)
    if ($Lang -eq "FR") { $Fr } else { $En }
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "[>] $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "[+] $Message" -ForegroundColor Green
}

function Write-ProgressBar {
    param([int]$Current, [int]$Total, [string]$Text)
    $width = 30
    $pct = if ($Total -gt 0) { ($Current + 1) / $Total } else { 0 }
    $filled = [math]::Floor($pct * $width)
    $bar = ("$([char]9608)" * $filled) + ("$([char]9617)" * ($width - $filled))
    $pctTxt = [math]::Round($pct * 100)
    if ($Text.Length -gt 40) { $Text = $Text.Substring(0, 40) }
    Write-Host "`r  [$bar] $pctTxt%  ($($Current + 1)/$Total) $Text        " -NoNewline -ForegroundColor Yellow
}

function New-CoclicoArchive {
    param(
        [string]$ZipPath,
        [object[]]$Files,
        [string]$BaseDir,
        [bool]$ShowProgress = $true
    )
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

    $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
    if ($sevenZip) {
        Write-Host (T "  [*] 7-Zip detecte : compression Deflate niveau 9" "  [*] 7-Zip detected: Deflate level 9 compression") -ForegroundColor DarkGray
        $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("coclico_stage_" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        try {
            $index = 0
            foreach ($f in $Files) {
                $rel = $f.FullName.Substring($BaseDir.Length) -replace '^[\\/]+', ''
                $dest = Join-Path $stage $rel
                $destDir = Split-Path $dest -Parent
                if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
                Copy-Item $f.FullName $dest -Force
                if ($ShowProgress) { Write-ProgressBar -Current $index -Total $Files.Count -Text $f.Name }
                $index++
            }
            if ($ShowProgress) { Write-Host "" }
            & 7z a -tzip -mx=9 -- $ZipPath "$stage\*" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw (T "7-Zip a echoue (code $LASTEXITCODE)." "7-Zip failed (code $LASTEXITCODE).")
            }
        }
        finally {
            Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host (T "  [-] 7-Zip absent : compression .NET standard (Deflate Optimal)" "  [-] 7-Zip not found: standard .NET compression (Deflate Optimal)") -ForegroundColor DarkGray
        $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
        $compressionLevel = [System.IO.Compression.CompressionLevel]::Optimal
        try {
            $index = 0
            foreach ($f in $Files) {
                $rel = $f.FullName.Substring($BaseDir.Length) -replace '^[\\/]+', ''
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $rel, $compressionLevel) | Out-Null
                if ($ShowProgress) { Write-ProgressBar -Current $index -Total $Files.Count -Text $f.Name }
                $index++
            }
        }
        finally {
            $zip.Dispose()
        }
        if ($ShowProgress) { Write-Host "" }
    }
}

$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$installerProjDir = "$rootDir\Coclico.InstallerAot"

try {
    Push-Location $rootDir

    Write-Section (T "VERIFICATIONS PRELIMINAIRES" "PRELIMINARY CHECKS")

    Write-Step (T "Verification de dotnet..." "Checking dotnet...")
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw (T "dotnet est introuvable. Installez le SDK .NET et relancez." "dotnet not found. Install the .NET SDK and retry.")
    }
    Write-Success "dotnet $(dotnet --version)"

    if (-not (Test-Path "$rootDir\Coclico\Coclico.csproj")) {
        throw "$(T "Coclico.csproj introuvable dans : " "Coclico.csproj not found in: ")$rootDir\Coclico"
    }

    if (-not (Test-Path "$installerProjDir\Coclico.InstallerAot.csproj")) {
        throw "$(T "Coclico.InstallerAot.csproj introuvable dans : " "Coclico.InstallerAot.csproj not found in: ")$installerProjDir"
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Step (T "Lecture de la version depuis version.txt..." "Reading version from version.txt...")
        $Version = (Get-Content "$rootDir\version.txt" -Raw).Trim()
        if ([string]::IsNullOrWhiteSpace($Version)) {
            throw (T "version.txt est absent ou vide a la racine du depot" "version.txt is missing or empty at repo root")
        }
    }
    Write-Success "$(T "Version cible : " "Target version: ")$Version"

    if (-not $SkipTests) {
        Write-Section (T "TESTS UNITAIRES COCLICO" "COClico UNIT TESTS")

        if (Test-Path "$rootDir\Coclico.Tests\Coclico.Tests.csproj") {
            Write-Step (T "Compilation et execution des tests unitaires..." "Building and running unit tests...")
            & dotnet test "$rootDir\Coclico.Tests\Coclico.Tests.csproj" --configuration $Configuration
            if ($LASTEXITCODE -ne 0) {
                throw (T "Les tests unitaires ont echoue. L'installeur n'est pas genere." "Unit tests failed. Installer is not generated.")
            }
            Write-Success (T "Tous les tests passent" "All tests passed")
        }
        else {
            Write-Host (T "  [-] Coclico.Tests absent, tests ignores." "  [-] Coclico.Tests not found, tests skipped.") -ForegroundColor DarkGray
        }
    }

    if (-not $SkipClean) {
        Write-Step (T "Nettoyage des anciens artefacts..." "Cleaning old artifacts...")
        $publishDir = "$rootDir\publish"
        if (Test-Path $publishDir) {
            Remove-Item "$publishDir\*" -Recurse -Force -ErrorAction SilentlyContinue
            Write-Success (T "Dossier publish nettoye" "publish folder cleaned")
        }
        $tempBuildDir = "$rootDir\artifacts\installer_build"
        if (Test-Path $tempBuildDir) {
            Remove-Item $tempBuildDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Success (T "Dossier installer_build nettoye" "installer_build cleaned")
        }
    }

    Write-Section (T "COMPILATION DE L'APPLICATION COCLICO" "BUILDING COCLICO APPLICATION")

    $publishDir = "$rootDir\publish"
    $mainExe = "$publishDir\Coclico.exe"

    if ($SkipPublish) {
        if (Test-Path $mainExe) {
            Write-Step (T "Utilisation du dossier publish existant" "Using existing publish folder")
        }
        else {
            throw (T "-SkipPublish utilise mais Coclico.exe absent du dossier publish." "-SkipPublish used but Coclico.exe missing from publish folder.")
        }
    }
    else {
        Write-Step "$(T "Publication de Coclico" "Publishing Coclico") ($Configuration, win-x64)..."
        $coclicoArgs = @(
            "publish", "$rootDir\Coclico\Coclico.csproj",
            "--configuration", $Configuration,
            "--runtime", "win-x64",
            "--self-contained", "false",
            "--output", $publishDir,
            "-p:PublishSingleFile=false",
            "-p:DebugSymbols=false",
            "-p:DebugType=None",
            "-p:Version=$Version",
            "-warnaserror"
        )
        if ($IncludeCuda) {
            $coclicoArgs += "-p:IncludeCuda=true"
        }
        & dotnet $coclicoArgs
        if ($LASTEXITCODE -ne 0) {
            throw (T "Echec de dotnet publish pour Coclico" "dotnet publish failed for Coclico")
        }
        Write-Success "$(T "Coclico publie dans : " "Coclico published to: ")$publishDir"
    }

    Write-Section (T "CREATION DU PAYLOAD" "CREATING PAYLOAD")

    $payloadZip = "$installerProjDir\payload.zip"

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    $cudaRuntimeFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$cudaRuntimeFiles.UnionWith([string[]]@(
        "ggml-cuda.dll",
        "cudart64_12.dll",
        "cublas64_12.dll",
        "cublasLt64_12.dll",
        "cufft64_11.dll",
        "nvrtc64_120_0.dll",
        "nvrtc64_120_0.alt.dll",
        "nvjitlink64_12.dll"
    ))

    $allPublishFiles = @(Get-ChildItem -Path $publishDir -Recurse -File)

    $files = @($allPublishFiles | Where-Object {
        if ($_.Extension -eq ".pdb") { return $false }
        if ($IncludeCuda) { return $true }
        $rel = ($_.FullName.Substring($publishDir.Length) -replace '^[\\/]+', '') -replace '\\', '/'
        if ($rel.StartsWith("runtimes/win-x64/native/cuda12/", [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
        return -not $cudaRuntimeFiles.Contains($_.Name)
    })

    if ($files.Count -eq 0) {
        throw (T "Aucun fichier a compresser dans le dossier publish." "No files to compress in publish folder.")
    }

    $excludedCount = $allPublishFiles.Count - $files.Count
    if (-not $IncludeCuda -and $excludedCount -gt 0) {
        Write-Host "$(T "  [-] $excludedCount fichiers CUDA ecartes du payload (telecharges lors de l'installation)." "  [-] $excludedCount CUDA files excluded from payload (downloaded during installation).")" -ForegroundColor DarkGray
    }

    $totalFiles = $files.Count
    Write-Step "$(T "Compression de $totalFiles fichiers..." "Compressing $totalFiles files...")"
    New-CoclicoArchive -ZipPath $payloadZip -Files $files -BaseDir $publishDir

    $zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
    Write-Success "$(T "Archive payload.zip creee : $zipSizeMb Mo ($totalFiles fichiers)" "payload.zip created: $zipSizeMb MB ($totalFiles files)")"

    Write-Section (T "COMPILATION DE COCLICO.INSTALLER (AVALONIA NATIVEAOT)" "BUILDING COCLICO.INSTALLER (AVALONIA NATIVEAOT)")

    Write-Step (T "Compilation en binaire natif unique (Avalonia + NativeAOT)..." "Building single native binary (Avalonia + NativeAOT)...")

    $tempBuildDir = "$rootDir\artifacts\installer_build"
    if (Test-Path $tempBuildDir) { Remove-Item $tempBuildDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempBuildDir -Force | Out-Null

    $aotArgs = @(
        "publish", "$installerProjDir\Coclico.InstallerAot.csproj",
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--output", $tempBuildDir,
        "-p:Version=$Version",
        "-warnaserror"
    )
    & dotnet @aotArgs
    if ($LASTEXITCODE -ne 0) {
        throw (T "Echec de compilation de Coclico.InstallerAot" "Coclico.InstallerAot build failed")
    }

    if (-not (Test-Path "$tempBuildDir\Coclico.Installer.exe")) {
        throw (T "Coclico.Installer.exe absent apres le publish AOT." "Coclico.Installer.exe missing after AOT publish.")
    }

    Write-Success (T "Coclico.InstallerAot compile : exe natif unique (payload + DLL natives embarques)" "Coclico.InstallerAot compiled: single native exe (payload + native DLLs embedded)")

    Write-Section (T "FINALISATION" "FINALIZATION")

    $finalOutputDir = "$rootDir\$OutputDir"
    if (-not (Test-Path $finalOutputDir)) {
        New-Item -ItemType Directory -Path $finalOutputDir -Force | Out-Null
    }

    $suffix = if ($IncludeCuda) { "-Full-CUDA" } else { "" }
    $finalExePath = "$finalOutputDir\Coclico-$Version$suffix.exe"

    Copy-Item "$tempBuildDir\Coclico.Installer.exe" $finalExePath -Force

    Remove-Item $tempBuildDir -Recurse -Force -ErrorAction SilentlyContinue

    $finalSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
    $hash = (Get-FileHash $finalExePath -Algorithm SHA256).Hash

    Write-Section (T "INSTALLEUR GENERE AVEC SUCCES" "INSTALLER GENERATED SUCCESSFULLY")

    Write-Host "$(T "Fichier   : " "File      : ")$finalExePath" -ForegroundColor White
    Write-Host "$(T "Taille    : " "Size      : ")$finalSizeMb Mo" -ForegroundColor White
    Write-Host "SHA-256   : $hash" -ForegroundColor White
    Write-Host "$(T "Version   : " "Version   : ")$Version" -ForegroundColor White

    Write-Host ""
    Write-Host (T "Fonctionnalites incluses :" "Included features:") -ForegroundColor Cyan
    Write-Host "  [+] Coclico application" -ForegroundColor Green
    Write-Host "  [+] Uninstall system" -ForegroundColor Green
    Write-Host "  [+] Windows registry entry" -ForegroundColor Green
    Write-Host "  [+] Uninstall from Windows Settings / Control Panel" -ForegroundColor Green
    Write-Host "  [+] Single native exe (Avalonia + NativeAOT, no .NET install needed)" -ForegroundColor Green
    Write-Host ""
    if (-not $IncludeCuda) {
        Write-Host (T "  Runtime CUDA et moteurs GPU : non embarques, telecharges lors de l'installation si carte NVIDIA." "  CUDA runtime and GPU engines: not bundled, downloaded during installation on NVIDIA cards.") -ForegroundColor DarkGray
        Write-Host ""
    }

    if (-not $SkipPortable) {
        Write-Section (T "VERSION PORTABLE" "PORTABLE VERSION")

        Write-Step (T "Publication autonome (fonctionne sans installer .NET)..." "Publishing self-contained (works without installing .NET)...")
        $portableDir = "$rootDir\publish-portable"
        if (Test-Path $portableDir) {
            Remove-Item $portableDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        $portableArgs = @(
            "publish", "$rootDir\Coclico\Coclico.csproj",
            "--configuration", $Configuration,
            "--runtime", "win-x64",
            "--self-contained", "true",
            "--output", $portableDir,
            "-p:PublishSingleFile=false",
            "-p:DebugSymbols=false",
            "-p:DebugType=None",
            "-p:Version=$Version",
            "-warnaserror"
        )
        if ($IncludeCuda) {
            $portableArgs += "-p:IncludeCuda=true"
        }
        & dotnet $portableArgs
        if ($LASTEXITCODE -ne 0) {
            throw (T "Echec de dotnet publish pour la version portable" "dotnet publish failed for portable version")
        }
        Write-Success "$(T "Coclico publie en autonome dans : " "Coclico published self-contained to: ")$portableDir"

        Write-Step (T "Creation de l'archive portable..." "Creating portable archive...")
        $portableZip = "$finalOutputDir\Coclico-Portable-$Version$suffix.zip"

        $allPortableFiles = @(Get-ChildItem -Path $portableDir -Recurse -File)
        $portableFiles = @($allPortableFiles | Where-Object {
            if ($_.Extension -eq ".pdb") { return $false }
            if ($IncludeCuda) { return $true }
            $rel = ($_.FullName.Substring($portableDir.Length) -replace '^[\\\/]+', '') -replace '\\', '/'
            if ($rel.StartsWith("runtimes/win-x64/native/cuda12/", [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
            return -not $cudaRuntimeFiles.Contains($_.Name)
        })
        if ($portableFiles.Count -eq 0) {
            throw (T "Aucun fichier pour la version portable." "No files for portable version.")
        }

        $totalPortable = $portableFiles.Count
        Write-Step "$(T "Compression de $totalPortable fichiers..." "Compressing $totalPortable files...")"

        New-CoclicoArchive -ZipPath $portableZip -Files $portableFiles -BaseDir $portableDir

        Write-Host ""

        $portableSizeMb = [math]::Round((Get-Item $portableZip).Length / 1MB, 2)
        $portableHash = (Get-FileHash $portableZip -Algorithm SHA256).Hash

        Write-Section (T "VERSION PORTABLE GENEREE AVEC SUCCES" "PORTABLE VERSION GENERATED SUCCESSFULLY")
        Write-Host "$(T "Fichier   : " "File      : ")$portableZip" -ForegroundColor White
        Write-Host "$(T "Taille    : " "Size      : ")$portableSizeMb Mo" -ForegroundColor White
        Write-Host "SHA-256   : $portableHash" -ForegroundColor White
        Write-Host "$(T "Version   : " "Version   : ")$Version" -ForegroundColor White
        Write-Host ""
        Write-Host (T "Mode d'emploi portable : decompresse le zip dans un dossier et lance Coclico.exe directement." "Portable how-to: unzip into a folder and run Coclico.exe directly.") -ForegroundColor Cyan
    }
    else {
        Write-Host (T "  [-] Version portable ignoree (-SkipPortable)." "  [-] Portable version skipped (-SkipPortable).") -ForegroundColor DarkGray
    }

    Write-Success (T "Pret pour la distribution !" "Ready for distribution!")
}
catch {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host "  $(T "ECHEC DU SCRIPT" "SCRIPT FAILURE")" -ForegroundColor Red
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "$(T "RAISON DE L'ECHEC :" "FAILURE REASON:")" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  Position : $($_.InvocationInfo.PositionMessage)" -ForegroundColor DarkGray
    Write-Host "  Type     : $($_.Exception.GetType().FullName)" -ForegroundColor DarkGray
    if ($_.Exception.InnerException) {
        Write-Host "  Cause    : $($_.Exception.InnerException.Message)" -ForegroundColor DarkGray
    }
    Write-Host "------------------------------------------" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "$(T "CONSEILS :" "HINTS:")" -ForegroundColor Cyan
    Write-Host "  - dotnet build Coclico.sln" -ForegroundColor Cyan
    Write-Host "  - dotnet --list-sdks" -ForegroundColor Cyan
    Write-Host "  $(T "- Relancer sans -SkipPublish" "- Retry without -SkipPublish")" -ForegroundColor Cyan
    Write-Host ""
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    Read-Host (T "Appuyez sur Entree pour fermer..." "Press Enter to close...") | Out-Null
}
