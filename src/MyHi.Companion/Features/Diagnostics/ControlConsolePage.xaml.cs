namespace MyHi.Companion.Features.Diagnostics;

public partial class ControlConsolePage : ContentPage
{
    public ControlConsolePage(ControlConsoleViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ControlConsoleViewModel vm)
        {
            vm.EnterCommand.Execute(null);
        }
    }
}
