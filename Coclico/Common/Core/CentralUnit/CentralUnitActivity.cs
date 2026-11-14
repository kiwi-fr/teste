namespace Coclico.Services;

internal enum CentralActivityLevel
{
    StartingUp,
    Active,
    Background,
    Idle,
    ExternalPressure
}

internal sealed class CentralUnitActivity
{
    private readonly Lock _stateLock = new();
    private bool _started;
    private bool _windowVisible;
    private bool _trayMinimized;
    private bool _aiInference;
    private bool _externalPressure;
    private double _intervalFactor = 1.0;

    private const double IntervalFactorBackground = 3.0;
    private const double IntervalFactorIdle = 10.0;
    private const double IntervalFactorAdditiveStep = 0.1;
    private const double IntervalFactorMultiplicativeCut = 0.5;

    public CentralActivityLevel Level { get; private set; } = CentralActivityLevel.StartingUp;

    public event Action<CentralActivityLevel>? LevelChanged;

    public bool IsAiInferenceActive
    {
        get
        {
            lock (_stateLock)
            {
                return _aiInference;
            }
        }
    }

    public void MarkStarted()
    {
        lock (_stateLock)
        {
            _started = true;
        }

        RecomputeLevel();
    }

    public void SetWindowVisible(bool visible)
    {
        lock (_stateLock)
        {
            _windowVisible = visible;
        }

        RecomputeLevel();
    }

    public void SetTrayMinimized(bool minimized)
    {
        lock (_stateLock)
        {
            _trayMinimized = minimized;
        }

        RecomputeLevel();
    }

    public void SetAiInference(bool active)
    {
        lock (_stateLock)
        {
            _aiInference = active;
        }

        RecomputeLevel();
    }

    public void SetExternalPressure(bool underPressure)
    {
        lock (_stateLock)
        {
            _externalPressure = underPressure;
        }

        RecomputeLevel();
    }

    public TimeSpan AdaptInterval(TimeSpan baseInterval)
    {
        if (Level == CentralActivityLevel.ExternalPressure)
        {
            return Timeout.InfiniteTimeSpan;
        }

        double target = Level switch
        {
            CentralActivityLevel.Background => IntervalFactorBackground,
            CentralActivityLevel.Idle => IntervalFactorIdle,
            _ => 1.0
        };

        double factor = MoveFactorToward(target);
        return TimeSpan.FromTicks((long)(baseInterval.Ticks * factor));
    }

    private double MoveFactorToward(double target)
    {
        lock (_stateLock)
        {
            if (target > _intervalFactor)
            {
                _intervalFactor = Math.Min(target, _intervalFactor + IntervalFactorAdditiveStep);
            }
            else if (target < _intervalFactor)
            {
                _intervalFactor = Math.Max(target, _intervalFactor * IntervalFactorMultiplicativeCut);
            }

            return _intervalFactor;
        }
    }

    private void RecomputeLevel()
    {
        CentralActivityLevel level;
        lock (_stateLock)
        {
            if (_externalPressure)
            {
                level = CentralActivityLevel.ExternalPressure;
            }
            else if (!_started)
            {
                level = CentralActivityLevel.StartingUp;
            }
            else if (_windowVisible || _aiInference)
            {
                level = CentralActivityLevel.Active;
            }
            else if (!_trayMinimized)
            {
                level = CentralActivityLevel.Background;
            }
            else
            {
                level = CentralActivityLevel.Idle;
            }
        }

        if (level == Level)
        {
            return;
        }

        Level = level;
        LevelChanged?.Invoke(level);
    }
}
