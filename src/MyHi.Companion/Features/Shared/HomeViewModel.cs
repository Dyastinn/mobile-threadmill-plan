using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MyHi.Companion.Features.Shared;

public sealed partial class HomeViewModel : BaseViewModel
{
    private readonly RollingFileLoggerProvider _fileLogger;

    public HomeViewModel(RollingFileLoggerProvider fileLogger)
    {
        _fileLogger = fileLogger;
        Destinations =
        [
            new NavDestination("Scan", "Find the treadmill; RSSI, filter, raw advertisement", "scan"),
            new NavDestination("GATT Tree", "Every service, characteristic, and property", "gatttree"),
            new NavDestination("Read Dump", "Dump every readable characteristic as hex", "readdump"),
            new NavDestination("Notification Log", "Live timestamp | uuid | hex, with rate and flags tracker", "notiflog"),
            new NavDestination("Control Console", "Send bytes to 0x2AD9 and see the decoded response", "controlconsole"),
            new NavDestination("Probe Checklist", "Parts A-G as a form; exports pasteable markdown", "checklist"),
            new NavDestination("Capture Sessions", "Session files: share, delete", "sessions"),
        ];
    }

    public IReadOnlyList<NavDestination> Destinations { get; }

    [ObservableProperty]
    private NavDestination? selectedDestination;

    /// <summary>
    /// Bound to the CollectionView's SelectedItem rather than a TapGestureRecognizer
    /// inside the item template — nested gesture recognizers are unreliable on
    /// Android's CollectionView (RecyclerView eats the touch before it reaches them).
    /// Reset to null immediately so tapping the same row again still navigates.
    /// </summary>
    partial void OnSelectedDestinationChanged(NavDestination? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedDestination = null;
        NavigateCommand.Execute(value.Route);
    }

    [RelayCommand]
    private static async Task NavigateAsync(string route) => await Shell.Current.GoToAsync(route);

    [RelayCommand]
    private async Task ShareLogAsync()
    {
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "MyHi Companion — app log",
            File = new ShareFile(_fileLogger.CurrentLogFilePath),
        });
    }
}
