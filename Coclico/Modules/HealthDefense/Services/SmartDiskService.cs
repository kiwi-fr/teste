using System.Management;

namespace Coclico.Services;

public sealed record SmartDiskStatus(
    string InstanceName,
    bool PredictsFailure,
    uint Reason,
    string Advisory);

public static class SmartDiskService
{

    public static List<SmartDiskStatus> GetDiskStatus()
    {
        var results = new List<SmartDiskStatus>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT InstanceName, PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus");

            foreach (ManagementBaseObject disk in searcher.Get())
            {
                try
                {
                    string instance = disk["InstanceName"]?.ToString() ?? "Disque inconnu";
                    bool predictsFailure = disk["PredictFailure"] is bool b && b;
                    uint reason = disk["Reason"] is uint r ? r : 0u;

                    results.Add(new SmartDiskStatus(
                        InstanceName: instance,
                        PredictsFailure: predictsFailure,
                        Reason: reason,
                        Advisory: predictsFailure
                            ? "Ce disque signale un risque de panne imminente (S.M.A.R.T.). Sauvegardez vos données importantes dès maintenant."
                            : "Aucun signe de panne imminente signalé par S.M.A.R.T."));
                }
                finally
                {
                    disk.Dispose();
                }
            }

            LoggingService.LogInfo($"[SmartDiskService] État S.M.A.R.T. récupéré pour {results.Count} disque(s).");
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or System.IO.IOException or System.UnauthorizedAccessException)
        {

            LoggingService.LogException(ex, "SmartDiskService.GetDiskStatus");
        }

        return results;
    }
}
