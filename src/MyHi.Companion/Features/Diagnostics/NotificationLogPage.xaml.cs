namespace MyHi.Companion.Features.Diagnostics;

public partial class NotificationLogPage : ContentPage
{
    private readonly NotificationLogViewModel _viewModel;

    public NotificationLogPage(NotificationLogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnChannelToggled(object? sender, ToggledEventArgs e)
    {
        if (sender is Switch { BindingContext: NotificationChannel channel })
        {
            _viewModel.ToggleChannelCommand.Execute(channel);
        }
    }
}
