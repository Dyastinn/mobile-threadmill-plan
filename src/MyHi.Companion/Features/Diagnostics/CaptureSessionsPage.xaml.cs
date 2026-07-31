namespace MyHi.Companion.Features.Diagnostics;

public partial class CaptureSessionsPage : ContentPage
{
    private readonly CaptureSessionsViewModel _viewModel;

    public CaptureSessionsPage(CaptureSessionsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshCommand.Execute(null);
    }

    private void OnShareClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: CaptureSessionSummary session })
        {
            _viewModel.ShareCommand.Execute(session);
        }
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: CaptureSessionSummary session })
        {
            _viewModel.DeleteCommand.Execute(session);
        }
    }
}
