using Game.UI;

namespace Game;

internal static class Program
{
    /// <summary>Single-threaded WinForms; required for forms and GDI+.</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new GameForm());
    }
}
