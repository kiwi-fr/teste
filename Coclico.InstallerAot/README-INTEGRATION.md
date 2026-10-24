# 🥥 Coclico.InstallerAot — Prototype d'installeur Avalonia + NativeAOT

**Option D de la recherche** : garder un installeur **100 % personnalisé** (votre code C#, votre design), mais compilé en **natif** (NativeAOT) au lieu d'embarquer 70 Mo de runtime .NET/WPF.

- Installeur actuel (WPF auto-contenu) : **~85 Mo**
- Objectif de ce prototype : **~35-45 Mo** (app native ~15-25 Mo + payload 17 Mo) — *à mesurer après compilation*
- Design : fenêtre sombre Fluent, entièrement modifiable dans `MainWindow.axaml`

---

## 📑 Sommaire

1. [Structure du pack](#-structure-du-pack)
2. [Prérequis](#-prérequis)
3. [Test rapide (5 minutes)](#-test-rapide-5-minutes)
4. [Intégration dans build-installer.ps1](#-intégration-dans-build-installer-ps1)
5. [Ce qui est inclus dans le prototype](#-ce-qui-est-inclus-dans-le-prototype)
6. [Ce qu'il reste à porter depuis votre installeur WPF](#-ce-quil-reste-à-porter-depuis-votre-installeur-wpf)
7. [Points d'attention NativeAOT (important)](#-points-dattention-nativeaot-important)
8. [Revenir en arrière](#-revenir-en-arrière)

---

## 📁 Structure du pack

```
Coclico.InstallerAot/
├── README-INTEGRATION.md          ← ce fichier
├── Coclico.InstallerAot/
│   ├── Coclico.InstallerAot.csproj
│   ├── Program.cs                 ← point d'entrée Avalonia
│   ├── App.axaml / App.axaml.cs   ← thème Fluent (sombre)
│   ├── MainWindow.axaml           ← design 100% modifiable
│   ├── MainWindow.axaml.cs        ← logique d'installation
│   ├── app.manifest               ← droits admin (comme l'actuel)
│   └── Services/
│       ├── PayloadExtractor.cs    ← extraction payload.zip embarqué + progression
│       ├── RegistryService.cs     ← entrée "Apps & Features"
│       └── ShortcutService.cs     ← raccourcis Bureau + Menu Démarrer
```

---

## ✅ Prérequis

1. **SDK .NET 10** — déjà installé chez vous ✔️
2. **Charge de travail C++** dans Visual Studio (ou VS Build Tools) :
   NativeAOT utilise le linker natif (`link.exe`). Si le build échoue avec
   « link.exe introuvable », installez *« Développement Desktop en C++ »*
   dans Visual Studio Installer (une seule fois).
3. **payload.zip** — généré par votre script existant (aucun changement).

---

## 🚀 Test rapide (5 minutes)

1. Copiez le dossier `Coclico.InstallerAot` à la racine de votre solution.
2. Ajoutez-le à la solution (optionnel pour tester) :
   ```powershell
   dotnet sln Coclico.sln add Coclico.InstallerAot/Coclico.InstallerAot.csproj
   ```
3. Déposez un `payload.zip` dans `Coclico.InstallerAot/Coclico.InstallerAot/`
   (récupérez celui de votre dernier build : `Coclico.Installer/payload.zip`).
4. Compilez :
   ```powershell
   dotnet publish Coclico.InstallerAot/Coclico.InstallerAot.csproj -c Release -r win-x64
   ```
   Le résultat est **un seul exe natif** dans `bin/Release/net10.0/win-x64/publish/`.
5. **Mesurez sa taille** et comparez à 85 Mo — envoyez-moi le chiffre !

> ⚠️ Premier build NativeAOT : long (1-3 min), c'est normal. Les suivants sont plus rapides (cache).

---

## 🔧 Intégration dans build-installer.ps1

Quand le prototype vous convient, remplacez dans `build-installer.ps1` la section
« COMPILATION DE COCLICO.INSTALLER » (l'appel `Invoke-DotnetPublish` du projet
`Coclico.Installer`) par :

```powershell
Write-Step "Compilation de l'installeur Avalonia NativeAOT..."
$installerAotDir = "$rootDir\Coclico.InstallerAot\Coclico.InstallerAot"

# Le payload doit etre present AVANT la compilation (ressource embarquee)
Copy-Item "$installerProjDir\payload.zip" "$installerAotDir\payload.zip" -Force

& dotnet publish "$installerAotDir\Coclico.InstallerAot.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --output $tempBuildDir `
    "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) {
    throw "Echec de compilation de Coclico.InstallerAot"
}
```

Le reste du script (finalisation, copie, hash, zip portable) **ne change pas** :
l'exe produit s'appelle `Coclico.Installer.exe` (voir `AssemblyName` du csproj),
exactement comme aujourd'hui.

---

## 📦 Ce qui est inclus dans le prototype

- ✅ Extraction du `payload.zip` embarqué avec barre de progression
- ✅ Entrée registre « Ajouter/Supprimer des programmes » (désinstallation)
- ✅ Raccourcis Bureau + Menu Démarrer
- ✅ Bouton « Lancer Coclico » après installation
- ✅ Fenêtre sombre moderne, 100 % modifiable (`MainWindow.axaml`)
- ✅ Gestion d'erreurs avec bouton « Réessayer »

## 🚧 Ce qu'il reste à porter depuis votre installeur WPF

Le prototype couvre le socle. Pour la parité complète avec `Coclico.Installer`, il reste à porter :

| Fonctionnalité | Où la porter |
|---|---|
| Choix du dossier d'installation | Ajouter un `TextBox` + bouton « Parcourir » (`OpenFolderDialog` Avalonia) dans MainWindow |
| Téléchargement du runtime .NET (`DotnetRuntimeService`) | `HttpClient` dans un service — compatible AOT |
| Détection GPU (`GpuDetector`, utilise WMI/System.Management) | ⚠️ Voir « Points d'attention » ci-dessous |
| Téléchargement CUDA (`CudaRuntimeDownloader`) | `HttpClient` — compatible AOT |
| Téléchargement modèles IA (`AiModelDownloader`) | `HttpClient` — compatible AOT |
| Fenêtre de désinstallation (`UninstallWindow`) | Nouvelle fenêtre Avalonia + `RegistryService.RemoveUninstallEntry()` |
| Localisation fr/en | Chaînes dans un dictionnaire simple (pas de XAML satellites sous AOT) |

## ⚠️ Points d'attention NativeAOT (important)

1. **System.Management (WMI) n'est pas compatible NativeAOT** — c'est du COM/des appels
   WMI réflexifs. Votre `GpuDetector` actuel ne compilera pas tel quel.
   **Solution recommandée** : déplacer la détection GPU au **premier lancement de
   Coclico** (l'app WPF, elle, n'a aucune limitation), et télécharger CUDA/modèles IA
   à ce moment-là. C'est aussi plus simple côté installeur.
2. **Pas de COM direct** : les raccourcis passent par un appel PowerShell externe
   (déjà codé dans `ShortcutService`). Alternative propre : raccourcis créés par
   Coclico au premier lancement.
3. **Pas de XAML chargé dynamiquement** : tout le XAML est compilé au build (c'est
   le comportement par défaut d'Avalonia, rien à faire).
4. **Taille à mesurer** : l'estimation « app ~15-25 Mo + payload » reste une
   estimation tant que vous n'avez pas compilé. Si la mesure vous déçoit, on
   compare avec les options A/B du rapport de recherche.

---

## ↩️ Revenir en arrière

Le prototype est un projet **à côté** de l'actuel : `Coclico.Installer` (WPF)
reste en place et fonctionnel tant que vous ne modifiez pas `build-installer.ps1`.
Aucun risque pour votre chaîne de build actuelle.
