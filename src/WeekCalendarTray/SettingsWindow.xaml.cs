using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using WeekCalendarTray.Core;

namespace WeekCalendarTray;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<IcalSubscription> _subscriptions = [];
    private readonly CalendarSyncCoordinator _syncCoordinator;
    private readonly PrayerLocationResolver _prayerLocationResolver = new();
    private IcalSubscription? _editingSubscription;
    private PrayerTimesLocation? _resolvedPrayerLocation;
    private bool _settingsLoaded;
    private bool _isBusy;
    private bool _isClosed;
    private bool _savedAcrylicEnabled;
    private int _savedAcrylicOpacityPercent = ThemeManager.DefaultAcrylicOpacityPercent;
    private AppThemePreference _savedThemePreference = AppThemePreference.System;
    private bool _hasUnsavedAcrylicPreview;

    internal SettingsWindow(CalendarSyncCoordinator syncCoordinator)
    {
        _syncCoordinator = syncCoordinator;
        InitializeComponent();
        SubscriptionsListBox.ItemsSource = _subscriptions;
        Loaded += SettingsWindow_Loaded;
        Closing += SettingsWindow_Closing;
        Closed += SettingsWindow_Closed;
        SetBusy(true);
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settingsLoaded || _isClosed)
        {
            return;
        }

        try
        {
            await LoadSettingsAsync();
            if (_isClosed)
            {
                return;
            }

            _settingsLoaded = true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Load settings window", ex);
            SetStatus("Settings could not be loaded. Your existing settings were not changed.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _syncCoordinator.SettingsStore.LoadAsync();
        if (_isClosed)
        {
            return;
        }

        _subscriptions.Clear();
        foreach (var subscription in settings.Subscriptions)
        {
            _subscriptions.Add(subscription);
        }

        StartWithWindowsCheckBox.IsChecked = StartupManager.IsEnabled();
        ShowEventIndicatorsCheckBox.IsChecked = settings.ShowEventIndicators;
        ShowEventPreviewCheckBox.IsChecked = settings.ShowEventPreviewOnHover;
        AcrylicEnabledCheckBox.IsChecked = settings.AcrylicEnabled;
        AcrylicOpacitySlider.Value = settings.AcrylicOpacityPercent;
        AcrylicOpacityValueText.Text = $"{settings.AcrylicOpacityPercent}%";
        var themePreference = AppThemePreferences.Parse(settings.ThemePreference);
        ThemePreferenceComboBox.SelectedIndex = (int)themePreference;
        DayViewLayoutComboBox.SelectedIndex = settings.DayViewLayout == DayViewLayouts.Timeline ? 1 : 0;
        _savedAcrylicEnabled = settings.AcrylicEnabled;
        _savedAcrylicOpacityPercent = settings.AcrylicOpacityPercent;
        _savedThemePreference = themePreference;
        _hasUnsavedAcrylicPreview = false;
        PrayerTimesEnabledCheckBox.IsChecked = settings.PrayerTimesEnabled;
        PrayerNotificationsEnabledCheckBox.IsChecked = settings.PrayerNotificationsEnabled;
        PrayerLocationTextBox.Text = settings.PrayerLocationName;
        _resolvedPrayerLocation = new PrayerTimesLocation(
            settings.PrayerLocationName,
            settings.PrayerLatitude,
            settings.PrayerLongitude);
        PrayerLocationStatusText.Text = GetPrayerLocationStatus(_resolvedPrayerLocation);
        StatusText.Text = $"{_subscriptions.Count} calendar link(s) configured.";
        ThemeManager.SetAppearanceOptions(
            settings.AcrylicEnabled,
            settings.AcrylicOpacityPercent,
            themePreference);
    }

    private async Task<string?> SaveSettingsAsync()
    {
        if (!_settingsLoaded || _isClosed)
        {
            return null;
        }

        SavePendingEditName();

        foreach (var subscription in _subscriptions)
        {
            subscription.Name = string.IsNullOrWhiteSpace(subscription.Name)
                ? "My calendar"
                : subscription.Name.Trim();
        }

        var settings = await _syncCoordinator.SettingsStore.LoadAsync();
        if (_isClosed)
        {
            return null;
        }

        settings.Subscriptions = _subscriptions.ToList();
        settings.ShowEventIndicators = ShowEventIndicatorsCheckBox.IsChecked == true;
        settings.ShowEventPreviewOnHover = ShowEventPreviewCheckBox.IsChecked == true;
        settings.AcrylicEnabled = AcrylicEnabledCheckBox.IsChecked == true;
        settings.AcrylicOpacityPercent = GetSelectedAcrylicOpacity();
        var themePreference = GetSelectedThemePreference();
        settings.ThemePreference = themePreference.ToString();
        settings.DayViewLayout = GetSelectedDayViewLayout();
        var startWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        var prayerStatus = await SavePrayerSettingsAsync(settings);
        if (_isClosed)
        {
            return null;
        }

        await _syncCoordinator.SettingsStore.SaveAsync(settings);
        ThemeManager.SetAppearanceOptions(
            settings.AcrylicEnabled,
            settings.AcrylicOpacityPercent,
            themePreference);
        _savedAcrylicEnabled = settings.AcrylicEnabled;
        _savedAcrylicOpacityPercent = settings.AcrylicOpacityPercent;
        _savedThemePreference = themePreference;
        _hasUnsavedAcrylicPreview = false;
        StartupManager.SetEnabled(startWithWindows);
        _syncCoordinator.NotifySettingsChanged();
        return prayerStatus;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var url = CleanPastedUrl(UrlTextBox.Text);

        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Enter a calendar name.";
            return;
        }

        if (!IsSupportedUrl(url))
        {
            StatusText.Text = "Paste a valid http, https, or webcal iCal/ICS URL.";
            return;
        }

        if (LooksLikeHtmlCalendarUrl(url))
        {
            StatusText.Text = "That looks like the Outlook HTML link. Copy the ICS link instead.";
            return;
        }

        _subscriptions.Add(new IcalSubscription
        {
            Name = name,
            Url = url,
            IsEnabled = true
        });

        UrlTextBox.Clear();
        StatusText.Text = "Calendar link added. Click Save or Sync now.";
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var subscription = _editingSubscription ?? SubscriptionsListBox.SelectedItem as IcalSubscription;
        if (subscription is null)
        {
            return;
        }

        _subscriptions.Remove(subscription);
        HideEditPanel();
        StatusText.Text = "Calendar link removed. Click Save or Sync now.";
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (SubscriptionsListBox.SelectedItem is not IcalSubscription subscription)
        {
            return;
        }

        subscription.IsEnabled = !subscription.IsEnabled;
        SubscriptionsListBox.Items.Refresh();
        StatusText.Text = subscription.IsEnabled ? "Calendar link enabled." : "Calendar link disabled.";
    }

    private void EditSubscription_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not IcalSubscription subscription)
        {
            return;
        }

        _editingSubscription = subscription;
        SubscriptionsListBox.SelectedItem = subscription;
        EditNameTextBox.Text = subscription.Name;
        EditUrlTextBox.Text = subscription.Url;
        EditSubscriptionPanel.Visibility = Visibility.Visible;
        EditNameTextBox.Focus();
        EditNameTextBox.SelectAll();
    }

    private void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_editingSubscription is null)
        {
            return;
        }

        SavePendingEditName();
        SubscriptionsListBox.Items.Refresh();
        StatusText.Text = "Calendar name updated. Click Save or Sync now.";
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        HideEditPanel();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var prayerStatus = await SaveSettingsAsync();
            SetStatus(prayerStatus ?? "Settings saved.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Save settings window", ex);
            SetStatus("Could not complete saving all settings. Check your settings and try again.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var prayerStatus = await SaveSettingsAsync();
            if (_isClosed)
            {
                return;
            }

            SetStatus(prayerStatus ?? "Syncing calendar links...");
            var result = await _syncCoordinator.SyncAsync();
            SetStatus(result.Summary);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Sync settings window", ex);
            SetStatus("Sync could not be completed. Your saved settings were kept.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AcrylicEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyAppearancePreview();
    }

    private void AcrylicOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (AcrylicOpacityValueText is not null)
        {
            AcrylicOpacityValueText.Text = $"{GetSelectedAcrylicOpacity()}%";
        }

        ApplyAppearancePreview();
    }

    private void ThemePreferenceComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplyAppearancePreview();
    }

    private async void FindPrayerLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            await ResolvePrayerLocationAsync(showSavedStatus: false);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Find prayer location", ex);
            if (!_isClosed)
            {
                PrayerLocationStatusText.Text = "Location lookup failed. Check the location and try again.";
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SavePendingEditName()
    {
        if (_editingSubscription is null)
        {
            return;
        }

        _editingSubscription.Name = string.IsNullOrWhiteSpace(EditNameTextBox.Text)
            ? "My calendar"
            : EditNameTextBox.Text.Trim();
    }

    private void HideEditPanel()
    {
        _editingSubscription = null;
        EditNameTextBox.Clear();
        EditUrlTextBox.Clear();
        EditSubscriptionPanel.Visibility = Visibility.Collapsed;
    }

    private static bool IsSupportedUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("webcal", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanPastedUrl(string value)
    {
        var trimmed = value.Trim();

        foreach (var prefix in new[] { "ICS:", "iCal:", "HTML:" })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return trimmed;
    }

    private static bool LooksLikeHtmlCalendarUrl(string value)
    {
        return value.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/calendar.html", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> SavePrayerSettingsAsync(SyncSettings settings)
    {
        settings.PrayerTimesEnabled = PrayerTimesEnabledCheckBox.IsChecked == true;
        settings.PrayerNotificationsEnabled = PrayerNotificationsEnabledCheckBox.IsChecked == true;

        var requestedLocation = string.IsNullOrWhiteSpace(PrayerLocationTextBox.Text)
            ? settings.PrayerLocationName
            : PrayerLocationTextBox.Text.Trim();

        if (settings.PrayerTimesEnabled
            && (_resolvedPrayerLocation is null
                || !requestedLocation.Equals(_resolvedPrayerLocation.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var resolvedMessage = await ResolvePrayerLocationAsync(showSavedStatus: true);
            if (_isClosed)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(resolvedMessage))
            {
                requestedLocation = PrayerLocationTextBox.Text.Trim();
            }
            else
            {
                return "Settings saved. Prayer location was not changed because it could not be found.";
            }
        }

        var location = _resolvedPrayerLocation ?? new PrayerTimesLocation(
            settings.PrayerLocationName,
            settings.PrayerLatitude,
            settings.PrayerLongitude);

        settings.PrayerLocationName = location.Name;
        settings.PrayerLatitude = location.Latitude;
        settings.PrayerLongitude = location.Longitude;
        PrayerLocationStatusText.Text = GetPrayerLocationStatus(location);

        return null;
    }

    private async Task<string?> ResolvePrayerLocationAsync(bool showSavedStatus)
    {
        var query = PrayerLocationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            PrayerLocationStatusText.Text = "Enter a city or address.";
            return null;
        }

        try
        {
            PrayerLocationStatusText.Text = "Finding location...";
            var location = await _prayerLocationResolver.ResolveAsync(query);
            if (_isClosed)
            {
                return null;
            }

            if (location is null)
            {
                PrayerLocationStatusText.Text = "Location not found.";
                return null;
            }

            _resolvedPrayerLocation = location;
            PrayerLocationTextBox.Text = location.Name;
            PrayerLocationStatusText.Text = GetPrayerLocationStatus(location);
            return showSavedStatus ? "Settings saved. Prayer location updated." : "Prayer location found.";
        }
        catch (Exception ex) when (ex is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or JsonException)
        {
            AppDiagnostics.Log("Resolve prayer location", ex);
            if (!_isClosed)
            {
                PrayerLocationStatusText.Text = "Location lookup failed. Check the location and try again.";
            }

            return null;
        }
    }

    private static string GetPrayerLocationStatus(PrayerTimesLocation location)
    {
        return $"Saved coordinates from typed location: {location.Latitude:0.0000}, {location.Longitude:0.0000}";
    }

    private bool TryBeginOperation()
    {
        if (!_settingsLoaded || _isBusy || _isClosed)
        {
            return false;
        }

        SetBusy(true);
        return true;
    }

    private void SetBusy(bool isBusy)
    {
        if (_isClosed)
        {
            return;
        }

        _isBusy = isBusy;
        SaveButton.IsEnabled = _settingsLoaded && !isBusy;
        SyncNowButton.IsEnabled = _settingsLoaded && !isBusy;
        FindPrayerLocationButton.IsEnabled = _settingsLoaded && !isBusy;
        AcrylicEnabledCheckBox.IsEnabled = _settingsLoaded && !isBusy;
        ThemePreferenceComboBox.IsEnabled = _settingsLoaded && !isBusy;
        DayViewLayoutComboBox.IsEnabled = _settingsLoaded && !isBusy;
        UpdateAcrylicOpacityPanelState();
    }

    private void SetStatus(string status)
    {
        if (!_isClosed)
        {
            StatusText.Text = status;
        }
    }

    private void ApplyAppearancePreview()
    {
        UpdateAcrylicOpacityPanelState();

        if (!_settingsLoaded || _isClosed)
        {
            return;
        }

        var enabled = AcrylicEnabledCheckBox.IsChecked == true;
        var opacityPercent = GetSelectedAcrylicOpacity();
        var themePreference = GetSelectedThemePreference();
        _hasUnsavedAcrylicPreview = enabled != _savedAcrylicEnabled
            || opacityPercent != _savedAcrylicOpacityPercent
            || themePreference != _savedThemePreference;
        ThemeManager.SetAppearanceOptions(enabled, opacityPercent, themePreference);
    }

    private void UpdateAcrylicOpacityPanelState()
    {
        if (AcrylicOpacityPanel is not null)
        {
            AcrylicOpacityPanel.IsEnabled = _settingsLoaded
                && !_isBusy
                && AcrylicEnabledCheckBox.IsChecked == true;
        }
    }

    private int GetSelectedAcrylicOpacity()
    {
        return Math.Clamp(
            (int)Math.Round(AcrylicOpacitySlider.Value),
            ThemeManager.MinAcrylicOpacityPercent,
            ThemeManager.MaxAcrylicOpacityPercent);
    }

    private AppThemePreference GetSelectedThemePreference()
    {
        return ThemePreferenceComboBox.SelectedIndex switch
        {
            (int)AppThemePreference.Light => AppThemePreference.Light,
            (int)AppThemePreference.Dark => AppThemePreference.Dark,
            _ => AppThemePreference.System
        };
    }

    private string GetSelectedDayViewLayout()
    {
        return DayViewLayoutComboBox.SelectedIndex == 1
            ? DayViewLayouts.Timeline
            : DayViewLayouts.List;
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        if (_hasUnsavedAcrylicPreview)
        {
            ThemeManager.SetAppearanceOptions(
                _savedAcrylicEnabled,
                _savedAcrylicOpacityPercent,
                _savedThemePreference);
            _hasUnsavedAcrylicPreview = false;
        }

        _isClosed = true;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        Loaded -= SettingsWindow_Loaded;
        Closing -= SettingsWindow_Closing;
        Closed -= SettingsWindow_Closed;
    }
}
