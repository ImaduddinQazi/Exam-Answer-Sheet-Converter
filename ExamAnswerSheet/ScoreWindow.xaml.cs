using System.Windows;

namespace ExamAnswerSheet;

public partial class ScoreWindow : Window
{
    public ScoreWindow(string summary, List<ScoreRow> rows)
    {
        InitializeComponent();
        SummaryBlock.Text = summary;
        RowsList.ItemsSource = rows;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
