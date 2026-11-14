using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Coclico.Services.AI;

internal enum AiGpuVendor
{
    Unknown,
    None,
    Nvidia,
    Amd,
    Intel
}

internal sealed record AiCudaProgress(int Percent, string Message);

internal enum AiHardwareRecommendation
{
    None,
    DisableGpuForAmd,
    OfferCudaInstall
}

internal sealed record AiHardwareEnvironment(
    AiGpuVendor Vendor,
    bool CudaReady,
    AiHardwareRecommendation Recommendation,
    string GpuName);

internal static class AiHardwareService
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string NugetFlatContainer = "https://api.nuget.org/v3-flatcontainer/";
    private const long ExpectedCublasLtBytes = 674667520;
    private const string ExpectedCublasLtSha256 = "B199D1FF892A81B7FD3D57BA1781549609B41500B36008FEF326038393AD46C7";
    private const long RequiredDiskBytes = 1536L * 1024 * 1024;
    private const int TotalRuntimeSteps = 8;

    private static readonly Dictionary<string, (long Bytes, string Sha256)> PinnedDllHashes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cudart64_12.dll"] = (573952L, "C2C9A9C22A9BCBA90E261825968836787B331038047A26770CFFB7A583C28344"),
            ["cublas64_12.dll"] = (113716224L, "9513540E4EC4C51EE9E7304138C2CC255C29A8C181F9E80C38EFA25738BECD99"),
            ["cufft64_11.dll"] = (276121600L, "F4FEA9227B14843894AD5436725F9638B172171142C95291FC6AE7A493248221"),
            ["nvrtc64_120_0.dll"] = (86728192L, "02D2D7EF4690BF1A55DDA1D3AC94C86CE8D16920071C87BCD3E70E94EF346E93"),
            ["nvrtc64_120_0.alt.dll"] = (86794240L, "5464EA01B079D78042374DC7819684910C8E9D5C63000E2D2D5AA02298ECF2AE"),
            ["ggml-base.dll"] = (541184L, "7BCF256FAC58E129F49F36C9D790F35E46EB10F70F9ECF798AC99BCC8431B12E"),
            ["ggml.dll"] = (68096L, "96C1034540F7A7FC80D250AD3058057530C9DFFBEF7F844B9825316BC2133894"),
            ["llama.dll"] = (2038784L, "A46BBF360A74913380C76155C11EDEA8F8FF0E4034A6F0B1C45950AD75A505E2"),
            ["mtmd.dll"] = (667648L, "C81BFACA457EF82258CF6B208EB203D773CA1DD1A42B3E4B54EDABE13FB5AD15"),
            ["ggml-cuda.dll"] = (444673536L, "1F0A9DD29D4287BE9FF3FE5D238808283A85786AC740F21BEBA7242C7A478FB4")
        };

    private static readonly (string PackageId, string Label)[] NvidiaRuntimePackages =
    [
        ("ntvlibs.cuda12.cudart64_12.runtime.win-x64", "cudart64_12.dll"),
        ("ntvlibs.cuda12.cublas64_12.runtime.win-x64", "cublas64_12.dll"),
        ("ntvlibs.cuda12.cufft64_11.runtime.win-x64", "cufft64_11.dll"),
        ("ntvlibs.cuda12.nvrtc64_120_0.runtime.win-x64", "nvrtc64_120_0.dll"),
        ("ntvlibs.cuda12.nvrtc64_120_0.alt.runtime.win-x64", "nvrtc64_120_0.alt.dll")
    ];

    private static readonly byte[] DownloadBuffer = new byte[1_048_576];
    private static readonly byte[] CopyBuffer = new byte[81_920];

    private static AiGpuVendor? _cachedVendor;
    private static string? _cachedGpuName;

    public static AiGpuVendor DetectVendor()
    {
        if (_cachedVendor.HasValue)
        {
            return _cachedVendor.Value;
        }

        (AiGpuVendor Vendor, string Name) found = ScanPciDevices(displayOnly: true);
        if (found.Vendor == AiGpuVendor.None)
        {
            (AiGpuVendor Vendor, string Name) loose = ScanPciDevices(displayOnly: false);
            if (loose.Vendor != AiGpuVendor.None)
            {
                found = loose;
            }
        }

        _cachedVendor = found.Vendor;
        _cachedGpuName = found.Name;
        return found.Vendor;
    }

    public static string GetGpuDisplayName()
    {
        _ = DetectVendor();
        return string.IsNullOrWhiteSpace(_cachedGpuName) ? "carte graphique" : _cachedGpuName;
    }

    public static async Task<AiHardwareEnvironment> EvaluateEnvironmentAsync(bool useGpu)
    {
        AiGpuVendor vendor = await Task.Run(DetectVendor).ConfigureAwait(false);
        bool cudaReady = await Task.Run(IsCudaRuntimeAvailable).ConfigureAwait(false);

        AiHardwareRecommendation recommendation = vendor switch
        {
            AiGpuVendor.Amd when useGpu => AiHardwareRecommendation.DisableGpuForAmd,
            AiGpuVendor.Nvidia when useGpu && !cudaReady => AiHardwareRecommendation.OfferCudaInstall,
            _ => AiHardwareRecommendation.None
        };

        return new AiHardwareEnvironment(vendor, cudaReady, recommendation, GetGpuDisplayName());
    }

    public static bool IsCudaRuntimeAvailable()
    {
        string? cudaDir = ResolveCudaDirectory();
        if (cudaDir == null)
        {
            return false;
        }

        foreach (string dll in new[] { "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cuda.dll" })
        {
            if (!File.Exists(Path.Join(cudaDir, dll)))
            {
                return false;
            }
        }

        foreach (string dll in new[] { "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "nvrtc64_120_0.dll" })
        {
            if (ResolveDllPath(dll) == null)
            {
                return false;
            }
        }

        return true;
    }

    public static string ResolveNativeDirectory()
    {
        List<string> candidates = BuildCandidateDirectories();
        return candidates.FirstOrDefault(d => Directory.Exists(Path.Join(d, "cuda12")))
            ?? candidates.FirstOrDefault(Directory.Exists)
            ?? candidates[0];
    }

    public static string? ResolveCudaDirectory()
    {
        string cudaDir = Path.Join(ResolveNativeDirectory(), "cuda12");
        return Directory.Exists(cudaDir) ? cudaDir : null;
    }

    public static string? ResolveDllPath(string fileName)
    {
        foreach (string path in BuildCandidateDirectories().Select(candidate => Path.Join(candidate, fileName)))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static async Task DownloadCudaRuntimeAsync(IProgress<AiCudaProgress> progress, CancellationToken ct = default)
    {
        string installPath = AppContext.BaseDirectory;
        try
        {
            EnsureSufficientDiskSpace(installPath);

            string cudaDir = Path.Join(installPath, "runtimes", "win-x64", "native", "cuda12");
            _ = Directory.CreateDirectory(cudaDir);

            await DownloadAndExtractFolderAsync(
                    "llamasharp.backend.cuda12.windows", "0.26.0",
                    "LLamaSharpRuntimes/win-x64/native/cuda12/",
                    cudaDir,
                    "Moteur CUDA (ggml-cuda.dll)", 0, progress, ct)
                .ConfigureAwait(false);

            int step = 1;
            foreach ((string packageId, string label) in NvidiaRuntimePackages)
            {
                await DownloadAndExtractFolderAsync(
                        packageId, "12.8.1",
                        "runtimes/win-x64/native/",
                        installPath, label, step, progress, ct)
                    .ConfigureAwait(false);
                step++;
            }

            await DownloadAndJoinCublasLtAsync(installPath, progress, ct).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Droits insuffisants pour installer le runtime CUDA dans le dossier de Coclico. Relancez l'installeur Coclico en administrateur pour installer le runtime CUDA.", ex);
        }
    }

    private static List<string> BuildCandidateDirectories()
    {
        string baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Join(baseDir, "runtimes", "win-x64", "native"),
            Path.Join(baseDir, "native"),
            baseDir
        };

        // Publish single-file self-contained : les DLL natives sont extraites sous %TEMP%\.net\<app>\<hash>\
        try
        {
            string bundleRoot = Path.Combine(Path.GetTempPath(), ".net");
            if (Directory.Exists(bundleRoot))
            {
                foreach (string appDir in Directory.GetDirectories(bundleRoot))
                {
                    foreach (string hashDir in Directory.GetDirectories(appDir))
                    {
                        candidates.Add(Path.Join(hashDir, "runtimes", "win-x64", "native"));
                        candidates.Add(hashDir);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

        try
        {
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir?.Parent != null; i++)
            {
                dir = dir.Parent;
                candidates.Add(Path.Join(dir.FullName, "Coclico", "bin", "Debug", "net10.0-windows10.0.22621.0", "runtimes", "win-x64", "native"));
                candidates.Add(Path.Join(dir.FullName, "Coclico", "bin", "Release", "net10.0-windows10.0.22621.0", "runtimes", "win-x64", "native"));
                candidates.Add(Path.Join(dir.FullName, "runtimes", "win-x64", "native"));
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

        return candidates;
    }

    private static (AiGpuVendor Vendor, string Name) ScanPciDevices(bool displayOnly)
    {
        AiGpuVendor bestVendor = AiGpuVendor.None;
        string bestName = string.Empty;

        try
        {
            using RegistryKey? pciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (pciKey == null)
            {
                return (bestVendor, bestName);
            }

            foreach (string deviceKey in pciKey.GetSubKeyNames())
            {
                AiGpuVendor vendor = VendorFromId(deviceKey);
                if (vendor == AiGpuVendor.None)
                {
                    continue;
                }

                if (bestVendor != AiGpuVendor.None && VendorPriority(vendor) < VendorPriority(bestVendor))
                {
                    continue;
                }

                using RegistryKey? device = pciKey.OpenSubKey(deviceKey);
                if (device == null)
                {
                    continue;
                }

                foreach (RegistryKey instance in device.GetSubKeyNames().Select(device.OpenSubKey).OfType<RegistryKey>())
                {
                    using var instanceHandle = instance;

                    if (displayOnly && !IsDisplayAdapter(instance))
                    {
                        continue;
                    }

                    if (VendorPriority(vendor) >= VendorPriority(bestVendor))
                    {
                        bestVendor = vendor;
                        bestName = CleanDeviceDesc(instance.GetValue("DeviceDesc")?.ToString(), vendor);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

        return (bestVendor, bestName);
    }

    private static bool IsDisplayAdapter(RegistryKey instance)
    {
        try
        {
            using RegistryKey? control = instance.OpenSubKey("Control");
            object? classGuid = control?.GetValue("ClassGUID");
            return classGuid != null && string.Equals(classGuid.ToString(), DisplayClassGuid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or System.UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static AiGpuVendor VendorFromId(string deviceKeyId)
    {
        if (deviceKeyId.StartsWith("VEN_10DE", StringComparison.OrdinalIgnoreCase))
        {
            return AiGpuVendor.Nvidia;
        }
        if (deviceKeyId.StartsWith("VEN_1002", StringComparison.OrdinalIgnoreCase))
        {
            return AiGpuVendor.Amd;
        }
        if (deviceKeyId.StartsWith("VEN_8086", StringComparison.OrdinalIgnoreCase))
        {
            return AiGpuVendor.Intel;
        }
        return AiGpuVendor.None;
    }

    private static int VendorPriority(AiGpuVendor vendor)
    {
        return vendor switch
        {
            AiGpuVendor.Nvidia => 3,
            AiGpuVendor.Amd => 2,
            AiGpuVendor.Intel => 1,
            _ => 0
        };
    }

    private static string CleanDeviceDesc(string? raw, AiGpuVendor vendor)
    {
        string fallback = vendor switch
        {
            AiGpuVendor.Nvidia => "GPU NVIDIA",
            AiGpuVendor.Amd => "GPU AMD",
            AiGpuVendor.Intel => "GPU Intel",
            _ => "carte graphique"
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        int lastSemi = raw.LastIndexOf(';');
        string cleaned = lastSemi >= 0 ? raw[(lastSemi + 1)..] : raw;
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned.Trim();
    }

    private static void EnsureSufficientDiskSpace(string installPath)
    {
        try
        {
            string? driveRoot = Path.GetPathRoot(Path.GetFullPath(installPath));
            if (string.IsNullOrEmpty(driveRoot))
            {
                return;
            }

            var drive = new DriveInfo(driveRoot);
            if (drive.IsReady && drive.AvailableFreeSpace < RequiredDiskBytes)
            {
                throw new IOException(
                    $"Espace disque insuffisant sur {driveRoot} : {(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024)):F1} Go disponibles, au moins 1,5 Go requis pour le runtime CUDA.");
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
    }

    private static async Task DownloadAndExtractFolderAsync(
        string packageId, string version, string entryFolder,
        string targetDir, string label, int stepIndex,
        IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Join(Path.GetTempPath(), $"coclico-{packageId}.nupkg");
        string extractDir = Path.Join(Path.GetTempPath(), $"coclico-extract-{packageId}");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, stepIndex, progress, ct).ConfigureAwait(false);

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            using (var archive = ZipFile.OpenRead(tempNupkg))
            {
                archive.ExtractToDirectory(extractDir, overwriteFiles: true);
            }

            foreach (string file in Directory.EnumerateFiles(extractDir, "*.dll", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(extractDir, file).Replace('\\', '/');
                if (!relative.StartsWith(entryFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string destination = Path.Join(targetDir, Path.GetFileName(file));
                File.Copy(file, destination, overwrite: true);
                await VerifyPinnedDllAsync(destination, label, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                File.Delete(tempNupkg);
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, recursive: true);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
        }
    }

    private static async Task VerifyPinnedDllAsync(string filePath, string label, CancellationToken ct)
    {
        string fileName = Path.GetFileName(filePath);
        if (!PinnedDllHashes.TryGetValue(fileName, out (long Bytes, string Sha256) pinned))
        {
            throw new InvalidDataException($"Fichier natif inattendu '{fileName}' ({label}).");
        }

        var info = new FileInfo(filePath);
        if (info.Length != pinned.Bytes)
        {
            throw new InvalidDataException(
                $"'{fileName}' invalide : {info.Length} octets au lieu de {pinned.Bytes}.");
        }

        string hash = await ComputeSha256Async(filePath, ct).ConfigureAwait(false);
        if (!string.Equals(hash, pinned.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{fileName}' invalide : empreinte SHA-256 incorrecte.");
        }
    }

    private static async Task DownloadAndJoinCublasLtAsync(
        string installPath, IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        string tempFragment0 = Path.Join(Path.GetTempPath(), "coclico-cublaslt.f00");
        string tempFragment1 = Path.Join(Path.GetTempPath(), "coclico-cublaslt.f01");
        string targetFile = Path.Join(installPath, "cublasLt64_12.dll");

        try
        {
            await DownloadFragmentAsync(
                    "ntvlibs.cuda12.cublaslt64_12.runtime.win-x64.f00", "12.8.1",
                    "fragments/win-x64/native/cublasLt64_12.dll.f00",
                    tempFragment0, "cublasLt64_12.dll (fragment 1/2)", 6, progress, ct)
                .ConfigureAwait(false);

            await DownloadFragmentAsync(
                    "ntvlibs.cuda12.cublaslt64_12.runtime.win-x64.f01", "12.8.1",
                    "fragments/win-x64/native/cublasLt64_12.dll.f01",
                    tempFragment1, "cublasLt64_12.dll (fragment 2/2)", 7, progress, ct)
                .ConfigureAwait(false);

            progress.Report(new AiCudaProgress(99, "Assemblage de cublasLt64_12.dll..."));
            using (var output = File.Create(targetFile))
            {
                await AppendFileAsync(tempFragment0, output, ct).ConfigureAwait(false);
                await AppendFileAsync(tempFragment1, output, ct).ConfigureAwait(false);
            }

            var info = new FileInfo(targetFile);
            if (info.Length != ExpectedCublasLtBytes)
            {
                throw new InvalidDataException(
                    $"cublasLt64_12.dll assemblé invalide : {info.Length} octets au lieu de {ExpectedCublasLtBytes}.");
            }

            string hash = await ComputeSha256Async(targetFile, ct).ConfigureAwait(false);
            if (!string.Equals(hash, ExpectedCublasLtSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("cublasLt64_12.dll assemblé invalide : empreinte SHA-256 incorrecte.");
            }

            progress.Report(new AiCudaProgress(100, "Runtime CUDA installé avec succès."));
        }
        finally
        {
            try { File.Delete(tempFragment0); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
            try { File.Delete(tempFragment1); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
        }
    }

    private static async Task DownloadFragmentAsync(
        string packageId, string version, string entryPath,
        string destinationFile, string label, int stepIndex,
        IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Join(Path.GetTempPath(), $"coclico-{packageId}.nupkg");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, stepIndex, progress, ct).ConfigureAwait(false);

            using var archive = ZipFile.OpenRead(tempNupkg);
            ZipArchiveEntry? entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new InvalidDataException($"Fragment introuvable dans le package {packageId}.");
            }

            using (Stream source = entry.Open())
            using (FileStream target = File.Create(destinationFile))
            {
                source.CopyTo(target);
            }
        }
        finally
        {
            try { File.Delete(tempNupkg); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
        }
    }

    private static async Task DownloadFileAsync(
        string url, string targetFile, string label, int stepIndex,
        IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        using var client = new HttpClient(new Coclico.Security.Infrastructure.OutboundDomainGuardHandler(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        }));
        client.Timeout = TimeSpan.FromHours(2);

        int stepBase = stepIndex * 100 / TotalRuntimeSteps;
        int stepSpan = 100 / TotalRuntimeSteps;
        progress.Report(new AiCudaProgress(stepBase, $"Téléchargement : {label} connexion au serveur..."));

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 1_048_576, useAsync: true);

        byte[] buffer = DownloadBuffer;
        long totalRead = 0;
        int read;
        int lastReport = -1;

        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;

            double filePct = totalBytes > 0 ? totalRead * 100.0 / totalBytes.Value : 0;
            int pctNow = stepBase + (int)(filePct * stepSpan / 100.0);
            if (pctNow != lastReport)
            {
                lastReport = pctNow;
                progress.Report(new AiCudaProgress(
                    pctNow,
                    $"Téléchargement : {label} ({totalRead / 1048576} Mo / {(totalBytes ?? 0) / 1048576} Mo)"));
            }
        }
    }

    private static async Task AppendFileAsync(string fragmentFile, FileStream output, CancellationToken ct)
    {
        using var input = File.OpenRead(fragmentFile);
        byte[] buffer = CopyBuffer;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        var stream = File.OpenRead(filePath);
        await using (stream.ConfigureAwait(false))
        {
            byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
        }
    }
}
