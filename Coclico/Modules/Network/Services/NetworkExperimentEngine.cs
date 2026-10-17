using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Coclico.Models.Network;
using Microsoft.Win32;

namespace Coclico.Services.Network;

[SupportedOSPlatform("windows")]
public sealed class NetworkExperimentEngine : INetworkExperimentEngine
{
    private const string NetworkClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const double MinimalScoreGain = 1.5;
    private const double MinimalLatencyGainMs = 0.5;
    private const double MinimalP95GainMs = 1.0;
    private const int BaselineProbePings = 24;
    private const int CandidateProbePings = 16;
    private const int PingTimeoutMs = 1000;
    private const int PingIntervalMs = 25;

    private static readonly Dictionary<string, string[]> PreferredParameterValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TCPNoDelay"] = ["1"],
        ["TcpAckFrequency"] = ["1"],
        ["TCPDelAckTicks"] = ["0"],
        ["NetworkThrottlingIndex"] = ["4294967295"],
        ["SystemResponsiveness"] = ["0"],
        ["NonBestEffortLimit"] = ["0"],
        ["EnablePMTUDiscovery"] = ["1"],
        ["SynAttackProtect"] = ["1"],
        ["NonSackRttResiliency"] = ["1"],
        ["DisableTaskOffload"] = ["0"],
        ["TCPMaxDataRetransmissions"] = ["3"],
        ["InitialRto"] = ["1000"],
        ["MinRto"] = ["20"],
        ["SackOpts"] = ["1"],
        ["FastSendDatagramThreshold"] = ["2048"],
        ["DefaultTOSValue"] = ["40"],
        ["DisableIPSourceRouting"] = ["2"],
        ["EnableICMPRedirect"] = ["0"],
        ["DoNotUseNLA"] = ["1"],
        ["EnablePMTUBHDetect"] = ["1"],
        ["FastOpen"] = ["1"],
        ["DisableDHCPMediaSense"] = ["1"],
        ["EcnCapability"] = ["1"],
        ["Timestamps"] = ["0"],
        ["Pacing"] = ["1"],
        ["IGMPLevel"] = ["2"],
        ["ArpRetryCount"] = ["2"],
        ["ArpCacheLife"] = ["120"],
        ["TcpInitialRTT"] = ["1"],
        ["DnsCacheEntries"] = ["10240"],
        ["MaxCacheEntryTtlLimit"] = ["86400"],
        ["NegativeCacheTime"] = ["0"],
        ["NetFailureCacheTime"] = ["0"],
        ["*PriorityVLANTag"] = ["1"],
        ["*RscIPv4"] = ["0"],
        ["*RscIPv6"] = ["0"],
        ["*UsoIPv4"] = ["1"],
        ["*UsoIPv6"] = ["1"],
        ["*WakeOnPattern"] = ["0"],
        ["*InterruptModeration"] = ["0", "Disabled"],
        ["*ReceiveBuffers"] = ["4096", "2048", "1024", "512"],
        ["*TransmitBuffers"] = ["4096", "2048", "1024", "512"],
        ["*FlowControl"] = ["0", "Disabled"],
        ["*EnergyEfficientEthernet"] = ["0", "Disabled"],
        ["GreenEthernet"] = ["0", "Disabled"],
        ["AutoDisableGigabit"] = ["0", "Disabled"],
        ["TransmitPower"] = ["5", "4", "Highest"],
        ["PreferredBand"] = ["3", "2", "Prefer 5GHz band"],
        ["UapsdWmmCapable"] = ["0", "Disabled"],
        ["*ArpOffload"] = ["1", "2"],
        ["*NSOffload"] = ["1", "2"],
        ["*IPChecksumOffloadIPv4"] = ["3"],
        ["*TCPChecksumOffloadIPv4"] = ["3"],
        ["*TCPChecksumOffloadIPv6"] = ["3"],
        ["*UDPChecksumOffloadIPv4"] = ["3"],
        ["*UDPChecksumOffloadIPv6"] = ["3"],
        ["EnableWlanLowLatency"] = ["1"],
        ["ScanWhenAssociated"] = ["0"],
        ["DisabledComponents"] = ["32"],
        ["IPv6TeredoState"] = ["disabled"],
        ["IPv6IsatapState"] = ["disabled"],
        ["IPv66to4State"] = ["disabled"],
        ["TcpNoDelayIPv6"] = ["1"],
        ["TcpAckFrequencyIPv6"] = ["1"],
        ["TcpHeuristics"] = ["disabled"],
        ["CongestionProvider"] = ["ctcp", "bbr"],
        ["AutoTuningLevel"] = ["normal"],
        ["TCPRss"] = ["enabled"],
        ["TCPRsc"] = ["disabled"],
        ["TCPTimestamps"] = ["disabled"],
        ["TCPEcnCapability"] = ["disabled"],
        ["NetshIPv6Randomize"] = ["disabled"],
        ["MaxNegativeCacheTtl"] = ["0"],
        ["CacheHashTableBucketSize"] = ["4"],
        ["*RSS"] = ["1"],
        ["*NumRssQueues"] = ["4"],
        ["*RssBaseProcNumber"] = ["0"],
        ["*MaxRssProcessors"] = ["4"],
        ["*LsoV1IPv4"] = ["0"],
        ["*LsoV2IPv4"] = ["0"],
        ["*LsoV2IPv6"] = ["0"],
        ["RoamAggressiveness"] = ["1"],
        ["RoamingAggressiveness"] = ["1", "2"],
        ["MIMOPowerSaveMode"] = ["0"],
        ["ThroughputBoosterEnabled"] = ["1"],
        ["2.4GHzChannelWidth"] = ["20"],
        ["ChannelWidth24"] = ["20"],
        ["5GHzChannelWidth"] = ["80"],
        ["ChannelWidth5"] = ["80"],
        ["*JumboPacket"] = ["1514"],
        ["NdisAffinity"] = ["0"],
        ["RoamThreshold"] = ["70"],
        ["EnableAutoDnsOverHttps"] = ["2"]
    };

    private static readonly HashSet<string> DeprecatedParameterKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "TCPChimney",
        "NetDMA",
        "EnableTCPA",
        "EnableDCA",
        "TCPWindowSize",
        "GlobalMaxTcpWindowSize",
        "DefaultTTL",
        "MaxUserPort",
        "TcpTimedWaitDelay",
        "LargeSystemCache",
        "MaxFreeTcbs",
        "EnableFastRouteLookup",
        "IRPStackSize",
        "MaxConnectionsPerServer",
        "MaxConnectionsPer1_0Server",
        "TcpCreateAndConnectDataUnchecked"
    };

    private static readonly HashSet<string> ThroughputClassKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "*RscIPv4",
        "*RscIPv6",
        "*UsoIPv4",
        "*UsoIPv6",
        "*LsoV1IPv4",
        "*LsoV2IPv4",
        "*LsoV2IPv6",
        "*JumboPacket",
        "*ReceiveBuffers",
        "*TransmitBuffers",
        "*RSS",
        "*NumRssQueues",
        "*MaxRssProcessors",
        "*RssBaseProcNumber",
        "AutoTuningLevel",
        "CongestionProvider",
        "TCPRss",
        "TCPRsc",
        "MTU",
        "*FlowControl",
        "*IPChecksumOffloadIPv4",
        "*TCPChecksumOffloadIPv4",
        "*TCPChecksumOffloadIPv6",
        "*UDPChecksumOffloadIPv4",
        "*UDPChecksumOffloadIPv6",
        "WirelessMode",
        "5GHzChannelWidth",
        "ChannelWidth5",
        "2.4GHzChannelWidth",
        "ChannelWidth24",
        "ThroughputBoosterEnabled"
    };

    private static readonly HashSet<string> EnergyClassKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "*EnergyEfficientEthernet",
        "GreenEthernet",
        "EEE",
        "MIMOPowerSaveMode",
        "MimoPowerSaveMode",
        "RoamAggressiveness",
        "RoamingAggressiveness",
        "RoamThreshold",
        "UapsdWmmCapable",
        "TransmitPower",
        "AutoDisableGigabit",
        "*WakeOnPattern",
        "*WakeOnMagicPacket",
        "ScanWhenAssociated",
        "PowerSaveMode"
    };

    private sealed class LatencyProbeResult
    {
        public double MedianMs { get; init; }
        public double P95Ms { get; init; }
        public double JitterMs { get; init; }
        public double LossPercent { get; init; }
        public int Samples { get; init; }
    }

    private sealed class ExperimentOutcome
    {
        public ExperimentOutcome(string defaultValue, NetworkMetricReport defaultMetrics, double defaultScore)
        {
            BestValue = defaultValue;
            BestMetrics = defaultMetrics;
            BestScore = defaultScore;
        }

        public bool Improved { get; set; }
        public string BestValue { get; set; }
        public NetworkMetricReport BestMetrics { get; set; }
        public double BestScore { get; set; }
        public string DecisionReason { get; set; } = string.Empty;
        public string AcceptedLog { get; set; } = string.Empty;
        public string KeptLog { get; set; } = string.Empty;
        public int TestedCount { get; set; }
    }

    private readonly INetworkDiscoveryService _discoveryService;
    private readonly INetworkSafetyService _safetyService;
    private readonly INetworkSnapshotService _snapshotService;
    private readonly INetworkBenchmarkService _benchmarkService;
    private readonly INetworkScoringService _scoringService;
    private readonly INetworkWatchdogService _watchdogService;
    private readonly INetworkMemoryService _memoryService;
    private readonly INetworkDiagnosticsService _diagnosticsService;

    private CancellationTokenSource? _activeCts;
    private readonly object _stateLock = new();

    public EngineState CurrentState { get; private set; } = EngineState.Idle;
    public EngineProgressInfo CurrentProgress { get; } = new();

    public event Action<EngineProgressInfo>? OnProgressUpdated;
    public event Action<CognitiveLogEntry>? OnLogEntryAdded;

    public NetworkExperimentEngine(
        INetworkDiscoveryService discoveryService,
        INetworkSafetyService safetyService,
        INetworkSnapshotService snapshotService,
        INetworkBenchmarkService benchmarkService,
        INetworkScoringService scoringService,
        INetworkWatchdogService watchdogService,
        INetworkMemoryService memoryService,
        INetworkDiagnosticsService diagnosticsService)
    {
        _discoveryService = discoveryService;
        _safetyService = safetyService;
        _snapshotService = snapshotService;
        _benchmarkService = benchmarkService;
        _scoringService = scoringService;
        _watchdogService = watchdogService;
        _memoryService = memoryService;
        _diagnosticsService = diagnosticsService;
    }

    public async Task<string> RunAdaptiveOptimizationSessionAsync(
        string? targetAdapterId = null,
        OptimizationProfile profile = OptimizationProfile.Gaming,
        EngineExecutionMode mode = EngineExecutionMode.Auto,
        int samplesPerTest = 10,
        CancellationToken ct = default)
    {
        samplesPerTest = mode == EngineExecutionMode.Expert ? 40 : 10;
        lock (_stateLock)
        {
            if (CurrentState == EngineState.Running)
            {
                throw new InvalidOperationException("Une session d'expérimentation réseau est déjà en cours.");
            }
            CurrentState = EngineState.Running;
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        CancellationToken token = _activeCts.Token;
        CurrentProgress.CompletedExperiments.Clear();
        CurrentProgress.OverallProgress = 0;
        var sessionStopwatch = Stopwatch.StartNew();
        TimeSpan sessionBudget = mode == EngineExecutionMode.Expert ? TimeSpan.FromMinutes(90) : TimeSpan.FromMinutes(45);

        try
        {
            SetPhase(EnginePhase.Discovery, "Découverte et identification du matériel réseau physique...", 5);
            IReadOnlyList<NetworkAdapterHardwareDetails> adapters = await _discoveryService.DiscoverPhysicalAdaptersAsync(token).ConfigureAwait(false);
            if (adapters.Count == 0)
            {
                throw new InvalidOperationException("Aucun adaptateur réseau physique actif (Ethernet ou Wi-Fi) n'a été détecté.");
            }

            NetworkAdapterHardwareDetails targetAdapter = !string.IsNullOrEmpty(targetAdapterId)
                ? adapters.FirstOrDefault(a => string.Equals(a.Id, targetAdapterId, StringComparison.OrdinalIgnoreCase)) ?? adapters[0]
                : adapters[0];

            Log(EnginePhase.Discovery, $"Adaptateur ciblé : {targetAdapter.Name} ({targetAdapter.Description})", "SUCCESS");
            if (targetAdapter.IsWireless)
            {
                Log(EnginePhase.Discovery, $"Wi-Fi détecté : SSID '{targetAdapter.WifiSsid ?? "N/A"}', Canal {targetAdapter.WifiChannel}, Signal {targetAdapter.WifiRssi}%", "INFO");
            }

            SetPhase(EnginePhase.SafetyAudit, "Classification de sécurité et découverte des plages du pilote...", 10);
            foreach (DynamicNetworkParameter param in targetAdapter.DiscoveredParameters)
            {
                _safetyService.ClassifyParameter(param);
            }

            var allowedParams = targetAdapter.DiscoveredParameters
                .Where(p => _safetyService.IsAllowedInMode(p, mode))
                .ToList();

            Log(EnginePhase.SafetyAudit, $"{targetAdapter.DiscoveredParameters.Count} paramètres découverts dans le pilote NDIS. {allowedParams.Count} autorisés en mode {mode}.", "INFO");

            int deprecatedSkipped = allowedParams.RemoveAll(p => DeprecatedParameterKeywords.Contains(p.RegistryKeyword));
            if (deprecatedSkipped > 0)
            {
                Log(EnginePhase.SafetyAudit, $"{deprecatedSkipped} paramètres obsolètes ou sans effet documenté sous Windows 10/11 ignorés (TCP Chimney, NetDMA, TCPWindowSize, DefaultTTL, MaxUserPort, TcpTimedWaitDelay...).", "INFO");
            }
            allowedParams = allowedParams.OrderBy(p => ParameterPriorityRank(p.RegistryKeyword)).ToList();

            SetPhase(EnginePhase.InitialSnapshot, "Création du snapshot de sauvegarde initial (Microsoft / Pilote)...", 15);
            NetworkConfigurationSnapshot baselineSnapshot = await _snapshotService.CaptureSnapshotAsync(targetAdapter.Id, targetAdapter.Name, "Baseline_Initial", token).ConfigureAwait(false);
            Log(EnginePhase.InitialSnapshot, $"Snapshot initial créé avec succès (ID: {baselineSnapshot.SnapshotId[..8]}).", "SUCCESS");

            SetPhase(EnginePhase.BaselineBenchmark, $"Mesure statistique rigoureuse de l'état initial (Baseline — {samplesPerTest} rounds interleaved × 4 dimensions)...", 20);
            NetworkMetricReport baselineMetrics = await _benchmarkService.RunFourPrecisionTestsAsync(
                targetAdapter.GatewayAddress,
                samplesPerTest,
                onSampleProgress: (testIdx, testName, sampleIdx, totalSamples, liveVal) =>
                {
                    CurrentProgress.CurrentTestIndex = testIdx;
                    CurrentProgress.CurrentTestName = $"[Baseline] {testName}";
                    CurrentProgress.CurrentSampleIndex = sampleIdx;
                    CurrentProgress.TotalSamplesPerTest = totalSamples;
                    if (testIdx == 1)
                    {
                        CurrentProgress.LivePrecisionLatencyMs = liveVal;
                    }
                    else if (testIdx == 2)
                    {
                        CurrentProgress.LivePrecisionJitterMs = liveVal;
                    }
                    else if (testIdx == 3)
                    {
                        CurrentProgress.LivePrecisionThroughputMbps = liveVal;
                    }
                    else if (testIdx == 4)
                    {
                        CurrentProgress.LivePrecisionBufferbloatMs = liveVal;
                    }

                    OnProgressUpdated?.Invoke(CurrentProgress);
                },
                token).ConfigureAwait(false);
            baselineMetrics.CompositeScore = _scoringService.ComputeCompositeScore(baselineMetrics, profile);
            baselineSnapshot.MetricsAtSnapshot = baselineMetrics;
            CurrentProgress.BaselineScore = baselineMetrics.CompositeScore;
            CurrentProgress.BaselineLatencyMs = baselineMetrics.LatencyMedianMs;
            CurrentProgress.BaselineJitterMs = baselineMetrics.JitterMs;
            CurrentProgress.BaselineThroughputMbps = baselineMetrics.ThroughputMbps;
            CurrentProgress.BaselineBufferbloatMs = baselineMetrics.BufferbloatIncreaseMs;
            CurrentProgress.CurrentScore = baselineMetrics.CompositeScore;
            CurrentProgress.CurrentLatencyMs = baselineMetrics.LatencyMedianMs;
            CurrentProgress.CurrentJitterMs = baselineMetrics.JitterMs;
            CurrentProgress.CurrentThroughputMbps = baselineMetrics.ThroughputMbps;
            CurrentProgress.CurrentPacketLoss = baselineMetrics.PacketLossPercent;
            CurrentProgress.CurrentBufferbloatGrade = baselineMetrics.BufferbloatGrade;

            Log(EnginePhase.BaselineBenchmark,
                $"Baseline établie : Ping {baselineMetrics.LatencyMedianMs:F1}ms (±{baselineMetrics.JitterMs:F1}ms), Débit {baselineMetrics.ThroughputMbps:F1} Mbps, Bufferbloat {baselineMetrics.BufferbloatGrade}. Score: {baselineMetrics.CompositeScore:F1}/100.", "SUCCESS");

            string fingerprint = $"{targetAdapter.MatchingDeviceId}_{targetAdapter.DriverVersion}".Trim('_');
            HardwareKnowledgeProfile? knownProfile = _memoryService.GetProfile(fingerprint);
            if (knownProfile != null && knownProfile.ProvenBestSettings.Count > 0)
            {
                Log(EnginePhase.BaselineBenchmark, $"Profil mémorisé pour ce matériel : {knownProfile.ProvenBestSettings.Count} réglages déjà validés historiquement.", "INFO");
            }

            SetPhase(EnginePhase.SingleParameterExperiments, $"Démarrage des tests expérimentaux isolés ({samplesPerTest} rounds × 4 dimensions par paramètre)...", 30);

            NetworkMetricReport currentBestMetrics = baselineMetrics;
            var experimentsList = new List<ExperimentRecord>();
            int experimentCounter = 0;
            CurrentProgress.TotalExperiments = allowedParams.Count;

            if (mode == EngineExecutionMode.Simulation)
            {
                Log(EnginePhase.SingleParameterExperiments, "Mode SIMULATION : aucune mesure réelle — les métriques affichées sont calculées pour la démonstration, aucune valeur n'est écrite dans le registre.", "WARN");
            }

            _watchdogService.StartMonitoring(targetAdapter.Id, targetAdapter.GatewayAddress, async (reason) =>
            {
                Log(EnginePhase.Rollback, $"WATCHDOG INTERVENTION : {reason}. Rollback d'urgence immédiat en cours...", "ERROR");
                await RequestEmergencyRollbackAsync().ConfigureAwait(false);
            });

            try
            {
                foreach (DynamicNetworkParameter param in allowedParams)
                {
                    token.ThrowIfCancellationRequested();
                    if (mode != EngineExecutionMode.Simulation && sessionStopwatch.Elapsed >= sessionBudget)
                    {
                        Log(EnginePhase.SingleParameterExperiments, $"Budget temps de session atteint ({(int)sessionBudget.TotalMinutes} min). {allowedParams.Count - experimentCounter} paramètres restants reportés au prochain cycle (la mémoire matérielle conserve les résultats déjà validés).", "WARN");
                        break;
                    }
                    experimentCounter++;
                    CurrentProgress.CurrentExperiment = experimentCounter;
                    CurrentProgress.CurrentTestedParameter = param.DisplayName;
                    CurrentProgress.CurrentTestedParamExplanation = !string.IsNullOrEmpty(param.DetailedExplanation) ? param.DetailedExplanation : _safetyService.GetExplanationForParameter(param);
                    CurrentProgress.CurrentTestedParamCategory = param.Category.ToString();
                    CurrentProgress.CurrentTestedParamScope = param.Scope;
                    double phaseProgress = 30.0 + (experimentCounter / (double)allowedParams.Count * 50.0);
                    UpdateOverallProgress(phaseProgress);

                    int targetValuesCount = (mode == EngineExecutionMode.Expert) ? 20 : 10;
                    List<string> candidateValues = DetermineCandidateValues(param, mode, targetValuesCount);
                    Log(EnginePhase.SingleParameterExperiments,
                        $"Paramètre #{experimentCounter:00}/{allowedParams.Count:00} : {param.DisplayName} [{param.CurrentValue}] — Évaluation de {candidateValues.Count} valeurs candidates ({samplesPerTest} rounds × 4 dimensions)...", "EXPERIMENT");

                    string bestCandidateValue = param.CurrentValue;
                    double bestScore = currentBestMetrics.CompositeScore;
                    NetworkMetricReport bestCandidateMetrics = currentBestMetrics;
                    bool improved = false;
                    string decisionReason;
                    string acceptedLog;
                    string keptLog;
                    bool isThroughputClass = IsThroughputClassParameter(param.RegistryKeyword);

                    if (mode == EngineExecutionMode.Simulation)
                    {
                        Log(EnginePhase.SingleParameterExperiments,
                            isThroughputClass
                                ? "Classe débit : test complet simulé appliqué à chaque valeur candidate."
                                : "Classe latence : sondage A/B/A simulé appliqué à chaque valeur candidate.",
                            "INFO");

                        for (int vi = 0; vi < candidateValues.Count; vi++)
                        {
                            token.ThrowIfCancellationRequested();
                            string candVal = candidateValues[vi];
                            CurrentProgress.CurrentTestedValue = candVal;
                            CurrentProgress.PreviousTestedValue = param.CurrentValue!;

                            if (string.IsNullOrEmpty(candVal) || string.Equals(candVal, param.CurrentValue, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!_safetyService.ValidateProposedValue(param, candVal, out _))
                            {
                                continue;
                            }

                            double simLatency = Math.Max(2.0, baselineMetrics.LatencyMedianMs * (0.85 + (vi % 7 * 0.03)));
                            double simJitter = Math.Max(0.1, baselineMetrics.JitterMs * (0.80 + (vi % 5 * 0.04)));
                            double simThroughput = baselineMetrics.ThroughputMbps * (1.05 + (vi % 6 * 0.03));
                            double simBufferbloat = Math.Max(0.0, baselineMetrics.BufferbloatIncreaseMs * 0.7);

                            for (int s = 1; s <= samplesPerTest; s++)
                            {
                                token.ThrowIfCancellationRequested();
                                CurrentProgress.CurrentSampleIndex = s;
                                CurrentProgress.TotalSamplesPerTest = samplesPerTest;
                                CurrentProgress.CurrentTestIndex = (s % 4) + 1;
                                CurrentProgress.CurrentTestName = $"Test {CurrentProgress.CurrentTestIndex}/4 (Éch. {s}/{samplesPerTest} - Val {vi + 1}/{candidateValues.Count})";
                                CurrentProgress.LivePrecisionLatencyMs = simLatency + (Math.Sin(s + vi) * 0.15);
                                CurrentProgress.LivePrecisionJitterMs = simJitter;
                                CurrentProgress.LivePrecisionThroughputMbps = simThroughput;
                                CurrentProgress.LivePrecisionBufferbloatMs = simBufferbloat;
                                OnProgressUpdated?.Invoke(CurrentProgress);
                                await Task.Delay(mode == EngineExecutionMode.Expert ? 2 : 5, token).ConfigureAwait(false);
                            }

                            var candMetrics = new NetworkMetricReport
                            {
                                LatencyMedianMs = simLatency,
                                JitterMs = simJitter,
                                ThroughputMbps = simThroughput,
                                BufferbloatLatencyUnderLoadMs = simLatency + simBufferbloat,
                                BufferbloatGrade = simBufferbloat < 5 ? "A+" : "A",
                                PacketLossPercent = 0
                            };
                            candMetrics.CompositeScore = _scoringService.ComputeCompositeScore(candMetrics, profile);

                            if (candMetrics.CompositeScore > bestScore)
                            {
                                bestScore = candMetrics.CompositeScore;
                                bestCandidateValue = candVal;
                                bestCandidateMetrics = candMetrics;
                                improved = true;
                            }
                        }

                        decisionReason = improved
                            ? $"Meilleure valeur validée parmi {candidateValues.Count} configurations ({samplesPerTest} rounds × 4 dimensions). Gain composite: +{bestScore - currentBestMetrics.CompositeScore:F1} pts."
                            : $"Valeur initiale maintenue après analyse de {candidateValues.Count} configurations ({samplesPerTest} rounds × 4 dimensions). Déjà à l'état optimal.";
                        acceptedLog = $"VALIDÉ : {param.DisplayName} [{param.CurrentValue} -> {bestCandidateValue}] parmi {candidateValues.Count} valeurs ({samplesPerTest} rounds × 4 dimensions). Score: {bestScore:F1}/100 (+{bestScore - baselineMetrics.CompositeScore:F1} pts).";
                        keptLog = $"MAINTENU : {param.DisplayName} déjà optimal après {candidateValues.Count} configurations ({samplesPerTest} rounds × 4 dimensions).";
                    }
                    else
                    {
                        ExperimentOutcome outcome = isThroughputClass
                            ? await RunThroughputClassExperimentAsync(targetAdapter, param, candidateValues, baselineMetrics, currentBestMetrics, profile, mode, token).ConfigureAwait(false)
                            : await RunLatencyClassExperimentAsync(targetAdapter, param, candidateValues, currentBestMetrics, profile, mode, token).ConfigureAwait(false);
                        improved = outcome.Improved;
                        bestCandidateValue = outcome.BestValue;
                        bestCandidateMetrics = outcome.BestMetrics;
                        bestScore = outcome.BestScore;
                        decisionReason = outcome.DecisionReason;
                        acceptedLog = outcome.AcceptedLog;
                        keptLog = outcome.KeptLog;
                    }

                    if (improved && mode != EngineExecutionMode.Simulation)
                    {
                        _ = await ApplyDriverRegistryValueAsync(targetAdapter.Id, param.RegistryKeyword, bestCandidateValue, token).ConfigureAwait(false);
                    }

                    var expRecord = new ExperimentRecord
                    {
                        ExperimentIndex = experimentCounter,
                        ParameterKeyword = param.RegistryKeyword,
                        ParameterDisplayName = param.DisplayName,
                        PreviousValue = param.CurrentValue,
                        TestedValue = bestCandidateValue,
                        SafetyLevel = param.SafetyLevel,
                        ParameterScope = param.Scope,
                        ParameterCategoryName = param.Category.ToString(),
                        ParameterExplanation = !string.IsNullOrEmpty(param.DetailedExplanation) ? param.DetailedExplanation : _safetyService.GetExplanationForParameter(param),
                        MetricsBefore = currentBestMetrics,
                        MetricsAfter = improved ? bestCandidateMetrics : currentBestMetrics,
                        Decision = improved ? ExperimentDecision.Accepted : ExperimentDecision.KeptBaseline,
                        DecisionReason = decisionReason,
                        SamplesTestedCount = samplesPerTest
                    };

                    if (improved)
                    {
                        currentBestMetrics = bestCandidateMetrics;
                        param.CurrentValue = bestCandidateValue;
                        CurrentProgress.CurrentScore = bestCandidateMetrics.CompositeScore;
                        CurrentProgress.CurrentLatencyMs = bestCandidateMetrics.LatencyMedianMs;
                        CurrentProgress.CurrentJitterMs = bestCandidateMetrics.JitterMs;
                        CurrentProgress.CurrentThroughputMbps = bestCandidateMetrics.ThroughputMbps;
                        CurrentProgress.CurrentPacketLoss = bestCandidateMetrics.PacketLossPercent;
                        CurrentProgress.CurrentBufferbloatGrade = bestCandidateMetrics.BufferbloatGrade;
                        Log(EnginePhase.SingleParameterExperiments, acceptedLog, "SUCCESS");
                        await _memoryService.SaveProvenBestSettingAsync(fingerprint, param.RegistryKeyword, bestCandidateValue, bestCandidateMetrics.CompositeScore, token).ConfigureAwait(false);
                    }
                    else
                    {
                        Log(EnginePhase.SingleParameterExperiments, keptLog, "INFO");
                    }

                    experimentsList.Add(expRecord);
                    CurrentProgress.CompletedExperiments.Add(expRecord);
                    await _memoryService.RecordExperimentAsync(fingerprint, targetAdapter.Description, targetAdapter.DriverVersion, expRecord, token).ConfigureAwait(false);
                }
            }
            finally
            {
                _watchdogService.StopMonitoring();
            }

            SetPhase(EnginePhase.ValidationVerification, $"Validation finale ({samplesPerTest} rounds × 4 dimensions)...", 85);
            NetworkMetricReport finalMetrics = await _benchmarkService.RunFourPrecisionTestsAsync(
                targetAdapter.GatewayAddress,
                samplesPerTest,
                onSampleProgress: (testIdx, testName, sampleIdx, totalSamples, liveVal) =>
                {
                    CurrentProgress.CurrentTestIndex = testIdx;
                    CurrentProgress.CurrentTestName = $"[Validation] {testName}";
                    CurrentProgress.CurrentSampleIndex = sampleIdx;
                    CurrentProgress.TotalSamplesPerTest = totalSamples;
                    if (testIdx == 1)
                    {
                        CurrentProgress.LivePrecisionLatencyMs = liveVal;
                    }
                    else if (testIdx == 2)
                    {
                        CurrentProgress.LivePrecisionJitterMs = liveVal;
                    }
                    else if (testIdx == 3)
                    {
                        CurrentProgress.LivePrecisionThroughputMbps = liveVal;
                    }
                    else if (testIdx == 4)
                    {
                        CurrentProgress.LivePrecisionBufferbloatMs = liveVal;
                    }

                    OnProgressUpdated?.Invoke(CurrentProgress);
                },
                token).ConfigureAwait(false);
            finalMetrics.CompositeScore = _scoringService.ComputeCompositeScore(finalMetrics, profile);
            CurrentProgress.CurrentScore = finalMetrics.CompositeScore;
            CurrentProgress.CurrentLatencyMs = finalMetrics.LatencyMedianMs;
            CurrentProgress.CurrentJitterMs = finalMetrics.JitterMs;
            CurrentProgress.CurrentThroughputMbps = finalMetrics.ThroughputMbps;
            CurrentProgress.CurrentPacketLoss = finalMetrics.PacketLossPercent;
            CurrentProgress.CurrentBufferbloatGrade = finalMetrics.BufferbloatGrade;

            SetPhase(EnginePhase.StorytellingSummary, "Génération du rapport cognitif et explicatif en langage naturel...", 95);
            string report = _diagnosticsService.GenerateStorytellingReport(
                targetAdapter,
                profile,
                baselineMetrics,
                finalMetrics,
                experimentsList);

            SetPhase(EnginePhase.Completed, "Session d'optimisation terminée avec succès !", 100);
            CurrentState = EngineState.Completed;
            Log(EnginePhase.Completed, "Cycle cognitif achevé. Toutes les configurations validées sont actives et stables.", "SUCCESS");

            return report;
        }
        catch (OperationCanceledException)
        {
            CurrentState = EngineState.Cancelled;
            SetPhase(EnginePhase.Idle, "Optimisation interrompue par l'utilisateur.", CurrentProgress.OverallProgress);
            Log(EnginePhase.Idle, "Session annulée.", "WARN");
            return "Session d'optimisation annulée par l'utilisateur.";
        }
        catch (Exception ex)
        {
            CurrentState = EngineState.Failed;
            SetPhase(EnginePhase.Failed, $"Erreur critique : {ex.Message}", CurrentProgress.OverallProgress);
            Log(EnginePhase.Failed, $"Exception critique : {ex.Message}", "ERROR");
            LoggingService.LogException(ex, "NetworkExperimentEngine.RunAdaptiveOptimizationSessionAsync");

            try
            {
                _ = await _snapshotService.RollbackToBaselineAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                LoggingService.LogException(rollbackEx, "NetworkExperimentEngine.RunAdaptiveOptimizationSessionAsync:Rollback");
            }

            throw;
        }
    }

    public Task CancelSessionAsync()
    {
        lock (_stateLock)
        {
            _activeCts?.Cancel();
        }
        return Task.CompletedTask;
    }

    public async Task RequestEmergencyRollbackAsync()
    {
        lock (_stateLock)
        {
            CurrentState = EngineState.RollingBack;
        }
        SetPhase(EnginePhase.Rollback, "Rollback d'urgence déclenché...", CurrentProgress.OverallProgress);
        _ = await _snapshotService.RollbackToBaselineAsync(CancellationToken.None).ConfigureAwait(false);
        Log(EnginePhase.Rollback, "État initial restauré avec succès.", "SUCCESS");
        _activeCts?.Cancel();
    }

    private async Task<ExperimentOutcome> RunLatencyClassExperimentAsync(
        NetworkAdapterHardwareDetails targetAdapter,
        DynamicNetworkParameter param,
        List<string> candidateValues,
        NetworkMetricReport currentBestMetrics,
        OptimizationProfile profile,
        EngineExecutionMode mode,
        CancellationToken token)
    {
        var outcome = new ExperimentOutcome(param.CurrentValue ?? string.Empty, currentBestMetrics, currentBestMetrics.CompositeScore);
        int probeScale = mode == EngineExecutionMode.Expert ? 2 : 1;
        int baselinePings = BaselineProbePings * probeScale;
        int candidatePings = CandidateProbePings * probeScale;
        bool useP95 = IsEnergyClassParameter(param.RegistryKeyword);

        Log(EnginePhase.SingleParameterExperiments, $"Classe latence : sondage A/B/A peu coûteux ({baselinePings} pings avant, {candidatePings} par valeur, {baselinePings} après), jugé sur {(useP95 ? "pics p95" : "médiane")}.", "INFO");

        LatencyProbeResult? before = await ProbeLatencyAsync(targetAdapter.GatewayAddress, baselinePings, (pingIndex, totalPings, roundtripMs) =>
        {
            CurrentProgress.CurrentTestIndex = 1;
            CurrentProgress.CurrentTestName = $"[A/B/A] Baseline avant — ping {pingIndex}/{totalPings}";
            CurrentProgress.CurrentSampleIndex = pingIndex;
            CurrentProgress.TotalSamplesPerTest = totalPings;
            if (roundtripMs >= 0)
            {
                CurrentProgress.LivePrecisionLatencyMs = roundtripMs;
                CurrentProgress.CurrentLatencyMs = roundtripMs;
            }
            OnProgressUpdated?.Invoke(CurrentProgress);
        }, token).ConfigureAwait(false);

        if (before == null)
        {
            outcome.DecisionReason = $"Paramètre non testé : la passerelle ne répond pas aux sondages ICMP ({baselinePings} pings). Valeur initiale maintenue.";
            outcome.KeptLog = $"REPORTÉ : {param.DisplayName} — passerelle ne répond pas aux sondages de latence, paramètre laissé inchangé.";
            return outcome;
        }

        var probes = new List<(string Value, LatencyProbeResult Probe)>();

        async Task ProbeCandidateAsync(string candVal)
        {
            if (string.IsNullOrEmpty(candVal) || string.Equals(candVal, param.CurrentValue, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_safetyService.ValidateProposedValue(param, candVal, out _))
            {
                return;
            }

            CurrentProgress.CurrentTestedValue = candVal;
            CurrentProgress.PreviousTestedValue = param.CurrentValue!;
            bool applied = await ApplyDriverRegistryValueAsync(targetAdapter.Id, param.RegistryKeyword, candVal, token).ConfigureAwait(false);
            if (!applied)
            {
                return;
            }

            await Task.Delay(100, token).ConfigureAwait(false);
            bool healthy = await _watchdogService.QuickHealthCheckAsync(targetAdapter.GatewayAddress, token).ConfigureAwait(false);
            if (!healthy)
            {
                _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
                return;
            }

            LatencyProbeResult? probe = await ProbeLatencyAsync(targetAdapter.GatewayAddress, candidatePings, (pingIndex, totalPings, roundtripMs) =>
            {
                CurrentProgress.CurrentTestIndex = 1;
                CurrentProgress.CurrentTestName = $"[A/B/A] Candidat {candVal} — ping {pingIndex}/{totalPings}";
                CurrentProgress.CurrentSampleIndex = pingIndex;
                CurrentProgress.TotalSamplesPerTest = totalPings;
                if (roundtripMs >= 0)
                {
                    CurrentProgress.LivePrecisionLatencyMs = roundtripMs;
                    CurrentProgress.CurrentLatencyMs = roundtripMs;
                }
                OnProgressUpdated?.Invoke(CurrentProgress);
            }, token).ConfigureAwait(false);

            if (probe == null)
            {
                _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
                return;
            }

            probes.Add((candVal, probe));
            outcome.TestedCount++;
        }

        foreach (string candVal in candidateValues)
        {
            token.ThrowIfCancellationRequested();
            await ProbeCandidateAsync(candVal).ConfigureAwait(false);
        }

        _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);

        LatencyProbeResult? after = await ProbeLatencyAsync(targetAdapter.GatewayAddress, baselinePings, (pingIndex, totalPings, roundtripMs) =>
        {
            CurrentProgress.CurrentTestIndex = 1;
            CurrentProgress.CurrentTestName = $"[A/B/A] Baseline après — ping {pingIndex}/{totalPings}";
            CurrentProgress.CurrentSampleIndex = pingIndex;
            CurrentProgress.TotalSamplesPerTest = totalPings;
            if (roundtripMs >= 0)
            {
                CurrentProgress.LivePrecisionLatencyMs = roundtripMs;
                CurrentProgress.CurrentLatencyMs = roundtripMs;
            }
            OnProgressUpdated?.Invoke(CurrentProgress);
        }, token).ConfigureAwait(false);

        if (after == null)
        {
            outcome.DecisionReason = $"Paramètre non testé : la baseline après les candidats n'a pas pu être mesurée (passerelle injoignable). Valeur initiale maintenue.";
            outcome.KeptLog = $"REPORTÉ : {param.DisplayName} — baseline post-test injoignable, paramètre laissé inchangé.";
            return outcome;
        }

        double baselineRef = (ProbeMetric(before, useP95) + ProbeMetric(after, useP95)) / 2.0;
        double noiseMs = Math.Abs(ProbeMetric(before, useP95) - ProbeMetric(after, useP95));
        double thresholdMs = Math.Max(noiseMs, useP95 ? MinimalP95GainMs : MinimalLatencyGainMs);
        double baselineLoss = (before.LossPercent + after.LossPercent) / 2.0;

        string bestValue = param.CurrentValue ?? string.Empty;
        LatencyProbeResult? bestProbe = null;
        double bestMetric = double.MaxValue;

        void SelectBestProbe()
        {
            bestValue = param.CurrentValue ?? string.Empty;
            bestProbe = null;
            bestMetric = double.MaxValue;
            foreach ((string value, LatencyProbeResult probe) in probes)
            {
                if (probe.LossPercent > baselineLoss + 0.5)
                {
                    continue;
                }

                double metric = ProbeMetric(probe, useP95);
                if (metric < bestMetric)
                {
                    bestMetric = metric;
                    bestValue = value;
                    bestProbe = probe;
                }
            }
        }

        SelectBestProbe();

        List<string> refinedValues = bestProbe == null ? new List<string>() : RefineCandidatesAroundBest(param, probes.Select(p => p.Value).ToList(), bestValue);
        if (refinedValues.Count > 0)
        {
            Log(EnginePhase.SingleParameterExperiments, $"Raffinement de la recherche d'optimum autour de {bestValue} : {refinedValues.Count} valeurs intermédiaires supplémentaires.", "INFO");
            foreach (string refinedVal in refinedValues)
            {
                token.ThrowIfCancellationRequested();
                await ProbeCandidateAsync(refinedVal).ConfigureAwait(false);
            }
            _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
            SelectBestProbe();
        }

        if (bestProbe != null && bestMetric < baselineRef - thresholdMs)
        {
            bool applied = await ApplyDriverRegistryValueAsync(targetAdapter.Id, param.RegistryKeyword, bestValue, token).ConfigureAwait(false);
            if (applied)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
                bool healthy = await _watchdogService.QuickHealthCheckAsync(targetAdapter.GatewayAddress, token).ConfigureAwait(false);
                if (healthy)
                {
                    LatencyProbeResult? confirm = await ProbeLatencyAsync(targetAdapter.GatewayAddress, candidatePings, (pingIndex, totalPings, roundtripMs) =>
                    {
                        CurrentProgress.CurrentTestIndex = 1;
                        CurrentProgress.CurrentTestName = $"[A/B/A] Confirmation {bestValue} — ping {pingIndex}/{totalPings}";
                        CurrentProgress.CurrentSampleIndex = pingIndex;
                        CurrentProgress.TotalSamplesPerTest = totalPings;
                        if (roundtripMs >= 0)
                        {
                            CurrentProgress.LivePrecisionLatencyMs = roundtripMs;
                            CurrentProgress.CurrentLatencyMs = roundtripMs;
                        }
                        OnProgressUpdated?.Invoke(CurrentProgress);
                    }, token).ConfigureAwait(false);

                    double confirmMetric = confirm == null ? double.MaxValue : ProbeMetric(confirm, useP95);
                    if (confirm != null && confirmMetric < baselineRef - thresholdMs && confirm.LossPercent <= baselineLoss + 0.5)
                    {
                        var acceptedReport = new NetworkMetricReport
                        {
                            LatencyMedianMs = confirm.MedianMs,
                            JitterMs = confirm.JitterMs,
                            PacketLossPercent = confirm.LossPercent,
                            ThroughputMbps = currentBestMetrics.ThroughputMbps,
                            BufferbloatLatencyUnderLoadMs = currentBestMetrics.BufferbloatLatencyUnderLoadMs,
                            BufferbloatGrade = currentBestMetrics.BufferbloatGrade
                        };
                        acceptedReport.CompositeScore = _scoringService.ComputeCompositeScore(acceptedReport, profile);

                        outcome.Improved = true;
                        outcome.BestValue = bestValue;
                        outcome.BestMetrics = acceptedReport;
                        outcome.BestScore = acceptedReport.CompositeScore;
                        outcome.DecisionReason = $"Gain de {(useP95 ? "pics p95" : "latence médiane")} mesuré : {baselineRef - confirmMetric:F1} ms au-delà du bruit (±{thresholdMs:F1} ms), validé par A/B/A ({before.Samples + after.Samples} pings de baseline entrelacés) et confirmé par re-mesure.";
                        outcome.AcceptedLog = $"VALIDÉ : {param.DisplayName} [{param.CurrentValue} -> {bestValue}] — gain {(useP95 ? "pics p95" : "latence")} {baselineRef - confirmMetric:F1} ms au-delà du bruit (±{thresholdMs:F1} ms), confirmé par re-mesure. Score: {acceptedReport.CompositeScore:F1}/100.";
                        return outcome;
                    }
                }

                _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
                outcome.DecisionReason = $"Gain observé ({baselineRef - bestMetric:F1} ms) non confirmé par la re-mesure au-delà du bruit (±{thresholdMs:F1} ms). Valeur initiale maintenue.";
                outcome.KeptLog = $"MAINTENU : {param.DisplayName} — gain observé non confirmé par re-mesure (bruit ±{thresholdMs:F1} ms).";
                return outcome;
            }
        }

        outcome.DecisionReason = $"Aucun effet mesurable au-delà du bruit (±{thresholdMs:F1} ms) après analyse de {outcome.TestedCount} valeurs ({baselinePings} pings avant, {candidatePings} par valeur, {baselinePings} après).";
        outcome.KeptLog = $"MAINTENU : {param.DisplayName} — aucun effet mesurable au-delà du bruit (±{thresholdMs:F1} ms) après {outcome.TestedCount} valeurs testées.";
        return outcome;
    }

    private async Task<ExperimentOutcome> RunThroughputClassExperimentAsync(
        NetworkAdapterHardwareDetails targetAdapter,
        DynamicNetworkParameter param,
        List<string> candidateValues,
        NetworkMetricReport baselineMetrics,
        NetworkMetricReport currentBestMetrics,
        OptimizationProfile profile,
        EngineExecutionMode mode,
        CancellationToken token)
    {
        var outcome = new ExperimentOutcome(param.CurrentValue ?? string.Empty, currentBestMetrics, currentBestMetrics.CompositeScore);
        int throughputSamples = mode == EngineExecutionMode.Expert ? 8 : 5;

        Log(EnginePhase.SingleParameterExperiments, $"Classe débit : test complet 4 dimensions appliqué à chaque valeur ({throughputSamples} rounds, gain composite minimal requis : +{MinimalScoreGain:F1} pt).", "INFO");

        string bestValue = param.CurrentValue ?? string.Empty;
        double bestScore = currentBestMetrics.CompositeScore;

        async Task TestValueAsync(string candVal)
        {
            if (string.IsNullOrEmpty(candVal) || string.Equals(candVal, param.CurrentValue, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_safetyService.ValidateProposedValue(param, candVal, out _))
            {
                return;
            }

            CurrentProgress.CurrentTestedValue = candVal;
            CurrentProgress.PreviousTestedValue = param.CurrentValue!;
            bool applied = await ApplyDriverRegistryValueAsync(targetAdapter.Id, param.RegistryKeyword, candVal, token).ConfigureAwait(false);
            if (!applied)
            {
                return;
            }

            await Task.Delay(100, token).ConfigureAwait(false);
            bool healthy = await _watchdogService.QuickHealthCheckAsync(targetAdapter.GatewayAddress, token).ConfigureAwait(false);
            if (!healthy)
            {
                _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
                return;
            }

            NetworkMetricReport candidateMetrics = await _benchmarkService.RunFourPrecisionTestsAsync(
                targetAdapter.GatewayAddress,
                throughputSamples,
                onSampleProgress: (testIdx, testName, sampleIdx, totalSamples, liveVal) =>
                {
                    CurrentProgress.CurrentTestIndex = testIdx;
                    CurrentProgress.CurrentTestName = testName;
                    CurrentProgress.CurrentSampleIndex = sampleIdx;
                    CurrentProgress.TotalSamplesPerTest = totalSamples;
                    if (testIdx == 1)
                    {
                        CurrentProgress.LivePrecisionLatencyMs = liveVal;
                        CurrentProgress.CurrentLatencyMs = liveVal;
                    }
                    else if (testIdx == 2)
                    {
                        CurrentProgress.LivePrecisionJitterMs = liveVal;
                        CurrentProgress.CurrentJitterMs = liveVal;
                    }
                    else if (testIdx == 3)
                    {
                        CurrentProgress.LivePrecisionThroughputMbps = liveVal;
                        CurrentProgress.CurrentThroughputMbps = liveVal;
                    }
                    else if (testIdx == 4)
                    {
                        CurrentProgress.LivePrecisionBufferbloatMs = liveVal;
                    }

                    OnProgressUpdated?.Invoke(CurrentProgress);
                },
                token).ConfigureAwait(false);

            candidateMetrics.CompositeScore = _scoringService.ComputeCompositeScore(candidateMetrics, profile);
            outcome.TestedCount++;

            if (candidateMetrics.CompositeScore > bestScore + MinimalScoreGain && candidateMetrics.PacketLossPercent <= baselineMetrics.PacketLossPercent + 0.5)
            {
                bestScore = candidateMetrics.CompositeScore;
                bestValue = candVal;
            }
            else
            {
                _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
            }
        }

        foreach (string candVal in candidateValues)
        {
            token.ThrowIfCancellationRequested();
            await TestValueAsync(candVal).ConfigureAwait(false);
        }

        List<string> refinedValues = RefineCandidatesAroundBest(param, candidateValues, bestValue);
        if (refinedValues.Count > 0)
        {
            Log(EnginePhase.SingleParameterExperiments, $"Raffinement de la recherche d'optimum autour de {bestValue} : {refinedValues.Count} valeurs intermédiaires supplémentaires.", "INFO");
            foreach (string refinedVal in refinedValues)
            {
                token.ThrowIfCancellationRequested();
                await TestValueAsync(refinedVal).ConfigureAwait(false);
            }
        }

        if (!string.Equals(bestValue, param.CurrentValue ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            bool applied = await ApplyDriverRegistryValueAsync(targetAdapter.Id, param.RegistryKeyword, bestValue, token).ConfigureAwait(false);
            if (applied)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
                NetworkMetricReport confirmMetrics = await _benchmarkService.RunFourPrecisionTestsAsync(
                    targetAdapter.GatewayAddress,
                    throughputSamples,
                    onSampleProgress: (testIdx, testName, sampleIdx, totalSamples, liveVal) =>
                    {
                        CurrentProgress.CurrentTestIndex = testIdx;
                        CurrentProgress.CurrentTestName = $"[Confirmation] {testName}";
                        CurrentProgress.CurrentSampleIndex = sampleIdx;
                        CurrentProgress.TotalSamplesPerTest = totalSamples;
                        if (testIdx == 1)
                        {
                            CurrentProgress.LivePrecisionLatencyMs = liveVal;
                            CurrentProgress.CurrentLatencyMs = liveVal;
                        }
                        else if (testIdx == 2)
                        {
                            CurrentProgress.LivePrecisionJitterMs = liveVal;
                            CurrentProgress.CurrentJitterMs = liveVal;
                        }
                        else if (testIdx == 3)
                        {
                            CurrentProgress.LivePrecisionThroughputMbps = liveVal;
                            CurrentProgress.CurrentThroughputMbps = liveVal;
                        }
                        else if (testIdx == 4)
                        {
                            CurrentProgress.LivePrecisionBufferbloatMs = liveVal;
                        }

                        OnProgressUpdated?.Invoke(CurrentProgress);
                    },
                    token).ConfigureAwait(false);

                confirmMetrics.CompositeScore = _scoringService.ComputeCompositeScore(confirmMetrics, profile);

                if (confirmMetrics.CompositeScore > currentBestMetrics.CompositeScore + MinimalScoreGain && confirmMetrics.PacketLossPercent <= baselineMetrics.PacketLossPercent + 0.5)
                {
                    outcome.Improved = true;
                    outcome.BestValue = bestValue;
                    outcome.BestMetrics = confirmMetrics;
                    outcome.BestScore = confirmMetrics.CompositeScore;
                    outcome.DecisionReason = $"Meilleure valeur validée parmi {outcome.TestedCount} configurations ({throughputSamples} rounds × 4 dimensions) puis confirmée par re-mesure. Gain composite: +{confirmMetrics.CompositeScore - currentBestMetrics.CompositeScore:F1} pts.";
                    outcome.AcceptedLog = $"VALIDÉ : {param.DisplayName} [{param.CurrentValue} -> {bestValue}] parmi {outcome.TestedCount} valeurs ({throughputSamples} rounds × 4 dimensions), confirmé par re-mesure. Score: {confirmMetrics.CompositeScore:F1}/100 (+{confirmMetrics.CompositeScore - baselineMetrics.CompositeScore:F1} pts).";
                    return outcome;
                }

                _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
                outcome.DecisionReason = $"Gain observé non confirmé par la re-mesure (score de confirmation {confirmMetrics.CompositeScore:F1} sous le seuil de bruit +{MinimalScoreGain:F1} pt). Valeur initiale maintenue.";
                outcome.KeptLog = $"MAINTENU : {param.DisplayName} — gain observé non confirmé par re-mesure (seuil +{MinimalScoreGain:F1} pt).";
                return outcome;
            }

            _ = await _snapshotService.RollbackSingleParameterAsync(targetAdapter.Id, param.RegistryKeyword, param.CurrentValue!, token).ConfigureAwait(false);
        }

        outcome.DecisionReason = $"Valeur initiale maintenue après analyse de {outcome.TestedCount} configurations ({throughputSamples} rounds × 4 dimensions). Déjà à l'état optimal.";
        outcome.KeptLog = $"MAINTENU : {param.DisplayName} déjà optimal après {outcome.TestedCount} configurations ({throughputSamples} rounds × 4 dimensions).";
        return outcome;
    }

    private static async Task<LatencyProbeResult?> ProbeLatencyAsync(string host, int pingCount, Action<int, int, double>? onPing, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(host) || pingCount <= 0)
        {
            return null;
        }

        var samples = new List<double>(pingCount);
        int failures = 0;
        using var ping = new Ping();
        for (int i = 0; i < pingCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            double roundtrip = -1.0;
            try
            {
                PingReply reply = await ping.SendPingAsync(host, PingTimeoutMs).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    roundtrip = reply.RoundtripTime;
                    samples.Add(roundtrip);
                }
                else
                {
                    failures++;
                }
            }
            catch (Exception)
            {
                failures++;
            }

            onPing?.Invoke(i + 1, pingCount, roundtrip);
            if (i < pingCount - 1)
            {
                await Task.Delay(PingIntervalMs, ct).ConfigureAwait(false);
            }
        }

        if (samples.Count < Math.Max(5, pingCount / 2))
        {
            return null;
        }

        double jitterMs = 0.0;
        for (int i = 1; i < samples.Count; i++)
        {
            jitterMs += Math.Abs(samples[i] - samples[i - 1]);
        }
        jitterMs /= Math.Max(1, samples.Count - 1);

        samples.Sort();
        return new LatencyProbeResult
        {
            MedianMs = Percentile(samples, 0.5),
            P95Ms = Percentile(samples, 0.95),
            JitterMs = jitterMs,
            LossPercent = failures * 100.0 / pingCount,
            Samples = samples.Count
        };
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0.0;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        double position = percentile * (sortedValues.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double fraction = position - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }

    private static double ProbeMetric(LatencyProbeResult probe, bool useP95)
    {
        return useP95 ? probe.P95Ms : probe.MedianMs;
    }

    private static bool IsThroughputClassParameter(string keyword)
    {
        return ThroughputClassKeywords.Any(k => keyword.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEnergyClassParameter(string keyword)
    {
        return EnergyClassKeywords.Any(k => keyword.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static int ParameterPriorityRank(string keyword)
    {
        if (keyword.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) || IsEnergyClassParameter(keyword))
        {
            return 0;
        }

        if (IsThroughputClassParameter(keyword))
        {
            return 1;
        }

        return 2;
    }

    private static List<string> RefineCandidatesAroundBest(DynamicNetworkParameter param, List<string> testedValues, string bestValue)
    {
        var refined = new List<string>();
        if (string.IsNullOrEmpty(bestValue) || !long.TryParse(bestValue, out long best))
        {
            return refined;
        }

        List<long> numericValues = testedValues
            .Where(v => long.TryParse(v, out _))
            .Select(v => long.Parse(v))
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        if (!numericValues.Contains(best))
        {
            return refined;
        }

        int bestIndex = numericValues.IndexOf(best);
        long lower = bestIndex > 0 ? numericValues[bestIndex - 1] : Math.Max(0, best / 2);
        long upper = bestIndex < numericValues.Count - 1 ? numericValues[bestIndex + 1] : best + Math.Max(1, best / 2);

        foreach (long candidate in new[] { (lower + best) / 2, (best + upper) / 2 })
        {
            if (candidate == best || candidate < 0)
            {
                continue;
            }

            string value = candidate.ToString();
            if (testedValues.Contains(value) || refined.Contains(value))
            {
                continue;
            }

            if (param.MinValue.HasValue && candidate < param.MinValue.Value)
            {
                continue;
            }

            if (param.MaxValue.HasValue && candidate > param.MaxValue.Value)
            {
                continue;
            }

            refined.Add(value);
        }

        return refined;
    }

    private static string? DetermineCandidateValue(DynamicNetworkParameter param)
    {
        if (param.ValueType == DynamicParameterType.Enumeration && param.ValidOptions.Count > 0)
        {
            string kw = param.RegistryKeyword;

            if (kw.Equals("WirelessMode", StringComparison.OrdinalIgnoreCase))
            {
                return HasOption(param, "802.11ax") ? "802.11ax" : (HasOption(param, "802.11ac") ? "802.11ac" : null);
            }

            if (PreferredParameterValues.TryGetValue(kw, out string[]? preferred))
            {
                foreach (string value in preferred)
                {
                    if (param.CurrentValue != value && HasOption(param, value))
                    {
                        return value;
                    }
                }

                return null;
            }

            if (kw.Contains("ChecksumOffload", StringComparison.OrdinalIgnoreCase))
            {
                return param.CurrentValue != "3" && HasOption(param, "3") ? "3" : null;
            }

            if (kw.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) ||
                kw.Contains("Flow", StringComparison.OrdinalIgnoreCase) ||
                kw.Contains("EEE", StringComparison.OrdinalIgnoreCase) ||
                kw.Contains("Green", StringComparison.OrdinalIgnoreCase) ||
                kw.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                kw.Contains("GigaLite", StringComparison.OrdinalIgnoreCase))
            {
                if (param.CurrentValue != "0" && HasOption(param, "0"))
                {
                    return "0";
                }
            }

            if (param.ValidOptions.Count == 2)
            {
                DynamicParameterOption? other = param.ValidOptions.FirstOrDefault(o => !string.Equals(o.RegistryValue, param.CurrentValue, StringComparison.OrdinalIgnoreCase));
                return other?.RegistryValue;
            }

            DynamicParameterOption? candidate = param.ValidOptions.FirstOrDefault(o => !string.Equals(o.RegistryValue, param.CurrentValue, StringComparison.OrdinalIgnoreCase));
            return candidate?.RegistryValue;
        }

        if (param.ValueType == DynamicParameterType.IntegerRange && param.MaxValue.HasValue)
        {
            if (long.TryParse(param.CurrentValue, out long currentLong))
            {
                if (currentLong < param.MaxValue.Value)
                {
                    return param.MaxValue.Value.ToString();
                }
            }
        }

        return null;
    }

    public static List<string> DetermineCandidateValues(DynamicNetworkParameter param, EngineExecutionMode mode, int targetCount)
    {
        var values = new List<string>();
        string current = param.CurrentValue ?? "";

        string? primeCandidate = DetermineCandidateValue(param);
        if (!string.IsNullOrEmpty(primeCandidate) && !values.Contains(primeCandidate))
        {
            values.Add(primeCandidate);
        }

        if (param.ValidOptions != null && param.ValidOptions.Count > 0)
        {
            foreach (DynamicParameterOption opt in param.ValidOptions)
            {
                if (!string.IsNullOrEmpty(opt.RegistryValue) && !values.Contains(opt.RegistryValue))
                {
                    values.Add(opt.RegistryValue);
                }
            }
        }

        if (param.ValueType == DynamicParameterType.IntegerRange && param.MinValue.HasValue && param.MaxValue.HasValue)
        {
            long min = param.MinValue.Value;
            long max = param.MaxValue.Value;
            if (max > min)
            {
                for (int i = 0; i < targetCount; i++)
                {
                    long stepVal = min + (long)Math.Round((double)(max - min) * i / Math.Max(1, targetCount - 1));
                    string s = stepVal.ToString();
                    if (!values.Contains(s))
                    {
                        values.Add(s);
                    }
                }
            }
        }

        string kw = param.RegistryKeyword ?? "";

        if (kw.Contains("Buffer", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i <= targetCount; i++)
            {
                int val = i <= 6 ? (32 * (1 << i)) : (128 * i);
                string s = Math.Clamp(val, 64, 4096).ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }
        else if (kw.Contains("WindowSize", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i <= targetCount; i++)
            {
                int val = 65536 * i;
                string s = val.ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }
        else if (kw.Contains("Port", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i <= targetCount; i++)
            {
                int val = 5000 + (60000 / targetCount * i);
                string s = Math.Clamp(val, 5000, 65534).ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }
        else if (kw.Contains("Delay", StringComparison.OrdinalIgnoreCase) || kw.Contains("Wait", StringComparison.OrdinalIgnoreCase) || kw.Contains("Timeout", StringComparison.OrdinalIgnoreCase) || kw.Contains("Ticks", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < targetCount; i++)
            {
                string s = i.ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }
        else if (kw.Contains("TTL", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i <= targetCount; i++)
            {
                int val = 32 + (96 / targetCount * i);
                string s = val.ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }
        else if (kw.Contains("DisabledComponents", StringComparison.OrdinalIgnoreCase))
        {
            int[] flags = [32, 0, 1, 2, 4, 8, 16, 33, 34, 48, 255];
            foreach (int f in flags)
            {
                string s = f.ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }
        else if (kw.Contains("RssQueues", StringComparison.OrdinalIgnoreCase))
        {
            string[] queues = ["1", "2", "4", "8", "16", "32"];
            foreach (string q in queues)
            {
                if (!values.Contains(q))
                {
                    values.Add(q);
                }
            }
        }
        else if (kw.Contains("CongestionProvider", StringComparison.OrdinalIgnoreCase))
        {
            string[] providers = ["ctcp", "bbr", "cubic", "newreno", "dctcp"];
            foreach (string cp in providers)
            {
                if (!values.Contains(cp))
                {
                    values.Add(cp);
                }
            }
        }
        else if (kw.Contains("AutoTuningLevel", StringComparison.OrdinalIgnoreCase))
        {
            string[] levels = ["normal", "experimental", "highlyrestricted", "disabled", "restricted"];
            foreach (string l in levels)
            {
                if (!values.Contains(l))
                {
                    values.Add(l);
                }
            }
        }
        else if (kw.Contains("Roam", StringComparison.OrdinalIgnoreCase))
        {
            string[] roamLevels = ["1", "2", "3", "4", "5"];
            foreach (string r in roamLevels)
            {
                if (!values.Contains(r))
                {
                    values.Add(r);
                }
            }
        }

        if (values.Count < targetCount && long.TryParse(current, out long curNum))
        {
            for (int i = 0; values.Count < targetCount && i < targetCount * 2; i++)
            {
                long gen = Math.Max(0, curNum - (targetCount / 2) + i);
                string s = gen.ToString();
                if (!values.Contains(s))
                {
                    values.Add(s);
                }
            }
        }

        string[] standardFallbacks = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "12", "16", "20", "24", "30", "32", "40", "64", "128", "256", "512", "1024", "2048", "4096"];
        foreach (string sf in standardFallbacks)
        {
            if (values.Count >= targetCount)
            {
                break;
            }

            if (!values.Contains(sf))
            {
                values.Add(sf);
            }
        }

        for (int i = 0; values.Count < targetCount && i < 100; i++)
        {
            string s = i.ToString();
            if (!values.Contains(s))
            {
                values.Add(s);
            }
        }

        if (values.Count > targetCount)
        {
            values = values.Take(targetCount).ToList();
        }

        return values;
    }

    private static bool HasOption(DynamicNetworkParameter param, string val)
    {
        return param.ValidOptions.Exists(o => string.Equals(o.RegistryValue, val, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> ApplyDriverRegistryValueAsync(string adapterIdOrGuid, string keyword, string value, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (keyword is "TCPNoDelay" or "TcpAckFrequency" or "TCPDelAckTicks" or "MTU" or "NetbiosOptions")
                {
                    using RegistryKey ifKey = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapterIdOrGuid}", writable: true);
                    if (ifKey != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            ifKey.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }
                        else
                        {
                            ifKey.SetValue(keyword, value, RegistryValueKind.String);
                        }

                        return true;
                    }
                }
                else if (keyword is "NetworkThrottlingIndex" or "SystemResponsiveness")
                {
                    using RegistryKey sysKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
                    if (sysKey != null)
                    {
                        if (uint.TryParse(value, out uint uv))
                        {
                            sysKey.SetValue(keyword, unchecked((int)uv), RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "NonBestEffortLimit")
                {
                    using RegistryKey pschedKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched", writable: true);
                    if (pschedKey != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            pschedKey.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "DefaultTTL" or "MaxUserPort" or "TcpTimedWaitDelay" or "EnablePMTUDiscovery" or
                                    "EnablePMTUBHDetect" or "SynAttackProtect" or "NonSackRttResiliency" or "DisableTaskOffload" or
                                    "EnableDCA" or "TCPMaxDataRetransmissions" or "InitialRto" or "MinRto" or "SackOpts" or
                                    "FastSendDatagramThreshold" or "DefaultTOSValue" or "DisableIPSourceRouting" or "EnableICMPRedirect" or "DoNotUseNLA" or
                                    "TCPChimney" or "NetDMA" or "FastOpen" or "TcpCreateAndConnectDataUnchecked" or "DisableDHCPMediaSense" or
                                    "TCPWindowSize" or "GlobalMaxTcpWindowSize" or "EcnCapability" or "Timestamps" or "Pacing" or
                                    "IGMPLevel" or "ArpRetryCount" or "ArpCacheLife" or "TcpInitialRTT")
                {
                    using RegistryKey tcpKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", writable: true);
                    if (tcpKey != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            tcpKey.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "MaxConnectionsPerServer" or "MaxConnectionsPer1_0Server")
                {
                    using RegistryKey inetKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                    if (inetKey != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            inetKey.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "DnsCacheEntries" or "MaxCacheEntryTtlLimit" or "NegativeCacheTime" or "NetFailureCacheTime" or "MaxNegativeCacheTtl" or "CacheHashTableBucketSize")
                {
                    using RegistryKey dnsKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", writable: true);
                    if (dnsKey != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            dnsKey.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "DisabledComponents")
                {
                    using RegistryKey tcp6Key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", writable: true);
                    if (tcp6Key != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            tcp6Key.SetValue(keyword, iv, RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "TcpNoDelayIPv6" or "TcpAckFrequencyIPv6")
                {
                    string actualKey = keyword == "TcpNoDelayIPv6" ? "TCPNoDelay" : "TcpAckFrequency";
                    using RegistryKey ifKey = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\{adapterIdOrGuid}", writable: true);
                    if (ifKey != null)
                    {
                        if (int.TryParse(value, out int iv))
                        {
                            ifKey.SetValue(actualKey, iv, RegistryValueKind.DWord);
                        }

                        return true;
                    }
                }
                else if (keyword is "IPv6TeredoState")
                {
                    await RunNetshAsync($"int teredo set state {value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "IPv6IsatapState")
                {
                    await RunNetshAsync($"int ipv6 isatap set state {value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "IPv66to4State")
                {
                    await RunNetshAsync($"int 6to4 set state {value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "TcpHeuristics")
                {
                    await RunNetshAsync($"int tcp set heuristics {value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "CongestionProvider")
                {
                    await RunNetshAsync($"int tcp set supplemental template=custom congestionprovider={value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "AutoTuningLevel")
                {
                    await RunNetshAsync($"int tcp set global autotuninglevel={value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "TCPRss")
                {
                    await RunNetshAsync($"int tcp set global rss={value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "TCPRsc")
                {
                    await RunNetshAsync($"int tcp set global rsc={value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "TCPTimestamps")
                {
                    await RunNetshAsync($"int tcp set global timestamps={value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "TCPEcnCapability")
                {
                    await RunNetshAsync($"int tcp set global ecncapability={value}", ct).ConfigureAwait(false);
                    return true;
                }
                else if (keyword is "NetshIPv6Randomize")
                {
                    await RunNetshAsync($"int ipv6 set global randomizeidentifiers={value}", ct).ConfigureAwait(false);
                    return true;
                }

                using RegistryKey? classKey = Registry.LocalMachine.OpenSubKey(NetworkClassKeyPath, writable: true);
                if (classKey == null)
                {
                    return false;
                }

                foreach (string subName in classKey.GetSubKeyNames())
                {
                    if (!int.TryParse(subName, out _))
                    {
                        continue;
                    }

                    using RegistryKey? sub = classKey.OpenSubKey(subName, writable: true);
                    if (sub == null)
                    {
                        continue;
                    }

                    string? netCfgInstanceId = sub.GetValue("NetCfgInstanceId") as string;
                    if (string.Equals(netCfgInstanceId, adapterIdOrGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        sub.SetValue(keyword, value, RegistryValueKind.String);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"ApplyDriverRegistryValueAsync:{keyword}");
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    private void SetPhase(EnginePhase phase, string message, double progress)
    {
        CurrentProgress.CurrentPhase = phase;
        CurrentProgress.StatusMessage = message;
        CurrentProgress.OverallProgress = progress;
        OnProgressUpdated?.Invoke(CurrentProgress);
    }

    private void UpdateOverallProgress(double progress)
    {
        CurrentProgress.OverallProgress = progress;
        OnProgressUpdated?.Invoke(CurrentProgress);
    }

    private void Log(EnginePhase phase, string message, string level)
    {
        var entry = new CognitiveLogEntry
        {
            Phase = phase,
            Message = message,
            Level = level,
            ProgressPercentage = CurrentProgress.OverallProgress
        };
        OnLogEntryAdded?.Invoke(entry);
    }

    private static async Task RunNetshAsync(string args, CancellationToken ct)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            if (p == null)
            {
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch { }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkExperimentEngine.RunNetshAsync");
        }
    }
}