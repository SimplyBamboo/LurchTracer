namespace LurchTracer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) => ShowFatalError(eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                if (eventArgs.ExceptionObject is Exception error)
                    WriteCrashLog(error);
            };

            Application.Run(new MainWindow());
        }
        catch (Exception error)
        {
            ShowFatalError(error);
        }
    }

    internal static void ShowFatalError(Exception error)
    {
        WriteCrashLog(error);

        try
        {
            MessageBox.Show(
                $"Lurch Tracer could not start.\n\n{error.Message}\n\nA crash log was saved to:\n{CrashLogPath}",
                "Lurch Tracer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
        }
    }

    private static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LurchTracer",
        "crash.log");

    private static void WriteCrashLog(Exception error)
    {
        try
        {
            string? folder = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(
                CrashLogPath,
                $"{DateTime.Now:O}{Environment.NewLine}{error}");
        }
        catch
        {
        }
    }
}
