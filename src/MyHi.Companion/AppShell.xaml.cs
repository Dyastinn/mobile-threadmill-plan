using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Diagnostics;

namespace MyHi.Companion;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("scan", typeof(ScanPage));
        Routing.RegisterRoute("gatttree", typeof(GattTreePage));
        Routing.RegisterRoute("readdump", typeof(ReadDumpPage));
        Routing.RegisterRoute("notiflog", typeof(NotificationLogPage));
        Routing.RegisterRoute("controlconsole", typeof(ControlConsolePage));
        Routing.RegisterRoute("checklist", typeof(ProbeChecklistPage));
        Routing.RegisterRoute("sessions", typeof(CaptureSessionsPage));
    }
}
