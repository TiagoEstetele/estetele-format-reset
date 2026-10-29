using System.IO;
using System.Diagnostics;
using System.Windows;

namespace Efr.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            int previewIndex = Array.IndexOf(e.Args, "--self-test-ui");
            bool preview = previewIndex >= 0 && previewIndex + 1 < e.Args.Length;
            bool demo = e.Args.Contains("--demo") || preview;
            var waitIndex = Array.IndexOf(e.Args, "--wait-pid");
            if (waitIndex >= 0 && waitIndex + 1 < e.Args.Length && int.TryParse(e.Args[waitIndex + 1], out var waitPid))
            {
                try { using var parent = Process.GetProcessById(waitPid); if (!parent.WaitForExit(15000)) throw new IOException("A janela anterior ainda está usando o histórico."); }
                catch (ArgumentException) { /* Parent already exited. */ }
            }
            string? expected = null;
            var index = Array.IndexOf(e.Args, "--expected-sid");
            if (index >= 0)
            {
                if (index + 1 >= e.Args.Length) throw new ArgumentException("SID de origem ausente.");
                expected = e.Args[index + 1];
            }
            var dataIndex = Array.IndexOf(e.Args, "--demo-directory");
            string? demoDirectory = preview ? Path.Combine(Path.GetFullPath(e.Args[previewIndex + 1]), "demo-history") : demo && dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? e.Args[dataIndex + 1] : null;
            var window = new MainWindow(demo, expected, demoDirectory);
            MainWindow = window;
            if (preview) { await window.SelfTestUiAsync(Path.GetFullPath(e.Args[previewIndex + 1])); window.Close(); Shutdown(0); }
            else window.Show();
        }
        catch (Exception ex)
        {
            int previewIndex = Array.IndexOf(e.Args, "--self-test-ui");
            if (previewIndex >= 0 && previewIndex + 1 < e.Args.Length)
            {
                var output = Path.GetFullPath(e.Args[previewIndex + 1]); Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"ui-failure.txt"),ex.ToString()); Shutdown(1); return;
            }
            MessageBox.Show("Não foi possível iniciar com segurança.\n\n" + ex.Message + "\n\nSe houver outra janela aberta, feche-a. Um histórico inválido não será sobrescrito.", "estetele-format-reset", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
