using HydroNet.Core;

namespace HydroNet.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DispatcherForm(new ScreenStateStore()));
    }
}

internal sealed class DispatcherForm : Form
{
    private readonly ScreenStateStore screenStore;
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 18F) };

    public DispatcherForm(ScreenStateStore screenStore)
    {
        this.screenStore = screenStore;
        Text = "HydroNet.Control — Диспетчерская водоканала";
        AccessibleName = "Пульт диспетчера HydroNet.Control";
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Controls.Add(status);
        Load += (_, _) => OpenOnSecondMonitor();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private void OpenOnSecondMonitor()
    {
        var monitors = Screen.AllScreens;
        var target = monitors.Length > 1 ? monitors[1] : monitors[0];
        StartPosition = FormStartPosition.Manual;
        Location = target.WorkingArea.Location;
        Size = target.WorkingArea.Size;
        screenStore.Set(new ScreenState("dispatcher", target.DeviceName, "PS-01"));
        status.Text = $"HydroNet.Control\n{target.DeviceName}\nВыбран объект: PS-01";
    }
}
