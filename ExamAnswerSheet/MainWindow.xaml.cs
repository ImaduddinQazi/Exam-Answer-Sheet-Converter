using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ExamAnswerSheet;

public partial class MainWindow : Window
{
    // ---------------------------------------------------------------- data
    private ExamConfig _config = null!;
    private AppState _state = new();

    private readonly List<PbqGroup> _groups = new();       // PBQ blocks (Q1-Q5)
    private readonly List<AnswerItem> _questions = new();  // grid questions (6..90)
    private readonly List<AnswerItem> _allItems = new();   // PBQ sub-items + grid questions
    private readonly ObservableCollection<object> _entries = new();   // what the ListBox shows
    private ICollectionView _view = null!;
    private string _filter = "All";
    private Dictionary<string, string>? _key;               // loaded key.json

    // ---------------------------------------------------------------- timer
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _sw = new();
    private double _baseElapsed;       // seconds accumulated before the current run
    private bool _timeUpShown;
    private int _tickCount;

    // ---------------------------------------------------------------- auto-save
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _loading = true;      // true while building / restoring / clearing: no auto-save

    private double Elapsed => _baseElapsed + _sw.Elapsed.TotalSeconds;
    private double LimitSeconds => _config.TimerMinutes * 60.0;

    // =================================================================
    //  Start-up
    // =================================================================
    public MainWindow()
    {
        InitializeComponent();

        _config = ExamIO.LoadConfig();
        _state = ExamIO.LoadState();
        Title = "ExamAnswerSheet - " + _config.Title;

        ApplyTheme(_state.Dark);
        BuildItems();

        AnswerList.ItemsSource = _entries;
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = Pass;

        RestoreState();

        _tick.Tick += OnTick;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        _loading = false;
        UpdateAll();
        if (_state.Finished) ShowResults();

        Loaded += (_, _) =>
        {
            if (ExamIO.ConfigWarning != null)
                MessageBox.Show(this,
                    "config.json could not be read, so the built-in layout is used.\n\n" + ExamIO.ConfigWarning,
                    "ExamAnswerSheet", MessageBoxButton.OK, MessageBoxImage.Warning);

            // Put the keyboard focus on the first row so the shortcuts work straight away.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (AnswerList.Items.Count == 0) return;
                AnswerList.SelectedIndex = 0;
                (AnswerList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            }));
        };
    }

    /// <summary>Creates the PBQ groups and the grid questions from config.json.</summary>
    private void BuildItems()
    {
        int firstQuestion = 1;

        foreach (var gd in _config.Pbq.OrderBy(g => g.Number))
        {
            var items = new List<AnswerItem>();
            foreach (var d in gd.Items)
            {
                ItemKind kind = d.Type switch
                {
                    "order" => ItemKind.Order,
                    "allowdeny" or "allow/deny" or "allow-deny" => ItemKind.AllowDeny,
                    _ => ItemKind.Letter
                };
                items.Add(new AnswerItem(d.Id, kind, d.Letters, false, false, 0));
            }
            _groups.Add(new PbqGroup(gd.Number, gd.Format, items));
            firstQuestion = Math.Max(firstQuestion, gd.Number + 1);
        }

        // The PBQs are questions 1-5, so the normal grid starts after them (6..TotalQuestions).
        for (int n = firstQuestion; n <= _config.TotalQuestions; n++)
        {
            bool multi = _config.MultiQuestions.Contains(n);
            _questions.Add(new AnswerItem(n.ToString(), ItemKind.Letter, _config.OptionLetters, multi, true, n));
        }

        foreach (var g in _groups) _allItems.AddRange(g.Items);
        _allItems.AddRange(_questions);
        foreach (var it in _allItems) it.Changed += OnItemChanged;

        foreach (var g in _groups) _entries.Add(g);
        foreach (var q in _questions) _entries.Add(q);
    }

    /// <summary>Restores answers, timer and finished-state from %AppData%.</summary>
    private void RestoreState()
    {
        foreach (var it in _allItems)
            if (_state.Items.TryGetValue(it.StateKey, out var s))
                it.LoadState(s);

        _baseElapsed = _state.ElapsedSeconds;        // timer comes back paused
        _timeUpShown = Elapsed >= LimitSeconds;
        if (_state.Finished) LockAll(true);
    }

    // =================================================================
    //  Auto-save
    // =================================================================
    private void OnItemChanged()
    {
        if (_loading) return;
        UpdateStats();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_loading) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (_loading) return;
        var dict = new Dictionary<string, ItemState>();
        foreach (var it in _allItems) dict[it.StateKey] = it.ToState();
        _state.Items = dict;
        _state.ElapsedSeconds = Elapsed;
        bool ok = ExamIO.SaveState(_state);
        StatusText.Text = ok ? $"Auto-saved {DateTime.Now:HH:mm:ss}" : "Auto-save FAILED (check disk space / permissions)";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _tick.Stop();
        SaveNow();
    }

    // =================================================================
    //  Timer
    // =================================================================
    private void StartTimer()
    {
        if (_state.Finished || _sw.IsRunning || Elapsed >= LimitSeconds) return;
        _sw.Start();
        _tick.Start();
        UpdateTimerUi();
    }

    private void PauseTimer()
    {
        if (_sw.IsRunning)
        {
            _baseElapsed += _sw.Elapsed.TotalSeconds;
            _sw.Reset();
        }
        _tick.Stop();
        UpdateTimerUi();
        ScheduleSave();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartTimer();
    private void Pause_Click(object sender, RoutedEventArgs e) => PauseTimer();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Finished) return;
        _tick.Stop();
        _sw.Reset();
        _baseElapsed = 0;
        _timeUpShown = false;
        UpdateTimerUi();
        ScheduleSave();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        UpdateTimerUi();
        if (++_tickCount % 20 == 0) ScheduleSave();       // keep elapsed time saved (about every 5 s)

        if (Elapsed >= LimitSeconds && !_timeUpShown)
        {
            _timeUpShown = true;
            PauseTimer();
            _baseElapsed = LimitSeconds;
            UpdateTimerUi();
            SystemSounds.Exclamation.Play();

            bool finish = ChoiceDialog.Ask(this, "Time is up",
                "The timer reached 00:00.\n\nFinish the exam now? Your answers will be saved to a TXT file automatically.",
                "Finish exam", "Keep editing");
            if (finish) FinishExam(askAboutUnanswered: false);
        }
    }

    private static string Fmt(double seconds)
    {
        int t = (int)seconds;
        return $"{t / 60:00}:{t % 60:00}";
    }

    private void UpdateTimerUi()
    {
        double remaining = Math.Max(0, LimitSeconds - Elapsed);
        RemainingText.Text = Fmt(Math.Ceiling(remaining));
        ElapsedText.Text = Fmt(Math.Floor(Elapsed));

        bool warn = remaining <= _config.WarnMinutes * 60.0;           // warning colour at 10 min left
        RemainingText.SetResourceReference(TextBlock.ForegroundProperty, warn ? "WarnBr" : "TextFg");
        TimeUpText.Visibility = remaining <= 0 ? Visibility.Visible : Visibility.Collapsed;

        StartBtn.IsEnabled = !_state.Finished && !_sw.IsRunning && remaining > 0;
        PauseBtn.IsEnabled = _sw.IsRunning;
        ResetBtn.IsEnabled = !_state.Finished;
    }

    // =================================================================
    //  Stats / progress / filters
    // =================================================================
    private void UpdateAll()
    {
        UpdateStats();
        UpdateTimerUi();
        UpdateFinishUi();
    }

    private void UpdateStats()
    {
        int total = _groups.Count + _questions.Count;
        int answered = _groups.Count(g => g.IsAnswered) + _questions.Count(q => q.IsAnswered);
        AnsweredText.Text = $"Answered: {answered} / {total}";
        UnansweredText.Text = $"Unanswered: {total - answered}";
        IdkText.Text = $"IDK: {_questions.Count(q => q.Idk)}";
        MaybeText.Text = $"MAYBE: {_questions.Count(q => q.Maybe)}";
        Progress.Maximum = Math.Max(1, total);
        Progress.Value = answered;
    }

    private void UpdateFinishUi()
    {
        FinishBtn.Content = _state.Finished ? "VIEW RESULTS" : "FINISH EXAM";
        ModeText.Visibility = _state.Finished ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool Pass(object o)
    {
        if (_filter == "All") return true;
        if (o is PbqGroup g) return _filter == "Unanswered" && !g.IsAnswered;
        if (o is AnswerItem a)
        {
            return _filter switch
            {
                "Unanswered" => !a.IsAnswered,
                "IDK" => a.Idk,
                "MAYBE" => a.Maybe,
                _ => true
            };
        }
        return true;
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string f)
        {
            _filter = f;
            _view.Refresh();          // clicking the active chip again re-applies the filter
        }
    }

    // =================================================================
    //  Answer list: clicks + keyboard
    // =================================================================
    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is OptionVM opt)
            opt.Owner.Pick(opt.Letter);
    }

    private void OrderBack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AnswerItem item)
            item.BackspaceOrder();
    }

    /// <summary>Whatever row gets keyboard focus (click on a button, checkbox...) becomes the current row.</summary>
    private void AnswerList_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            ItemsControl.ContainerFromElement(AnswerList, d) is ListBoxItem lbi)
            lbi.IsSelected = true;
    }

    private void MoveSelection(int delta)
    {
        int count = AnswerList.Items.Count;
        if (count == 0) return;
        int i = Math.Clamp(AnswerList.SelectedIndex + delta, 0, count - 1);
        AnswerList.SelectedIndex = i;
        AnswerList.ScrollIntoView(AnswerList.SelectedItem);
        AnswerList.UpdateLayout();
        (AnswerList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem)?.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_state.Finished || Keyboard.Modifiers != ModifierKeys.None) return;   // locked: scrolling only

        var focused = Keyboard.FocusedElement;
        if (focused is TextBoxBase || focused is ComboBox || focused is ComboBoxItem) return;  // typing / dropdown
        if (!AnswerList.IsKeyboardFocusWithin) return;

        if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; return; }
        if (e.Key == Key.Down) { MoveSelection(+1); e.Handled = true; return; }

        if (AnswerList.SelectedItem is not AnswerItem cur) return;

        if (e.Key >= Key.A && e.Key <= Key.E)
        {
            cur.Pick(((char)('A' + (e.Key - Key.A))).ToString());
            e.Handled = true;
        }
        else if (e.Key == Key.I) { cur.Idk = !cur.Idk; e.Handled = true; }
        else if (e.Key == Key.M) { cur.Maybe = !cur.Maybe; e.Handled = true; }
        else if (e.Key == Key.Delete) { cur.Clear(); e.Handled = true; }
    }

    // =================================================================
    //  FINISH EXAM
    // =================================================================
    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Finished) { ShowResults(); return; }
        FinishExam(askAboutUnanswered: true);
    }

    private List<string> UnansweredLabels()
    {
        var list = new List<(int n, string label)>();
        foreach (var g in _groups.Where(x => !x.IsAnswered)) list.Add((g.Number, g.Number + " (PBQ)"));
        foreach (var q in _questions.Where(x => !x.IsAnswered)) list.Add((q.Number, q.Id));
        return list.OrderBy(t => t.n).Select(t => t.label).ToList();
    }

    private void FinishExam(bool askAboutUnanswered)
    {
        // 1. stop the timer (remember if it was running, in case the user goes back)
        bool wasRunning = _sw.IsRunning;
        PauseTimer();

        // 2. unanswered questions -> confirm first
        if (askAboutUnanswered)
        {
            var missing = UnansweredLabels();
            if (missing.Count > 0)
            {
                string msg = $"You have {missing.Count} unanswered question{(missing.Count == 1 ? "" : "s")} " +
                             $"({string.Join(", ", missing)}). Finish anyway?";
                if (!ChoiceDialog.Ask(this, "Finish exam?", msg, "Finish anyway", "Go back"))
                {
                    if (wasRunning) StartTimer();
                    return;
                }
            }
        }

        // 3. record the time used, build the TXT and save it automatically
        int used = (int)Math.Floor(Math.Min(Elapsed, LimitSeconds));
        string text = ExamIO.BuildExportText(_groups, _questions, used);
        string path;
        try
        {
            path = ExamIO.SaveFinishedExam(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "The result file could not be saved:\n" + ex.Message +
                "\n\nThe exam is NOT finished. Use File > Export to TXT to save it somewhere else.",
                "ExamAnswerSheet", MessageBoxButton.OK, MessageBoxImage.Error);
            if (wasRunning) StartTimer();
            return;
        }

        // 4. lock the grid and remember the result (so it survives a restart)
        _state.Finished = true;
        _state.TimeUsedSeconds = used;
        _state.SavedPath = path;
        LockAll(true);
        UpdateAll();
        SaveNow();

        // 5. results panel
        ShowResults();
    }

    private void LockAll(bool locked)
    {
        foreach (var it in _allItems) it.IsLocked = locked;
    }

    private void ShowResults()
    {
        int total = _groups.Count + _questions.Count;
        int answered = _groups.Count(g => g.IsAnswered) + _questions.Count(q => q.IsAnswered);

        PathText.Text = _state.SavedPath;
        SummaryText.Text =
            $"Answered: {answered} of {total}\n" +
            $"Unanswered: {total - answered}\n" +
            $"IDK: {_questions.Count(q => q.Idk)}\n" +
            $"MAYBE: {_questions.Count(q => q.Maybe)}\n" +
            ExamIO.FormatTimeUsed(_state.TimeUsedSeconds);
        ResultsStatus.Text = "";
        ResultsOverlay.Visibility = Visibility.Visible;
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        ResultsOverlay.Visibility = Visibility.Collapsed;   // grid stays locked: scrolling only
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_state.SavedPath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "Could not open the file:\n" + ex.Message, "ExamAnswerSheet"); }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_state.SavedPath))
                Process.Start("explorer.exe", $"/select,\"{_state.SavedPath}\"");
            else
                Process.Start(new ProcessStartInfo(ExamIO.ExamsFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not open the folder:\n" + ex.Message, "ExamAnswerSheet"); }
    }

    private void CopyResults_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = File.Exists(_state.SavedPath) ? File.ReadAllText(_state.SavedPath) : BuildText(); }
        catch { text = BuildText(); }
        if (CopyText(text)) ResultsStatus.Text = "Copied to clipboard.";
    }

    // =================================================================
    //  NEW EXAM
    // =================================================================
    private void NewExam_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        if (!ChoiceDialog.Ask(this, "New exam",
                "This clears all answers, flags and the timer.\nThis cannot be undone. Continue?",
                "Clear everything", "Cancel"))
            return;

        _tick.Stop();
        _sw.Reset();
        _baseElapsed = 0;
        _timeUpShown = false;

        _loading = true;                                   // no auto-save while clearing
        foreach (var it in _allItems) it.ResetAll();
        _state.Finished = false;
        _state.TimeUsedSeconds = 0;
        _state.SavedPath = "";
        LockAll(false);
        _loading = false;

        ResultsOverlay.Visibility = Visibility.Collapsed;
        _filter = "All";
        FilterAll.IsChecked = true;
        _view.Refresh();

        UpdateAll();
        SaveNow();
    }

    // =================================================================
    //  File menu: manual export, clipboard, key / score
    // =================================================================
    private void MenuBtn_Click(object sender, RoutedEventArgs e) => MenuPopup.IsOpen = true;

    private string BuildText() =>
        ExamIO.BuildExportText(_groups, _questions, _state.Finished ? _state.TimeUsedSeconds : (int?)null);

    private bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = "Copied to clipboard.";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not copy to the clipboard:\n" + ex.Message, "ExamAnswerSheet");
            return false;
        }
    }

    /// <summary>Backup export: lets you pick the location, then offers to copy to the clipboard.</summary>
    private void Menu_Export(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        var dlg = new SaveFileDialog
        {
            Title = "Export to TXT",
            Filter = "Text file (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = $"Exam_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt",
            InitialDirectory = ExamIO.ExamsFolder
        };
        if (dlg.ShowDialog(this) != true) return;

        string text = BuildText();
        try { File.WriteAllText(dlg.FileName, text, new UTF8Encoding(false)); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save the file:\n" + ex.Message, "ExamAnswerSheet",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (ChoiceDialog.Ask(this, "Export complete",
                $"Saved to:\n{dlg.FileName}\n\nCopy the text to the clipboard as well?",
                "Copy to clipboard", "No thanks"))
            CopyText(text);
    }

    private void Menu_Copy(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        CopyText(BuildText());
    }

    private void Menu_OpenExamsFolder(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        try
        {
            Directory.CreateDirectory(ExamIO.ExamsFolder);
            Process.Start(new ProcessStartInfo(ExamIO.ExamsFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ExamAnswerSheet"); }
    }

    private void Menu_LoadKey(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        var dlg = new OpenFileDialog
        {
            Title = "Load answer key",
            Filter = "Answer key (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = ExamIO.AppDir
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _key = ExamIO.LoadKey(dlg.FileName);
            StatusText.Text = $"Answer key loaded ({_key.Count} answers).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The key file could not be read:\n" + ex.Message, "ExamAnswerSheet",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Menu_Score(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        if (_key == null)
        {
            MessageBox.Show(this, "Load an answer key first (File > Load answer key, or the \"Load key\" button).",
                "ExamAnswerSheet");
            return;
        }
        var (summary, rows) = ExamIO.Score(_allItems, _key);
        new ScoreWindow(summary, rows) { Owner = this }.ShowDialog();
    }

    // =================================================================
    //  Theme
    // =================================================================
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_state.Dark);
        ScheduleSave();
    }

    private void ApplyTheme(bool dark)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri(dark ? "pack://application:,,,/Themes/Dark.xaml"
                                  : "pack://application:,,,/Themes/Light.xaml", UriKind.Absolute)
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);
        ThemeBtn.Content = dark ? "Light" : "Dark";
        _state.Dark = dark;
    }
}
