using Velopack;

namespace RaceIngenior;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before WPF starts so install/update hooks work without UI overhead.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
