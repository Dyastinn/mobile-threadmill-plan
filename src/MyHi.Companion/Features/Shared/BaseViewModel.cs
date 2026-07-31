using CommunityToolkit.Mvvm.ComponentModel;

namespace MyHi.Companion.Features.Shared;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;
}
