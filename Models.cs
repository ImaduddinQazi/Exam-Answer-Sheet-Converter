using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExamAnswerSheet;

// =====================================================================
//  config.json  (layout of the exam; edit without recompiling)
// =====================================================================
public class ExamConfig
{
    public string Title { get; set; } = "Practice exam";
    /// <summary>Total questions INCLUDING the PBQs (PBQs are Q1-Q5, the grid starts after them).</summary>
    public int TotalQuestions { get; set; } = 90;
    public int TimerMinutes { get; set; } = 90;
    public int WarnMinutes { get; set; } = 10;
    public string OptionLetters { get; set; } = "ABCDE";
    public List<int> MultiQuestions { get; set; } = new() { 30, 66, 89 };
    public List<PbqGroupDef> Pbq { get; set; } = new();

    /// <summary>Built-in layout, used when config.json is missing or broken.</summary>
    public static ExamConfig CreateDefault()
    {
        static PbqItemDef L(string id, string letters) => new() { Id = id, Type = "letter", Letters = letters };
        static PbqItemDef AD(string id) => new() { Id = id, Type = "allowdeny" };

        return new ExamConfig
        {
            Title = "CompTIA Security+ practice exam",
            Pbq = new List<PbqGroupDef>
            {
                new() { Number = 1, Format = "answer-id", Items = new List<PbqItemDef>
                    { L("1a","ABCDE"), L("1b","ABCDE"), L("1c","ABCDE"), L("1d","ABCDE"), L("1e","ABCDE") } },
                new() { Number = 2, Format = "id-answer", Items = new List<PbqItemDef>
                    { L("2a","ABCD"), L("2b","ABCD"), L("2c","ABCD"), L("2d","ABCD") } },
                new() { Number = 3, Format = "inline", Items = new List<PbqItemDef>
                    { new() { Id = "3", Type = "order", Letters = "ABCDEF" } } },
                new() { Number = 4, Format = "id-answer", Items = new List<PbqItemDef>
                    { AD("4a"), AD("4b"), AD("4c"), L("4d","ABCD") } },
                new() { Number = 5, Format = "answer-id", Items = new List<PbqItemDef>
                    { L("5a","ABCDE"), L("5b","ABCDE"), L("5c","ABCDE"), L("5d","ABCDE"), L("5e","ABCDE") } },
            }
        };
    }
}

public class PbqGroupDef
{
    public int Number { get; set; }
    /// <summary>"answer-id" -> C-1a | "id-answer" -> 2a-B | "inline" -> 3.CEFADB</summary>
    public string Format { get; set; } = "answer-id";
    public List<PbqItemDef> Items { get; set; } = new();
}

public class PbqItemDef
{
    public string Id { get; set; } = "";
    /// <summary>"letter" | "order" | "allowdeny"</summary>
    public string Type { get; set; } = "letter";
    public string Letters { get; set; } = "ABCDE";
}

// =====================================================================
//  Auto-save file (%AppData%\ExamAnswerSheet\state.json)
// =====================================================================
public class ItemState
{
    public string Answer { get; set; } = "";
    public bool Idk { get; set; }
    public bool Maybe { get; set; }
    public bool Multi { get; set; }
}

public class AppState
{
    public Dictionary<string, ItemState> Items { get; set; } = new();
    public double ElapsedSeconds { get; set; }
    public bool Finished { get; set; }
    public int TimeUsedSeconds { get; set; }
    public string SavedPath { get; set; } = "";
    public bool Dark { get; set; }
}

// =====================================================================
//  key.json  (optional scoring)
// =====================================================================
public class KeyFile
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Answers { get; set; } = new();
}

public class ScoreRow
{
    public string Id { get; set; } = "";
    public string Yours { get; set; } = "";
    public string Correct { get; set; } = "";
    public string Result { get; set; } = "";
    public string Flag { get; set; } = "";
}

// =====================================================================
//  View-models
// =====================================================================
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One A/B/C... toggle button.</summary>
public class OptionVM : Observable
{
    private bool _isSelected;
    public OptionVM(AnswerItem owner, string letter) { Owner = owner; Letter = letter; }
    public AnswerItem Owner { get; }
    public string Letter { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public enum ItemKind { Letter, Order, AllowDeny }

/// <summary>
/// One answerable thing: a grid question (6, 7, 8 ...) or a PBQ sub-item (1a, 2c, 3, 4a ...).
/// </summary>
public class AnswerItem : Observable
{
    public static readonly string[] AllowDenyChoices = { "", "Allow", "Deny" };

    private bool _multi, _idk, _maybe, _isLocked;
    private string _orderText = "";
    private string _allowDeny = "";

    /// <summary>Raised after every user change (used for stats + auto-save).</summary>
    public event Action? Changed;

    public AnswerItem(string id, ItemKind kind, string letters, bool multi, bool hasFlags, int number)
    {
        Id = id;
        Kind = kind;
        Number = number;          // > 0 for grid questions, 0 for PBQ sub-items
        HasFlags = hasFlags;
        DefaultMulti = multi;
        _multi = multi;
        if (kind != ItemKind.AllowDeny)
            foreach (char c in letters)
                Options.Add(new OptionVM(this, c.ToString()));
    }

    public string Id { get; }
    public string Label => Id;
    public int Number { get; }
    public ItemKind Kind { get; }
    public bool HasFlags { get; }
    public bool DefaultMulti { get; }
    public ObservableCollection<OptionVM> Options { get; } = new();

    // Handy flags for XAML visibility bindings
    public bool IsLetter => Kind == ItemKind.Letter;
    public bool IsOrder => Kind == ItemKind.Order;
    public bool IsAllowDeny => Kind == ItemKind.AllowDeny;

    /// <summary>Key used in state.json (grid questions and PBQ ids cannot clash).</summary>
    public string StateKey => (Number > 0 ? "q" : "p") + Id;

    public bool IsLocked
    {
        get => _isLocked;
        set { if (Set(ref _isLocked, value)) Raise(nameof(IsEditable)); }
    }
    public bool IsEditable => !_isLocked;

    public bool Multi
    {
        get => _multi;
        set
        {
            if (_multi == value) return;
            _multi = value;
            Raise();
            if (!value) TrimToSingle(); else Changed?.Invoke();
        }
    }

    public bool Idk
    {
        get => _idk;
        set { if (Set(ref _idk, value)) Changed?.Invoke(); }
    }

    public bool Maybe
    {
        get => _maybe;
        set { if (Set(ref _maybe, value)) Changed?.Invoke(); }
    }

    /// <summary>Sequence for "order" items, e.g. CEFADB.</summary>
    public string OrderText
    {
        get => _orderText;
        set
        {
            value = (value ?? "").ToUpperInvariant();
            if (!Set(ref _orderText, value)) return;
            RefreshOrderOptions();
            AfterChange();
        }
    }

    /// <summary>"", "Allow" or "Deny".</summary>
    public string AllowDeny
    {
        get => _allowDeny;
        set
        {
            value ??= "";
            if (Set(ref _allowDeny, value)) AfterChange();
        }
    }

    /// <summary>The answer exactly as it is exported ("A C", "CEFADB", "allow", "").</summary>
    public string Answer
    {
        get
        {
            switch (Kind)
            {
                case ItemKind.Order: return _orderText;
                case ItemKind.AllowDeny: return _allowDeny.ToLowerInvariant();
                default: return string.Join(" ", Options.Where(o => o.IsSelected).Select(o => o.Letter));
            }
        }
    }

    public bool IsAnswered => Answer.Length > 0;

    // ---------------- user actions ----------------

    /// <summary>A letter button was clicked (or a letter key pressed).</summary>
    public void Pick(string letter)
    {
        if (_isLocked) return;
        switch (Kind)
        {
            case ItemKind.Letter:
                var opt = Options.FirstOrDefault(o => o.Letter == letter);
                if (opt == null) return;
                if (opt.IsSelected)
                {
                    opt.IsSelected = false;               // clicking the selected option clears it
                }
                else
                {
                    if (!_multi)
                        foreach (var o in Options) o.IsSelected = false;
                    opt.IsSelected = true;
                }
                AfterChange();
                break;

            case ItemKind.Order:
                // Clicking a letter appends it; clicking a letter already used removes it.
                int idx = _orderText.IndexOf(letter, StringComparison.Ordinal);
                OrderText = idx >= 0 ? _orderText.Remove(idx, 1) : _orderText + letter;
                break;
        }
    }

    public void BackspaceOrder()
    {
        if (_isLocked || _orderText.Length == 0) return;
        OrderText = _orderText.Substring(0, _orderText.Length - 1);
    }

    /// <summary>Delete key: clear the answer and both flags (Multi setting is kept).</summary>
    public void Clear()
    {
        if (_isLocked) return;
        ResetCore(keepMulti: true);
        AfterChange();
    }

    /// <summary>New exam: clear everything, including the Multi setting.</summary>
    public void ResetAll()
    {
        ResetCore(keepMulti: false);
        AfterChange();
    }

    // ---------------- persistence ----------------

    public ItemState ToState() => new() { Answer = Answer, Idk = _idk, Maybe = _maybe, Multi = _multi };

    public void LoadState(ItemState s)
    {
        foreach (var o in Options) o.IsSelected = false;
        _multi = s.Multi;
        _idk = s.Idk;
        _maybe = s.Maybe;
        string a = s.Answer ?? "";
        switch (Kind)
        {
            case ItemKind.Order:
                _orderText = a.ToUpperInvariant();
                RefreshOrderOptions();
                break;
            case ItemKind.AllowDeny:
                _allowDeny = AllowDenyChoices.FirstOrDefault(c => string.Equals(c, a, StringComparison.OrdinalIgnoreCase)) ?? "";
                break;
            default:
                var picked = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var o in Options) o.IsSelected = picked.Contains(o.Letter);
                break;
        }
        Raise(nameof(Multi)); Raise(nameof(Idk)); Raise(nameof(Maybe));
        Raise(nameof(OrderText)); Raise(nameof(AllowDeny));
        Raise(nameof(Answer)); Raise(nameof(IsAnswered));
    }

    // ---------------- scoring helper ----------------

    /// <summary>Turns an answer (yours or from key.json) into a comparable form.</summary>
    public string NormalizeKey(string? raw)
    {
        raw ??= "";
        switch (Kind)
        {
            case ItemKind.Order:
                return new string(raw.ToUpperInvariant().Where(c => char.IsLetter(c)).ToArray());
            case ItemKind.AllowDeny:
                return raw.Trim().ToLowerInvariant();
            default:   // letters: order does not matter ("C A" == "A C")
                return string.Join(" ", raw.ToUpperInvariant().Where(c => char.IsLetter(c)).Distinct().OrderBy(c => c));
        }
    }

    // ---------------- internals ----------------

    private void TrimToSingle()
    {
        bool keep = true;
        foreach (var o in Options)
        {
            if (!o.IsSelected) continue;
            if (keep) keep = false; else o.IsSelected = false;
        }
        AfterChange();
    }

    private void RefreshOrderOptions()
    {
        foreach (var o in Options) o.IsSelected = _orderText.Contains(o.Letter, StringComparison.Ordinal);
    }

    private void ResetCore(bool keepMulti)
    {
        foreach (var o in Options) o.IsSelected = false;
        _orderText = "";
        _allowDeny = "";
        _idk = false;
        _maybe = false;
        if (!keepMulti) _multi = DefaultMulti;
        Raise(nameof(OrderText)); Raise(nameof(AllowDeny));
        Raise(nameof(Idk)); Raise(nameof(Maybe)); Raise(nameof(Multi));
    }

    private void AfterChange()
    {
        Raise(nameof(Answer));
        Raise(nameof(IsAnswered));
        Changed?.Invoke();
    }
}

/// <summary>A PBQ block (Q1..Q5) made of several sub-items.</summary>
public class PbqGroup
{
    public PbqGroup(int number, string format, List<AnswerItem> items)
    {
        Number = number;
        Format = format;
        Items = items;
    }
    public int Number { get; }
    public string Format { get; }
    public List<AnswerItem> Items { get; }
    public string Title => "Question " + Number;
    public bool IsAnswered => Items.Count > 0 && Items.All(i => i.IsAnswered);
}
