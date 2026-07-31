namespace MyHi.Companion.Features.Diagnostics;

public partial class ProbeChecklistPage : ContentPage
{
    public ProbeChecklistPage(ProbeChecklistViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ProbeChecklistViewModel vm)
        {
            vm.LoadCommand.Execute(null);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ProbeChecklistViewModel vm)
        {
            vm.SaveCommand.Execute(null);
        }
    }
}
