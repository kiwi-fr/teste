using Coclico.Services;

namespace Coclico.ViewModels;

/// <summary>
/// État S.M.A.R.T. d'un disque, prêt pour l'affichage dans la vue Santé &amp; Défense.
/// Immutable : chaque vérification remplace la collection, aucun binding bidirectionnel.
/// </summary>
public sealed class SmartDiskItem
{
    public SmartDiskItem(SmartDiskStatus status)
    {
        InstanceName = status.InstanceName;
        IsWarning = status.PredictsFailure;
        Advisory = status.Advisory;
    }

    public string InstanceName { get; }

    /// <summary>True si le disque signale un risque de panne imminente (S.M.A.R.T.).</summary>
    public bool IsWarning { get; }

    /// <summary>Inverse de <see cref="IsWarning"/>, pratique pour les convertisseurs de visibilité.</summary>
    public bool IsOk => !IsWarning;

    public string Advisory { get; }
}
