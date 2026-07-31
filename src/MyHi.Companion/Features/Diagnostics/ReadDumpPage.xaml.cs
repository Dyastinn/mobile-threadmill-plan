namespace MyHi.Companion.Features.Diagnostics;

public partial class ReadDumpPage : ContentPage
{
    private readonly ReadDumpViewModel _viewModel;

    public ReadDumpPage(ReadDumpViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnCopyRowClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: ReadDumpRow row })
        {
            _viewModel.CopyRowCommand.Execute(row);
        }
    }
}
