namespace MyHi.Companion.Features.Diagnostics;

public partial class GattTreePage : ContentPage
{
    public GattTreePage(GattTreeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is GattTreeViewModel vm)
        {
            vm.RefreshCommand.Execute(null);
        }
    }
}
