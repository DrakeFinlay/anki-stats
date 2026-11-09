using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AnkiStats.Core;
using Microsoft.Win32;

namespace AnkiStats.App;

public partial class MainWindow : Window
{
    private LocalStore store = null!;
    private Settings? settings;
    private ImportedCollection? data;
    private AnalysisResult? overview;
    private AnalysisResult? reviews;
    private bool busy;
    private bool ready;
    private string? previewSignature;
    private CollectionMetadata? metadata;
    private readonly ObservableCollection<DeckChoice> deckChoices = [];
    private readonly ObservableCollection<MappingChoice> mappingChoices = [];
    private readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateOnly insightDate;
    private DateOnly calendarDate;
    private List<ReviewStreak> streaks = [];

    public MainWindow()
    {
        InitializeComponent();
        foreach (var column in LearningCardsGrid.Columns.Concat(LearningFindingsGrid.Columns).OfType<DataGridTextColumn>())
            column.ElementStyle = (Style)LearningPage.FindResource("LearningCell");
        OverviewMetrics.SizeChanged += (_, _) => OverviewMetrics.Columns = OverviewMetrics.ActualWidth < 900 ? 2 : 4;
        CalendarHeatmap.DaySelected += text => CalendarDay.Text = text;
        DeckGrid.ItemsSource = deckChoices;
        ProfilesGrid.ItemsSource = editorProfiles;
        MappingGrid.ItemsSource = mappingChoices;
        TimeZonePicker.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        TimeZonePicker.SelectedValue = TimeZoneInfo.Local.Id;
        Loaded += async (_, _) => await Initialize();
        Closed += (_, _) => dayTimer.Stop();
        Closed += (_, _) => mediaCancellation?.Cancel();
        dayTimer.Tick += async (_, _) =>
        {
            if (!busy && data is not null && Today(data.TimeZoneId) != insightDate)
                await Guard(() => { UpdateInsight(); return Task.CompletedTask; });
            if (!busy && data is not null && Streaks.StudyDay(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(data.TimeZoneId)) != calendarDate)
                await Guard(async () => { await UpdateCalendar(); await UpdateTimeAnalysis(); await UpdateLearningPatterns(); });
        };
    }

    private async Task Initialize()
    {
        ready = true;
        await Guard(async () =>
        {
            store = new LocalStore();
            InitializeMedia();
            UpdateSourceIndicator();
            try { settings = store.LoadSettings(); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { SetStatus("Saved settings could not be read. Reconnect your profile in Settings.", true); }
            if (settings is null || string.IsNullOrWhiteSpace(settings.ProfileFolder)) { ShowPage("Settings"); return; }
            ProfilePath.Text = settings.ProfileFolder;
            TimeZonePicker.SelectedValue = settings.TimeZoneId;
            try { importedData = await Task.Run(() => store.LoadCache(settings.CollectionKey)); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
            { SetStatus("The previous cache could not be read. Attempting a fresh import.", true); }
            if (importedData is not null)
            {
                PopulateSettings(importedData.Metadata, settings);
                await DisplayImport(importedData);
                SetStatus("Showing the last successful import while refreshing…");
            }
            await RefreshInternal();
        });
        dayTimer.Start();
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusPanel.Background = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(77, 45, 49) : System.Windows.Media.Color.FromRgb(40, 52, 70));
    }
    private void UpdateSourceIndicator()
    {
        var isolated = store.IsIsolated || data?.IsSynthetic == true;
        Title = isolated ? "Nakama — TEST DATA SESSION" : "Nakama";
        TestDataBanner.Visibility = isolated ? Visibility.Visible : Visibility.Collapsed;
        var source = data?.SourceProfileFolder;
        if (string.IsNullOrWhiteSpace(source)) source = settings?.ProfileFolder;
        SourceInfo.Text = string.IsNullOrWhiteSpace(source) ? "Source: no collection connected" : "Source: " + source;
        SourceInfo.ToolTip = "App data: " + store.Root;
    }
    private async Task Guard(Func<Task> action)
    {
        if (busy) return;
        busy = true; RefreshButton.IsEnabled = false; SettingsContent.IsEnabled = false;
        RangePicker.IsEnabled = KindPicker.IsEnabled = false;
        ActiveProfilePicker.IsEnabled = false;
        TimeRangePicker.IsEnabled = TimeGroupingPicker.IsEnabled = false;
        LearningControls.IsEnabled = false;
        CoverageControls.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try { await action(); }
        catch (Exception e)
        {
            var cached = data is null ? " No successful results are available yet." : " Showing the previous successful import, with its original scope and timestamp.";
            SetStatus(e.Message + cached, true);
            if (data is null) ShowPage("Settings");
        }
        finally
        {
            busy = false; RefreshButton.IsEnabled = true; SettingsContent.IsEnabled = true;
            RangePicker.IsEnabled = KindPicker.IsEnabled = true;
            ActiveProfilePicker.IsEnabled = profileViews.Count > 0;
            TimeRangePicker.IsEnabled = TimeGroupingPicker.IsEnabled = true;
            LearningControls.IsEnabled = true;
            CoverageControls.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }
    private CollectionReader ReaderFor(string profile)
    {
        var key = AnkiApiKey.Password;
        return new(store.TemporaryRoot, () => new AnkiConnectClient(key),
            allowLive: !store.IsIsolated && !File.Exists(Path.Combine(profile, ".ankistats-test-data")));
    }
    private async Task RefreshInternal()
    {
        if (settings is null) { ShowPage("Settings"); SetStatus("Choose a profile, select decks, and preview the field mappings first."); return; }
        SetStatus("Acquiring a safe private copy of the collection…");
        var progress = new Progress<string>(s => SetStatus(s));
        var draft = DeckProfiles.Copy(settings);
        if (draft.Profiles.Count == 0)
        {
            var found = await ReaderFor(draft.ProfileFolder).Discover(draft.ProfileFolder);
            DeckProfiles.Ensure(draft, found);
            foreach (var type in found.NoteTypes.Where(t => draft.Mappings.All(m => m.NoteTypeId != t.Id)))
                draft.Mappings.Add(new MappingChoice(type, null).ToMapping());
        }
        var next = await ReaderFor(draft.ProfileFolder).Import(draft, progress);
        // Validate and compute before replacing the last successful persistent result.
        var prepared = await Task.Run(() => PrepareProfiles(next));
        await Task.Run(() => store.SaveCache(next, draft));
        settings = draft;
        PopulateSettings(next.Metadata, settings);
        await DisplayImport(next, prepared);
        if (mediaLibrary.Count > 0)
        {
            try { await CompareMedia(); }
            catch (Exception e) { mediaCoverage = null; DrawMedia(); MediaStatus.Text = "Media comparison unavailable: " + e.Message; }
        }
        SetStatus($"Up to date. {prepared.Count:N0} enabled deck profiles · {next.Cards.Count:N0} unique included cards. Switch profiles without refreshing. Anki was read without modification.");
    }
    private async Task AnalyzeAndDisplay(AnalysisResult? computed = null)
    {
        if (data is null) return;
        overview = computed ?? await Task.Run(() => Analyzer.Analyze(data, kind: null));
        VocabularyCount.Text = overview.Vocabulary.Count.ToString("N0");
        ReviewCount.Text = overview.TotalAnswers.ToString("N0");
        int CountKind(int kind) => data.Reviews.Count(r => r.Kind == kind && r.Rating is >= 1 and <= 4);
        ReviewBreakdown.Text = $"Learning {CountKind(0):N0} · Review {CountKind(1):N0}\nRelearning {CountKind(2):N0} · Filtered {CountKind(3):N0}\nAll available history; administrative events excluded";
        InventoryBreakdown.Text = $"{data.Notes.Count(n => n.Category == "Vocabulary"):N0} vocabulary notes → {overview.Vocabulary.Count:N0} distinct entries\nGrammar and kanji notes excluded; all card states included";
        UpdateSourceIndicator();
        AgainRate.Text = overview.AgainDisplay;
        OverviewScope.Text = "Active profile: " + data.Scope;
        LastRefresh.Text = "Last successful refresh · " + data.RefreshedAt.ToLocalTime().ToString("MMM d, yyyy · HH:mm");
        ApplySearch();
        SkippedButton.IsEnabled = overview.Skipped.Count > 0;
        UpdateInsight();
        await UpdateCalendar();
        await UpdateReviews();
        await UpdateTimeAnalysis();
        await UpdateLearningPatterns();
        await UpdateCoverage();
        await UpdateMappings();
        var orphans = data.UnresolvedReviewsAvailable ? $"{data.UnresolvedReviews:N0} orphaned review events excluded collection-wide" : "orphaned review count unavailable via AnkiConnect";
        Footer.Text = $"{data.TimeZoneId} · current deck membership · {orphans} · {data.UnknownReviewKinds:N0} unknown events excluded";
    }
    private DateOnly Today(string zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
    private async Task UpdateCalendar()
    {
        if (data is null) return;
        var selectedStart = selectedStreaks.GetValueOrDefault(ActiveProfileId);
        var now = DateTimeOffset.UtcNow;
        streaks = await Task.Run(() => Streaks.Analyze(data, now));
        calendarDate = Streaks.StudyDay(now, TimeZoneInfo.FindSystemTimeZoneById(data.TimeZoneId));
        StreakPicker.ItemsSource = streaks;
        StreakPicker.IsEnabled = streaks.Count > 0;
        StreakPicker.SelectedItem = streaks.Find(s => s.Start == selectedStart) ?? streaks.FirstOrDefault();
        CalendarStatus.Text = streaks.Count == 0 ? "No rated answers in this selection yet. Review in Anki, then refresh here."
            : $"{streaks.Sum(s => s.Length):N0} reviewed study days · {streaks.Count:N0} streaks · longest {streaks.Max(s => s.Length):N0} days. "
                + (streaks[0].IsCurrent ? "Your latest streak is still active." : "No active streak. Your most recent streak is selected by default.");
        CalendarScope.Text = $"Study days run from 4 AM to 4 AM · {data.TimeZoneId}. Today ({calendarDate:MMM d, yyyy}) stays open until the next 4 AM cutoff. This fixed cutoff matches Anki's default; custom Anki cutoffs are not imported.\n"
            + $"All available learning, review, relearning and filtered rated answers · included deck rules: {data.Scope}. Administrative and unknown events excluded. Current deck membership; deleted/unresolved cards excluded. Refresh after reviewing to update these results.";
        DisplayStreak();
    }
    private void StreakChanged(object sender, SelectionChangedEventArgs e) { if (ready) DisplayStreak(); }
    private void DisplayStreak()
    {
        var streak = StreakPicker.SelectedItem as ReviewStreak;
        if (streak is not null) selectedStreaks[ActiveProfileId] = streak.Start;
        CalendarHeatmap.SetStreak(streak);
        CalendarScroll.ScrollToLeftEnd();
        StreakSummary.Text = streak is null ? "No streak selected" : $"{streak.Length:N0} consecutive days · {streak.Answers:N0} answers\n{streak.Start:MMM d, yyyy} – {streak.End:MMM d, yyyy}";
        if (streak?.IsCurrent == true && streak.End < calendarDate)
            StreakSummary.Text += " · Awaiting today's reviews";
    }
    private void UpdateInsight()
    {
        if (data is null || overview is null) return;
        insightDate = Today(data.TimeZoneId);
        var insight = store.SelectInsight(data.CollectionKey + ":" + ActiveProfileId, insightDate, overview.Insights);
        InsightTitle.Text = FeaturedTitle.Text = insight.Title;
        InsightDetail.Text = FeaturedDetail.Text = insight.Detail;
        InsightMethod.Text = "How this is measured: " + insight.Explanation + $"\nScope: {data.Scope} · all available history · all rated answers unless ordinary reviews are specified · {data.TimeZoneId}.";
        FeaturedDetail.Text = insight.Detail + "\n\n" + InsightMethod.Text;
    }
    private async Task UpdateReviews()
    {
        if (data is null) return;
        int? days = RangePicker.SelectedIndex switch { 1 => 30, 2 => 90, _ => null };
        int? kind = KindPicker.SelectedIndex switch { 0 => 1, 1 => 0, 2 => 2, 3 => 3, _ => null };
        reviews = days is null && kind is null && overview is not null ? overview : await Task.Run(() => Analyzer.Analyze(data, days, kind));
        BucketGrid.ItemsSource = reviews.Buckets;
        DailyGrid.ItemsSource = reviews.Daily;
        UpdateRanking();
        HistoryChart.SetRows(reviews.Daily, GranularityPicker.SelectedIndex == 1);
        ReviewSummary.Text = $"{data.Scope}\n{reviews.TotalAnswers:N0} selected answers · {reviews.Daily.Count:N0} active calendar days · {data.TimeZoneId}. Administrative and unknown events excluded. Missing durations excluded from medians.";
    }
    private void Navigate(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    private void ShowPage(string page)
    {
        MinWidth = page == "Media" ? 620 : 1000;
        PageTitle.Text = page;
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        VocabularyPage.Visibility = page == "Vocabulary" ? Visibility.Visible : Visibility.Collapsed;
        CoveragePage.Visibility = page == "Word Coverage" ? Visibility.Visible : Visibility.Collapsed;
        MediaPage.Visibility = page == "Media" ? Visibility.Visible : Visibility.Collapsed;
        MappingsPage.Visibility = page == "Mappings" ? Visibility.Visible : Visibility.Collapsed;
        ReviewsPage.Visibility = page == "Reviews" ? Visibility.Visible : Visibility.Collapsed;
        InsightsPage.Visibility = page == "Insights" ? Visibility.Visible : Visibility.Collapsed;
        CalendarPage.Visibility = page == "Calendar" ? Visibility.Visible : Visibility.Collapsed;
        TimeAnalysisPage.Visibility = page == "Time Analysis" ? Visibility.Visible : Visibility.Collapsed;
        LearningPage.Visibility = page == "Learning Patterns" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Guard(RefreshInternal);
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (ready) ApplySearch(); }
    private void ApplySearch()
    {
        if (overview is null) return;
        var search = VocabularySearch.Text.Trim();
        var rows = overview.Vocabulary.Where(v => search.Length == 0 || $"{v.Expression}\n{v.Reading}\n{v.Meaning}\n{v.Decks}".Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        VocabularyGrid.ItemsSource = rows;
        VocabularySummary.Text = $"{rows.Count:N0} of {overview.Vocabulary.Count:N0} distinct entries · {overview.Skipped.Count:N0} skipped expressions · same expression with a different reading stays separate";
    }
    private async void ReviewFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && !busy && data is not null) await Guard(UpdateReviews);
    }
    private void ChartGroupingChanged(object sender, SelectionChangedEventArgs e) { if (ready && reviews is not null) HistoryChart.SetRows(reviews.Daily, GranularityPicker.SelectedIndex == 1); }
    private void RankingChanged(object sender, SelectionChangedEventArgs e) { if (ready) UpdateRanking(); }
    private void UpdateRanking()
    {
        if (reviews is null) return;
        RankingGrid.ItemsSource = RankingPicker.SelectedIndex == 1 ? reviews.Rankings.OrderByDescending(r => r.Again).ThenByDescending(r => r.Reviews).ToList() : reviews.Rankings;
    }
    private void VocabularyDetails(object sender, MouseButtonEventArgs e)
    {
        if (VocabularyGrid.SelectedItem is VocabularyRow row)
            ShowDetails(row.Expression, $"Reading: {row.Reading}\nMeaning: {row.Meaning}\nDecks: {row.Decks}\n\nNote IDs: {row.NoteIds}\n\nCard IDs: {row.CardIds}");
    }
    private void ShowDetails(string title, string text)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) };
        new Window { Owner = this, Title = title, Width = 650, Height = 430, Content = box, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void ShowSkipped(object sender, RoutedEventArgs e)
    {
        if (overview is not null) ShowDetails("Skipped expressions", string.Join("\n", overview.Skipped.Select(s => $"Note {s.NoteId} · {s.NoteType} · {s.Reason}")));
    }
    private void BrowseProfile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose the Anki profile containing collection.anki2" };
        if (picker.ShowDialog(this) == true)
        {
            ProfilePath.Text = picker.FolderName;
            metadata = null; previewSignature = null; deckChoices.Clear(); mappingChoices.Clear();
            populatingProfiles = true; editorProfiles.Clear(); editingProfile = null; populatingProfiles = false;
            PreviewText.Text = "Inspect this profile to choose decks and map fields.";
        }
    }
    private async void InspectProfile(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(ProfilePath.Text)) throw new InvalidDataException("Choose a profile folder first.");
        SetStatus("Inspecting deck and field definitions from a private copy…");
        var profile = ProfilePath.Text;
        var found = await ReaderFor(profile).Discover(profile);
        PopulateSettings(found, settings?.ProfileFolder == profile ? settings : null);
        SetStatus("Profile inspected. Choose included decks and confirm the suggested field mappings, then preview.");
    });
    private void PopulateSettings(CollectionMetadata found, Settings? previous)
    {
        metadata = found; previewSignature = null;
        PopulateProfiles(found, previous);
        mappingChoices.Clear();
        foreach (var type in found.NoteTypes) mappingChoices.Add(new(type, previous?.Mappings.Find(m => m.NoteTypeId == type.Id)));
        if (previous is not null) TimeZonePicker.SelectedValue = previous.TimeZoneId;
    }
    private Settings DraftSettings()
    {
        ProfilesGrid.CommitEdit(); MappingGrid.CommitEdit(); SaveEditingRules();
        if (metadata is null) throw new InvalidDataException("Inspect the profile before setting up your selection.");
        var draft = new Settings { ProfileFolder = ProfilePath.Text, TimeZoneId = (string?)TimeZonePicker.SelectedValue ?? TimeZoneInfo.Local.Id,
            Profiles = DeckProfiles.Copy(editorProfiles.ToList()),
            ActiveProfileId = ActiveProfileId,
            Mappings = mappingChoices.Select(m => m.ToMapping()).ToList() };
        DeckProfiles.Validate(draft, metadata);
        if (!draft.Profiles.Any(p => p.Enabled && p.Id == draft.ActiveProfileId)) draft.ActiveProfileId = draft.Profiles.First(p => p.Enabled).Id;
        if (!draft.Mappings.Any(m => m.Category != "Ignored")) throw new InvalidDataException("Map at least one note type to Vocabulary, Grammar, or Kanji.");
        return draft;
    }
    private async void PreviewSelection(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var draft = DraftSettings(); SetStatus("Previewing your selection without saving…");
        var imported = await ReaderFor(draft.ProfileFolder).Import(draft);
        var prepared = await Task.Run(() => PrepareProfiles(imported));
        var result = prepared.First(p => p.Profile.Id == draft.ActiveProfileId).Analysis;
        previewSignature = draft.Signature;
        PreviewText.Text = string.Join("\n", prepared.Select(p => $"{p.Profile.Name}: {p.Analysis.TotalAnswers:N0} rated answers · {p.Data.Cards.Count:N0} cards · {p.Analysis.Vocabulary.Count:N0} vocabulary entries"))
            + $"\n\nActive profile sample ({prepared.First(p => p.Profile.Id == draft.ActiveProfileId).Profile.Name}):\n" + string.Join("\n", result.Vocabulary.Take(5).Select(v => $"{v.Expression}  {v.Reading}  {v.Meaning}"));
        SetStatus("Preview complete. Check extraction and counts, then save and refresh.");
    });
    private async void SaveSettings(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var draft = DraftSettings();
        if (previewSignature != draft.Signature) throw new InvalidDataException("Preview this selection before saving. Changed decks, fields, or time zone need a new preview.");
        // Keep current results and settings intact until the new selection is imported.
        SetStatus("Applying the previewed selection…");
        var next = await ReaderFor(draft.ProfileFolder).Import(draft);
        var prepared = await Task.Run(() => PrepareProfiles(next));
        await Task.Run(() => store.SaveCache(next, draft));
        settings = draft;
        await DisplayImport(next, prepared);
        SetStatus("Connected. Saved deck profiles refresh together with Anki open or closed. Switch the active profile without changing deck rules.");
        ShowPage("Overview");
    });
    private void OpenDataFolder(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("explorer.exe", store.Root) { UseShellExecute = true });
    private void ExportVocabulary(object sender, RoutedEventArgs e)
    {
        if (overview is null || data is null) { SetStatus("Refresh a collection before exporting."); return; }
        var picker = new SaveFileDialog { Filter = "CSV files|*.csv", FileName = "vocabulary.csv" };
        if (picker.ShowDialog(this) != true) return;
        var rows = new List<IEnumerable<object?>> { new object?[] { "Expression", "Reading", "Meaning", "Notes", "Cards", "Decks", "Note IDs", "Card IDs", "Refreshed at", "Scope", "Search" } };
        rows.AddRange(VocabularyGrid.Items.Cast<VocabularyRow>().Select(v => new object?[] { v.Expression, v.Reading, v.Meaning, v.Notes, v.Cards, v.Decks, v.NoteIds, v.CardIds, data.RefreshedAt.ToString("O"), data.Scope, VocabularySearch.Text }));
        CsvExport.Write(picker.FileName, rows); SetStatus("Vocabulary CSV exported with the current search and sort order.");
    }
    private void ExportReviews(object sender, RoutedEventArgs e)
    {
        if (reviews is null || data is null) { SetStatus("Refresh a collection before exporting."); return; }
        var picker = new SaveFileDialog { Filter = "CSV files|*.csv", FileName = "reviews.csv" };
        if (picker.ShowDialog(this) != true) return;
        var rows = new List<IEnumerable<object?>> { new object?[] { "Record type", "Date or period", "Card ID", "Expression", "Deck", "Answers", "Again", "Again percent", "Median seconds", "Lifetime lapses", "Notes" },
            new object?[] { "Metadata", "", "", "", "", "", "", "", "", "", $"{data.Scope}; {data.TimeZoneId}; {((ComboBoxItem)RangePicker.SelectedItem).Content}; {((ComboBoxItem)KindPicker.SelectedItem).Content}; refreshed {data.RefreshedAt:O}" } };
        rows.AddRange(reviews.Daily.Select(d => new object?[] { "Daily", d.Day, "", "", "", d.Answers, "", "", "", "", "Selected event scope" }));
        rows.AddRange(reviews.Buckets.Select(b => new object?[] { "Time bucket", b.Period, "", "", "", b.Answers, b.Again, b.AgainPercent, b.MedianSeconds, "", b.Confidence }));
        rows.AddRange(RankingGrid.Items.Cast<RankingRow>().Select(r => new object?[] { "Card ranking", "", r.CardId, r.Expression, r.Deck, r.Reviews, r.Again, "", "", r.LifetimeLapses, "Ordinary reviews only" }));
        CsvExport.Write(picker.FileName, rows); SetStatus("Review CSV exported. Rows identify daily counts, time buckets, and ordinary-review card rankings.");
    }
}

public sealed class DeckChoice
{
    public string Name { get; set; } = "";
    public int Cards { get; set; }
    public string Mode { get; set; } = "Inherit";
    public string[] Modes { get; } = ["Inherit", "Include", "Exclude"];
}
public sealed class MappingChoice
{
    public long Id { get; }
    public string Name { get; }
    public List<string> RequiredFields { get; }
    public List<string> OptionalFields { get; }
    public string[] Categories { get; } = ["Vocabulary", "Grammar", "Kanji", "Ignored"];
    public string Category { get; set; }
    public string Expression { get; set; }
    public string Reading { get; set; }
    public string Meaning { get; set; }
    public bool StripBracketReadings { get; set; }
    public MappingChoice(NoteType type, FieldMapping? previous)
    {
        Id = type.Id; Name = type.Name; RequiredFields = type.Fields; OptionalFields = ["(none)", .. type.Fields];
        string? Match(params string[] names) => names.Select(n => type.Fields.FirstOrDefault(f => f.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(f => f is not null);
        var suggested = Match("Expression", "Word", "Vocabulary", "単語", "表現");
        Category = Name.Contains("conjug", StringComparison.OrdinalIgnoreCase) || Name.Contains("grammar", StringComparison.OrdinalIgnoreCase) ? "Grammar" : Name.Contains("kanji", StringComparison.OrdinalIgnoreCase) ? "Kanji" : suggested is not null || Name.Equals("Japanese", StringComparison.OrdinalIgnoreCase) ? "Vocabulary" : "Ignored";
        Expression = suggested ?? type.Fields.FirstOrDefault() ?? "";
        Reading = Match("Reading", "Word Reading", "読み", "読み方", "かな") ?? "(none)";
        Meaning = Match("Meaning", "Word Meaning", "Definition", "意味", "Back") ?? "(none)";
        if (previous is null) return;
        Category = previous.Category;
        Expression = type.Fields.ElementAtOrDefault(previous.Expression) ?? Expression;
        Reading = type.Fields.ElementAtOrDefault(previous.Reading) ?? "(none)";
        Meaning = type.Fields.ElementAtOrDefault(previous.Meaning) ?? "(none)";
        StripBracketReadings = previous.StripBracketReadings;
    }
    public FieldMapping ToMapping() => new() { NoteTypeId = Id, Category = Category, Expression = RequiredFields.IndexOf(Expression), Reading = RequiredFields.IndexOf(Reading), Meaning = RequiredFields.IndexOf(Meaning), ExpressionName = Expression, ReadingName = Reading == "(none)" ? "" : Reading, MeaningName = Meaning == "(none)" ? "" : Meaning, StripBracketReadings = StripBracketReadings };
}
