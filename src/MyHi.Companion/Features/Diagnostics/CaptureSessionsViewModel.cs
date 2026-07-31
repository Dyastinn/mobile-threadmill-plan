using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Diagnostics;

public sealed partial class CaptureSessionsViewModel : BaseViewModel
{
    private readonly CaptureSessionManager _captures;

    public CaptureSessionsViewModel(CaptureSessionManager captures)
    {
        _captures = captures;
        _captures.SessionChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<CaptureSessionSummary> Sessions { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Sessions.Clear();
        foreach (var session in _captures.ListSessions())
        {
            Sessions.Add(session);
        }
    }

    [RelayCommand]
    private void StartNewSession()
    {
        _captures.StartNewSession();
        Refresh();
    }

    [RelayCommand]
    private static async Task ShareAsync(CaptureSessionSummary session) =>
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = session.FileName,
            File = new ShareFile(session.FilePath),
        });

    [RelayCommand]
    private async Task DeleteAsync(CaptureSessionSummary session)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlertAsync("Delete session", $"Delete {session.FileName}? This cannot be undone.", "Delete", "Cancel");
        if (confirmed)
        {
            _captures.DeleteSession(session.FilePath);
        }
    }
}
