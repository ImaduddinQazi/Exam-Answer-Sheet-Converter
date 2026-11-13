using System.Windows;

namespace ExamAnswerSheet;

public partial class App : Application
{
    public App()
    {
        // Last-resort safety net: show the error instead of silently dying.
        // Answers are auto-saved on every change, so nothing is lost.
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(
                "Unexpected error: " + e.Exception.Message +
                "\n\nYour answers are auto-saved and will be restored on the next launch.",
                "ExamAnswerSheet", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
