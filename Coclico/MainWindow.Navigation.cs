using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Modules.Apps.Views;
using Coclico.Modules.DiskAnalyzer.Views;
using Coclico.Modules.Power.Views;
using Coclico.Modules.UpdateCenter.ViewModels;
using Coclico.Modules.UpdateCenter.Views;
using Coclico.Services;
using Coclico.Modules.RamCleaner.Views;
using Coclico.Views;
namespace Coclico;

public partial class MainWindow
{
    private static readonly (string Tag, Func<UIElement> Factory)[] ModuleRegistry =
    [
        ("Home", static () => ServiceContainer.GetOptional<DashboardView>() ?? new DashboardView()),
        ("Programs", static () => new ProgramsView()),
        ("DiskAnalyzer", static () => new DiskAnalyzerView()),
        ("HealthDefense", static () => new HealthDefenseView()),
        ("Diagnostics", static () => new DiagnosticView()),
        ("Network", static () => ServiceContainer.GetOptional<NetworkOptimizerView>() ?? new NetworkOptimizerView()),
        ("Installer", static () => ServiceContainer.GetOptional<AppsView>() ?? new AppsView()),
        ("UpdateCenter", static () => ServiceContainer.GetOptional<UpdateCenterView>() ?? new UpdateCenterView(ServiceContainer.GetRequired<UpdateCenterViewModel>())),
        ("Cleaning", static () => new CleaningView()),
        ("RamCleaner", static () => new RamCleanerView()),
        ("TelemetryBlocker", static () => new Coclico.Modules.TelemetryBlocker.Views.TelemetryBlockerView()),
        ("PowerOptimizer", static () => ServiceContainer.GetOptional<PowerOptimizerView>() ?? new PowerOptimizerView()),
        ("Settings", static () => new SettingsView()),
        ("Help", static () => new HelpView()),
    ];

    private static readonly string[] MenuGroupIds = ["Maintenance", "Performance", "Applications"];

    private const int MaxCachedViews = 8;

    private UIElement GetOrCreateView(string? tag)
    {
        tag = string.IsNullOrEmpty(tag) ? "Home" : tag;
        if (_viewCache.TryGetValue(tag, out UIElement? cached))
        {
            TouchViewCache(tag);
            return cached;
        }

        (string Tag, Func<UIElement> Factory) module = ModuleRegistry
            .FirstOrDefault(m => string.Equals(m.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (module.Factory == null)
        {
            module = ModuleRegistry[0];
        }

        UIElement view = module.Factory();
        _viewCache[module.Tag] = view;
        TouchViewCache(module.Tag);
        TrimViewCache();
        return view;
    }

    private void TouchViewCache(string tag)
    {
        _ = _viewCacheOrder.Remove(tag);
        _viewCacheOrder.Add(tag);
    }

    private void TrimViewCache()
    {
        int guard = 0;
        while (_viewCacheOrder.Count > MaxCachedViews && guard++ < MaxCachedViews + 1)
        {
            string oldest = _viewCacheOrder[0];
            if (_viewCache.TryGetValue(oldest, out UIElement? view) && ReferenceEquals(view, MainContentFrame.Content))
            {
                TouchViewCache(oldest);
                continue;
            }

            _ = _viewCacheOrder.Remove(oldest);
            _ = _viewCache.Remove(oldest);
        }
    }

    private static string AiContextForTag(string tag) => tag switch
    {
        "Home" => Loc.Get("AiCtx_Home"),
        "Programs" => Loc.Get("AiCtx_Programs"),
        "DiskAnalyzer" => Loc.Get("AiCtx_DiskAnalyzer"),
        "HealthDefense" => Loc.Get("AiCtx_HealthDefense"),
        "Network" => Loc.Get("AiCtx_Network"),
        "Installer" => Loc.Get("AiCtx_Installer"),
        "UpdateCenter" => Loc.Get("AiCtx_UpdateCenter"),
        "Cleaning" => Loc.Get("AiCtx_Cleaning"),
        "RamCleaner" => Loc.Get("AiCtx_RamCleaner"),
        "PowerOptimizer" => Loc.Get("AiCtx_PowerOptimizer"),
        "Settings" => Loc.Get("AiCtx_Settings"),
        "Help" => Loc.Get("AiCtx_Help"),
        _ => Loc.Get("AiCtx_Home")
    };

    private static string? TagForAiAction(string action)
    {
        return action switch
        {
            "open_dashboard" => "Home",
            "open_programs" => "Programs",
            "open_disk" => "DiskAnalyzer",
            "open_health" or "open_defense" => "HealthDefense",
            "open_diagnostics" => "Diagnostics",
            "open_network" => "Network",
            "open_power" => "PowerOptimizer",
            "open_installer" => "Installer",
            "open_updates" => "UpdateCenter",
            "open_cleaning" => "Cleaning",
            "open_ramcleaner" => "RamCleaner",
            "open_settings" => "Settings",
            "open_help" => "Help",
            _ => null
        };
    }

    private static void RecordStigmergyTransition(string tag)
    {
        try
        {
            CentralUnitStigmergy stigmergy = CentralUnitStigmergy.Current;
            stigmergy.RecordTransition(stigmergy.LastModule, tag);
            stigmergy.RecordModuleUsage(tag);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "MainWindow.RecordStigmergyTransition"); }
    }

    private void PreloadHourlyLikelyViews()
    {
        try
        {
            int hour = DateTime.Now.Hour;
            if (hour == _lastHourlyPreloadHour)
            {
                return;
            }

            _lastHourlyPreloadHour = hour;

            CentralActivityLevel level = CentralUnitService.Current.Activity.Level;
            if (level != CentralActivityLevel.Active)
            {
                return;
            }

            List<(string Module, double Score)> likely = CentralUnitStigmergy.Current.GetLikelyModulesAtHour(hour, 1);
            if (likely.Count == 0 || likely[0].Score < 2.0 || _viewCache.ContainsKey(likely[0].Module))
            {
                return;
            }

            string module = likely[0].Module;
            _ = Dispatcher.InvokeAsync(() => _ = GetOrCreateView(module), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "MainWindow.PreloadHourlyLikelyViews"); }
    }

    private void PreloadPredictedViews(string currentTag)
    {
        try
        {
            List<(string Module, double Probability)> predicted = CentralUnitStigmergy.Current.GetPredictedNextModules(currentTag, 2);
            if (predicted.Count == 0)
            {
                return;
            }

            CentralActivityLevel level = CentralUnitService.Current.Activity.Level;
            if (level != CentralActivityLevel.Active && level != CentralActivityLevel.StartingUp)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                foreach ((string module, double probability) in predicted)
                {
                    if (probability < 0.6)
                    {
                        continue;
                    }

                    _ = GetOrCreateView(module);
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "MainWindow.PreloadPredictedViews"); }
    }

    public async Task NavigateToTagAsync(string tag)
    {
        try
        {
            RecordStigmergyTransition(tag);

            UIElement newContent = GetOrCreateView(tag);

            IAiService? ai = ServiceContainer.GetOptional<IAiService>();
            if (ai != null)
            {
                ai.CurrentStatusContext = AiContextForTag(tag);
            }

            await NavigateWithTransition(newContent).ConfigureAwait(false);
            PreloadPredictedViews(tag);
            PreloadHourlyLikelyViews();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.NavigateToTagAsync");
            MainContentFrame.Content = CreateErrorFallback(Loc.Get("MainWindow_PageLoadError"));
        }
    }

    private async Task NavigateWithTransition(UIElement newContent)
    {
        if (MainContentFrame.RenderTransform is not TranslateTransform)
        {
            MainContentFrame.RenderTransform = new TranslateTransform();
        }

        var translate = (TranslateTransform)MainContentFrame.RenderTransform;

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeOut = new DoubleAnimation(0, Motion.NavFadeOut) { EasingFunction = easeOut };
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(Motion.NavFadeOut).ConfigureAwait(false);

        MainContentFrame.Content = newContent;

        if (AnimatePageIn())
        {
            return;
        }

        translate.Y = Motion.PageSlideDistance;
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0, 1, Motion.NavSlideIn) { EasingFunction = easeIn };
        var slideIn = new DoubleAnimation(Motion.PageSlideDistance, 0, Motion.NavSlideIn) { EasingFunction = easeIn };
        translate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private bool AnimatePageIn()
    {
        if (TryFindResource("PageInStoryboard") is not Storyboard pageIn || FindName("PageContentTransform") is null)
        {
            return false;
        }

        pageIn.Begin(this, HandoffBehavior.SnapshotAndReplace, false);
        return true;
    }

    private UIElement CreateErrorFallback(string message)
    {
        var border = new Border
        {
            Background = TryFindResource("BgPanelBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(255, 6, 6, 12)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(26),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _ = stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? new SolidColorBrush(Colors.White),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(6),
            Width = 560
        });

        _ = stack.Children.Add(new Button
        {
            Content = Loc.Get("Common_Retry"),
            Width = 140,
            Margin = new Thickness(0, 14, 0, 0),
            Background = TryFindResource("BgElevatedBrush") as Brush,
            Foreground = TryFindResource("ArchitecturalYellowBrush") as Brush,
            BorderBrush = TryFindResource("BorderAccentBrush") as Brush,
            BorderThickness = new Thickness(1),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => MainContentFrame.Content = GetOrCreateView("Home"))
        });

        border.Child = stack;
        return border;
    }

    public void NavigateTo(string tag)
    {
        _ = NavigateToTagAsync(tag);
    }

    private void MenuGroupToggle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string groupId)
        {
            SetMenuGroupExpanded(groupId, !IsMenuGroupExpanded(groupId), persist: true);
            e.Handled = true;
        }
    }

    private bool IsMenuGroupExpanded(string groupId)
    {
        return FindMenuPanel(groupId)?.Visibility == Visibility.Visible;
    }

    private System.Windows.Controls.StackPanel? FindMenuPanel(string groupId) => groupId switch
    {
        "Maintenance" => MenuPanelMaintenance,
        "Performance" => MenuPanelPerformance,
        "Applications" => MenuPanelApplications,
        _ => null
    };

    private Wpf.Ui.Controls.SymbolIcon? FindMenuChevron(string groupId) => groupId switch
    {
        "Maintenance" => MenuChevronMaintenance,
        "Performance" => MenuChevronPerformance,
        "Applications" => MenuChevronApplications,
        _ => null
    };

    private void SetMenuGroupExpanded(string groupId, bool expanded, bool persist = false)
    {
        try
        {
            System.Windows.Controls.StackPanel? panel = FindMenuPanel(groupId);
            if (panel != null)
            {
                panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FindMenuChevron(groupId)?.RenderTransform is RotateTransform rotate)
            {
                var anim = new DoubleAnimation(expanded ? 0 : -90, Motion.ChevronRotate)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            }

            if (persist)
            {
                SaveMenuGroupExpansion();
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"MainWindow.SetMenuGroupExpanded({groupId})");
        }
    }

    private void LoadMenuGroupExpansion()
    {
        try
        {
            SettingsService ss = ServiceContainer.GetRequired<SettingsService>();
            HashSet<string> expanded = new(
                ss.Settings.MenuExpandedGroups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            foreach (string id in MenuGroupIds)
            {
                SetMenuGroupExpanded(id, expanded.Contains(id), persist: false);
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.LoadMenuGroupExpansion");
        }
    }

    private void SaveMenuGroupExpansion()
    {
        try
        {
            SettingsService ss = ServiceContainer.GetRequired<SettingsService>();
            ss.Settings.MenuExpandedGroups = string.Join(",", MenuGroupIds.Where(IsMenuGroupExpanded));
            _ = ss.SaveAsync();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.SaveMenuGroupExpansion");
        }
    }
}
