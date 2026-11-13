using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExamAnswerSheet;

/// <summary>File handling, TXT export and scoring (no UI code here).</summary>
public static class ExamIO
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Folder that holds the exe (and therefore config.json / key.json).</summary>
    public static readonly string AppDir = ComputeAppDir();

    private static string ComputeAppDir()
    {
        string? p = Environment.ProcessPath;
        // When started with "dotnet run" the process is dotnet.exe -> use the build folder instead.
        if (!string.IsNullOrEmpty(p) &&
            !Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(p)!;
        return AppContext.BaseDirectory;
    }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExamAnswerSheet");

    public static string StatePath => Path.Combine(DataDir, "state.json");

    public static string ExamsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExamAnswerSheet");

    /// <summary>Set when config.json exists but could not be read.</summary>
    public static string? ConfigWarning { get; private set; }

    // ------------------------------------------------------------ config

    public static ExamConfig LoadConfig()
    {
        string path = Path.Combine(AppDir, "config.json");
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<ExamConfig>(File.ReadAllText(path), JsonOpts);
                if (cfg != null) return Sanitize(cfg);
            }
            else
            {
                try { File.WriteAllText(path, JsonSerializer.Serialize(ExamConfig.CreateDefault(), JsonOpts)); }
                catch { /* read-only folder: ignore */ }
            }
        }
        catch (Exception ex)
        {
            ConfigWarning = ex.Message;
        }
        return ExamConfig.CreateDefault();
    }

    private static ExamConfig Sanitize(ExamConfig c)
    {
        if (c.TotalQuestions < 1) c.TotalQuestions = 90;
        if (c.TimerMinutes < 1) c.TimerMinutes = 90;
        if (c.WarnMinutes < 0) c.WarnMinutes = 10;
        if (string.IsNullOrWhiteSpace(c.OptionLetters)) c.OptionLetters = "ABCDE";
        c.OptionLetters = new string(c.OptionLetters.ToUpperInvariant().Where(ch => char.IsLetter(ch)).Distinct().ToArray());
        c.MultiQuestions ??= new();
        c.Pbq ??= new();
        foreach (var g in c.Pbq)
        {
            g.Format = (g.Format ?? "answer-id").Trim().ToLowerInvariant();
            g.Items ??= new();
            foreach (var d in g.Items)
            {
                d.Id ??= "";
                d.Type = (d.Type ?? "letter").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(d.Letters)) d.Letters = "ABCDE";
                d.Letters = d.Letters.ToUpperInvariant().Replace(" ", "");
            }
        }
        return c;
    }

    // ------------------------------------------------------------ auto-save state

    public static AppState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), JsonOpts) ?? new AppState();
        }
        catch { /* corrupt file: start fresh */ }
        return new AppState();
    }

    /// <summary>Writes to a temp file first, then swaps it in, so a crash cannot corrupt the save.</summary>
    public static bool SaveState(AppState state)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOpts));
            File.Move(tmp, StatePath, true);
            return true;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------ export

    public static string FormatTimeUsed(int seconds) =>
        $"Time used: {seconds / 60} min {seconds % 60} sec";

    /// <summary>Builds the exact TXT format that goes to the tutor.</summary>
    public static string BuildExportText(IEnumerable<PbqGroup> groups, IEnumerable<AnswerItem> questions, int? timeUsedSeconds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("idk- means, idk question, concept and the answers and options.");
        sb.AppendLine("maybe- means, I am not sure about the selected option.");
        sb.AppendLine();

        foreach (var g in groups.OrderBy(x => x.Number))
        {
            if (g.Format == "inline" && g.Items.Count == 1)
            {
                sb.AppendLine($"{g.Number}.{g.Items[0].Answer}");          // 3.CEFADB
                continue;
            }
            sb.AppendLine($"{g.Number}.");
            foreach (var it in g.Items)
            {
                sb.AppendLine(g.Format == "id-answer"
                    ? $"{it.Id}-{it.Answer}"                               // 2a-B
                    : $"{it.Answer}-{it.Id}");                             // C-1a
            }
        }

        foreach (var q in questions)
        {
            string line = $"{q.Id}.{q.Answer}";                            // 6.B   30.A C   7.
            if (q.Idk) line += " IDK";
            if (q.Maybe) line += " MAYBE";
            sb.AppendLine(line);
        }

        if (timeUsedSeconds.HasValue)
            sb.AppendLine(FormatTimeUsed(timeUsedSeconds.Value));

        return sb.ToString();
    }

    /// <summary>Saves to Documents\ExamAnswerSheet\Exam_YYYY-MM-DD_HH-mm.txt and returns the full path.</summary>
    public static string SaveFinishedExam(string text)
    {
        Directory.CreateDirectory(ExamsFolder);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);
        string path = Path.Combine(ExamsFolder, $"Exam_{stamp}.txt");
        int n = 2;
        while (File.Exists(path))                                          // never overwrite an earlier exam
            path = Path.Combine(ExamsFolder, $"Exam_{stamp}_{n++}.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    // ------------------------------------------------------------ scoring

    public static Dictionary<string, string> LoadKey(string path)
    {
        var kf = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(path), JsonOpts) ?? new KeyFile();
        return new Dictionary<string, string>(kf.Answers, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Tally
    {
        public int N, Ok;
        public void Add(bool ok) { N++; if (ok) Ok++; }
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static (string Summary, List<ScoreRow> Rows) Score(IEnumerable<AnswerItem> items, Dictionary<string, string> key)
    {
        var rows = new List<ScoreRow>();
        var idk = new Tally();
        var maybe = new Tally();
        var plain = new Tally();       // answered, no flag
        int total = 0, correct = 0, unanswered = 0;

        foreach (var it in items)
        {
            if (!key.TryGetValue(it.Id, out var expected)) continue;       // only items present in the key are scored
            total++;
            string mine = it.NormalizeKey(it.Answer);
            bool answered = mine.Length > 0;
            bool ok = answered && mine == it.NormalizeKey(expected);
            if (ok) correct++;
            if (!answered) unanswered++;

            if (it.Idk) idk.Add(ok);
            if (it.Maybe) maybe.Add(ok);
            if (!it.Idk && !it.Maybe && answered) plain.Add(ok);

            rows.Add(new ScoreRow
            {
                Id = it.Id,
                Yours = answered ? it.Answer : "—",
                Correct = expected,
                Result = !answered ? "Unanswered" : ok ? "Correct" : "Wrong",
                Flag = it.Idk && it.Maybe ? "IDK + MAYBE" : it.Idk ? "IDK" : it.Maybe ? "MAYBE" : ""
            });
        }

        string summary;
        if (total == 0)
        {
            summary = "None of the question ids in the key match this exam. Check key.json.";
        }
        else
        {
            summary =
                $"Correct: {correct} of {total}  ({Pct(correct, total)})\n" +
                $"Wrong: {total - correct - unanswered}    Unanswered: {unanswered}\n\n" +
                $"IDK-flagged:        {idk.N} questions, {idk.Ok} correct ({Pct(idk.Ok, idk.N)})\n" +
                $"MAYBE-flagged:      {maybe.N} questions, {maybe.Ok} correct ({Pct(maybe.Ok, maybe.N)})\n" +
                $"Unflagged answers:  {plain.N} questions, {plain.Ok} correct ({Pct(plain.Ok, plain.N)})";
        }
        return (summary, rows);
    }
}
