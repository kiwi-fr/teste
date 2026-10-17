using Coclico.Models.Network;

namespace Coclico.Services.Network;

public class NetworkScoringService : INetworkScoringService
{
    public double ComputeCompositeScore(NetworkMetricReport report, OptimizationProfile profile)
    {
        double latScore = Math.Clamp(100.0 - (report.LatencyMedianMs * 0.7), 0.0, 100.0);

        double jitScore = Math.Clamp(100.0 - (report.JitterMs * 5.0), 0.0, 100.0);

        double lossScore = Math.Clamp(100.0 - (report.PacketLossPercent * 25.0), 0.0, 100.0);

        double bloatScore = report.BufferbloatGrade switch
        {
            "A+" => 100.0,
            "A" => 90.0,
            "B" => 75.0,
            "C" => 55.0,
            "D" => 35.0,
            _ => 10.0
        };

        double dnsScore = Math.Clamp(100.0 - (report.DnsResolutionMs * 0.8), 0.0, 100.0);
        if (report.DnsSuccessRatePercent < 100)
        {
            dnsScore *= report.DnsSuccessRatePercent / 100.0;
        }

        double finalScore = profile switch
        {
            OptimizationProfile.Gaming =>
                (latScore * 0.35) + (jitScore * 0.30) + (lossScore * 0.25) + (bloatScore * 0.10),

            OptimizationProfile.LowLatency =>
                (latScore * 0.40) + (jitScore * 0.35) + (lossScore * 0.25),

            OptimizationProfile.MaxThroughput =>
                (bloatScore * 0.35) + (lossScore * 0.35) + (jitScore * 0.15) + (latScore * 0.15),

            OptimizationProfile.Streaming =>
                (lossScore * 0.35) + (jitScore * 0.30) + (bloatScore * 0.20) + (latScore * 0.15),

            OptimizationProfile.WifiSpecific =>
                (jitScore * 0.35) + (lossScore * 0.30) + (latScore * 0.25) + (bloatScore * 0.10),

            _ =>
                (latScore * 0.30) + (jitScore * 0.25) + (lossScore * 0.25) + (bloatScore * 0.10) + (dnsScore * 0.10)
        };

        report.CompositeScore = Math.Round(Math.Clamp(finalScore, 0.0, 100.0), 1);
        return report.CompositeScore;
    }

    public (bool IsImprovement, ExperimentDecision Decision, string Reason) CompareMetrics(
        NetworkMetricReport baseline,
        NetworkMetricReport tested,
        OptimizationProfile profile)
    {
        if (tested.PacketLossPercent > baseline.PacketLossPercent + 0.5)
        {
            return (false, ExperimentDecision.RejectedInstability,
                $"Perte de paquets en hausse ({tested.PacketLossPercent:F1}% vs {baseline.PacketLossPercent:F1}%). Rejet pour préserver la stabilité.");
        }

        if (tested.JitterMs > baseline.JitterMs + 2.0 && tested.JitterMs > baseline.JitterMs * 1.35)
        {
            return (false, ExperimentDecision.RejectedInstability,
                $"Gigue excessive (+{tested.JitterMs - baseline.JitterMs:F1} ms). Dégradation de la régularité du flux.");
        }

        if (tested.LatencyMedianMs > baseline.LatencyMedianMs + 3.0 && tested.LatencyMedianMs > baseline.LatencyMedianMs * 1.15)
        {
            return (false, ExperimentDecision.RejectedRegression,
                $"Latence médiane dégradée (+{tested.LatencyMedianMs - baseline.LatencyMedianMs:F1} ms).");
        }

        if (tested.LatencyMedianMs <= 0 && tested.ThroughputMbps <= 0)
        {
            return (false, ExperimentDecision.KeptBaseline,
                "Mesure non concluante après application : aucune métrique exploitable, configuration d'origine conservée.");
        }

        double baselineScore = ComputeCompositeScore(baseline, profile);
        double testedScore = ComputeCompositeScore(tested, profile);

        double jitterRef = Math.Max(baseline.JitterMs, tested.JitterMs);
        double sampleFactor = Math.Min(baseline.SampleCount, tested.SampleCount) >= 20 ? 1.0 : 1.5;
        double latencyNoise = Math.Max(0.5 * sampleFactor, jitterRef * 0.25 * sampleFactor);
        double p99Noise = Math.Max(1.0 * sampleFactor, latencyNoise * 2.0);
        double jitterNoise = Math.Max(0.4 * sampleFactor, jitterRef * 0.20 * sampleFactor);
        double throughputNoise = Math.Max(3.0, baseline.ThroughputMbps * 0.05) * sampleFactor;
        double bloatNoise = 10.0 * sampleFactor;

        double deltaLat = baseline.LatencyMedianMs - tested.LatencyMedianMs;
        double deltaJit = baseline.JitterMs - tested.JitterMs;
        double deltaP99 = baseline.LatencyP99Ms - tested.LatencyP99Ms;
        double deltaBloat = baseline.BufferbloatIncreaseMs - tested.BufferbloatIncreaseMs;
        double deltaThroughput = tested.ThroughputMbps - baseline.ThroughputMbps;

        bool latencyWin = baseline.LatencyMedianMs > 0 && tested.LatencyMedianMs > 0 && deltaLat > latencyNoise;
        bool jitterWin = baseline.JitterMs > 0 && tested.JitterMs > 0 && deltaJit > jitterNoise;
        bool p99Win = baseline.LatencyP99Ms > 0 && tested.LatencyP99Ms > 0 && deltaP99 > p99Noise;
        bool bloatWin = baseline.BufferbloatLatencyUnderLoadMs > 0 && tested.BufferbloatLatencyUnderLoadMs > 0 && deltaBloat > bloatNoise;
        bool throughputWin = baseline.ThroughputMbps > 0 && tested.ThroughputMbps > 0 && deltaThroughput > throughputNoise;

        if (!latencyWin && !jitterWin && !p99Win && !bloatWin && !throughputWin)
        {
            return (false, ExperimentDecision.KeptBaseline,
                $"Aucun effet mesurable au-delà du bruit (Ping {tested.LatencyMedianMs:F1} vs {baseline.LatencyMedianMs:F1} ms, Score {testedScore:F1} vs {baselineScore:F1}). Configuration d'origine conservée.");
        }

        if (testedScore < baselineScore - 1.5)
        {
            return (false, ExperimentDecision.KeptBaseline,
                $"Gain local annulé par le reste des mesures (Score {testedScore:F1} vs {baselineScore:F1}). Configuration d'origine conservée.");
        }

        string details = "";
        if (latencyWin)
        {
            details += $"-{deltaLat:F1}ms ping ";
        }

        if (jitterWin)
        {
            details += $"-{deltaJit:F1}ms gigue ";
        }

        if (p99Win)
        {
            details += $"-{deltaP99:F1}ms pics ";
        }

        if (bloatWin)
        {
            details += $"-{deltaBloat:F0}ms bloat ";
        }

        if (throughputWin)
        {
            details += $"+{deltaThroughput:F0}Mbps ";
        }

        return (true, ExperimentDecision.Accepted, $"Amélioration mesurée ({details.Trim()}). Configuration conservée.");
    }
}