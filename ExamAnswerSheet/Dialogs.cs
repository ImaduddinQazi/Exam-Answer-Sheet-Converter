using System.Windows;
using System.Windows.Controls;

namespace ExamAnswerSheet;

/// <summary>Small themed Yes/No dialog with custom button captions (MessageBox cannot do that).</summary>
public static class ChoiceDialog
{
    /// <returns>true if the right-hand (primary) button was chosen.</returns>
    public static bool Ask(Window owner, string title, string message, string yesText, string noText)
    {
        bool answer = false;

        var win = new Window
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 480,
            MaxHeight = 560,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 14
        };
        win.SetResourceReference(Control.BackgroundProperty, "WindowBg");
        win.SetResourceReference(Control.ForegroundProperty, "TextFg");

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFg");

        // long lists of unanswered numbers scroll instead of growing the dialog
        var scroll = new ScrollViewer
        {
            Content = text,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 18)
        };

        var yes = MakeButton(yesText, primary: true);
        var no = MakeButton(noText, primary: false);
        no.IsCancel = true;                                   // Esc = the safe choice
        yes.Click += (_, _) => { answer = true; win.DialogResult = true; };
        no.Click += (_, _) => { win.DialogResult = false; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(no);
        buttons.Children.Add(yes);

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(scroll);
        root.Children.Add(buttons);
        win.Content = root;

        win.ShowDialog();
        return answer;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        MinWidth = 120,
        Height = 40,
        Margin = new Thickness(8, 0, 0, 0),
        Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "ActionButton")
    };
}
