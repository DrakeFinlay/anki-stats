using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AnkiStats.Core;
using Microsoft.Win32;

namespace AnkiStats.App;

public partial class MainWindow
{
    private readonly HttpClient mediaHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private List<MediaCorpus> mediaLibrary = [];
    private CoverageAnalysis? mediaCoverage;
    private ImportedCollection? mediaComparisonCollection;
    private string mediaCollectionKey = "";
    private string mediaSettingsSignature = "";
    private MediaCorpus? selectedMedia;
    private CancellationTokenSource? mediaCancellation;
    private string mediaSearchQuery = "";
    private int mediaSearchType, mediaSearchOffset;
    private MediaLibraryStore MediaStore => new(store.Root);
    private int SelectedMediaType => int.Parse((string)((ComboBoxItem)MediaKind.SelectedItem).Tag, System.Globalization.CultureInfo.InvariantCulture);

    private void InitializeMedia()
    {
        CurrentMediaCards.SizeChanged += (_, _) => CurrentMediaCards.Columns = CurrentMediaCards.Children.Count > 1 && CurrentMediaCards.ActualWidth >= 1500 ? 2 : 1;
        PlannedMediaCards.SizeChanged += (_, _) => ResizeMediaCards(PlannedMediaCards);
        CompletedMediaCards.SizeChanged += (_, _) => ResizeMediaCards(CompletedMediaCards);
        try { mediaLibrary = MediaStore.Load(); DrawMedia(); }
        catch (Exception e) { MediaStatus.Text = "Media cache could not be loaded: " + e.Message; }
    }
    private static void ResizeMediaCards(UniformGrid panel) => panel.Columns = Math.Clamp((int)(panel.ActualWidth / 480), 1, 4);
    private async Task MediaAction(Func<CancellationToken, Task> action)
    {
        if (busy || mediaCancellation is not null) return;
        await Guard(async () =>
        {
            using var cancellation = new CancellationTokenSource(); mediaCancellation = cancellation;
            CancelMediaButton.IsEnabled = true;
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { MediaStatus.Text = "Media operation cancelled. Previously saved titles were retained."; }
            catch (Exception e) { MediaStatus.Text = e.Message; SetStatus("Media: " + e.Message, true); }
            finally { mediaCancellation = null; CancelMediaButton.IsEnabled = false; }
        });
    }
    private void CancelMediaClick(object sender, RoutedEventArgs e) => mediaCancellation?.Cancel();
    private async void SearchMediaClick(object sender, RoutedEventArgs e) => await SearchMedia(false);
    private async void NextMediaClick(object sender, RoutedEventArgs e) => await SearchMedia(true);
    private async Task SearchMedia(bool next) => await MediaAction(async token =>
    {
        var query = MediaQuery.Text.Trim(); var type = SelectedMediaType;
        var offset = next && query == mediaSearchQuery && type == mediaSearchType ? mediaSearchOffset + 50 : 0;
        MediaStatus.Text = "Searching Jiten…";
        var titles = await new JitenClient(mediaHttp).Search(query, type, offset, token);
        mediaSearchQuery = query; mediaSearchType = type; mediaSearchOffset = offset;
        MediaResults.ItemsSource = titles; MediaResults.SelectedIndex = titles.Count > 0 ? 0 : -1;
        MediaStatus.Text = titles.Count == 0 ? "No matching title on this page. Try another title or import local text."
            : $"{titles.Count} results starting at {offset + 1}. Select the matching work; its source edition may be unverified.";
    });
    private async Task<CoverageReference> RequireMediaReference()
    {
        if (vocabularyMatcher is not null) return vocabularyMatcher.Reference;
        var reference = await Task.Run(() => ReferenceStore.Load()) ?? throw new InvalidOperationException("Install dictionary data in Word Coverage before adding media.");
        vocabularyMatcher = await Task.Run(() => new VocabularyMatcher(reference));
        return reference;
    }
    private async void DownloadMediaClick(object sender, RoutedEventArgs e) => await MediaAction(async token =>
    {
        if (MediaResults.SelectedItem is not MediaTitle title) throw new InvalidOperationException("Search and select a title first.");
        var reference = await RequireMediaReference();
        var corpus = await new JitenClient(mediaHttp).Download(title, MediaEdition.Text, reference,
            new Progress<string>(message => MediaStatus.Text = message), token);
        var prior = mediaLibrary.Find(c => c.Id == corpus.Id);
        corpus = MediaShelves.Move(corpus, prior?.State ?? NewMediaShelfName, mediaLibrary);
        token.ThrowIfCancellationRequested();
        corpus = await DownloadBestMediaCover(corpus, token);
        await SaveMedia(corpus);
        MediaStatus.Text = $"Saved {corpus.Title} locally. {corpus.Words.Count(w => w.EntryId is null):N0} source forms need review. Compare all Anki vocabulary to calculate mining progress.";
        SelectMediaShelf(corpus.State);
    });
    private async void ImportLocalMediaClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "Japanese text (*.txt;*.srt;*.ass;*.vtt;*.epub)|*.txt;*.srt;*.ass;*.vtt;*.epub" };
        if (dialog.ShowDialog(this) != true) return;
        await MediaAction(async token =>
        {
            var title = LocalMediaTitle.Text.Trim(); var type = SelectedMediaType; var edition = MediaEdition.Text;
            if (title.Length == 0) throw new InvalidOperationException("Enter a title for the local text.");
            var reference = await RequireMediaReference();
            MediaStatus.Text = "Reading local text…";
            var text = await Task.Run(() => LocalMediaText.Read(dialog.FileName), token);
            MediaStatus.Text = "Analyzing locally with Sudachi. No text is uploaded…";
            var analyzed = await LocalMediaText.Analyze(text, MediaPython.Text.Trim(), token);
            var corpus = await Task.Run(() => LocalMediaText.Resolve(title, type, edition, text, analyzed, reference), token);
            corpus = MediaShelves.Move(corpus, NewMediaShelfName, mediaLibrary);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => MediaStore.SaveText(corpus, text), token);
            await SaveMedia(corpus);
            MediaStatus.Text = $"Saved {title}. {corpus.Words.Count(w => w.EntryId is null):N0} unresolved token forms remain inspectable. Compare all Anki vocabulary to calculate mining progress.";
            SelectMediaShelf(corpus.State);
        });
    }
    private async Task SaveMedia(MediaCorpus corpus)
    {
        await Task.Run(() => MediaStore.Save(corpus));
        mediaLibrary = mediaLibrary.Where(c => c.Id != corpus.Id).Append(corpus).OrderBy(c => c.Title).ToList();
        if (selectedMedia?.Id == corpus.Id) selectedMedia = corpus;
        await UpdateMappings();
        DrawMedia();
    }
    private async void CompareMediaClick(object sender, RoutedEventArgs e) => await MediaAction(token => CompareMedia(token));
    private async Task CompareMedia(CancellationToken token, ImportedCollection? snapshot = null)
    {
        if (settings is null) throw new InvalidOperationException("Connect an Anki collection and map vocabulary fields in Settings first.");
        await RequireMediaReference();
        foreach (var corpus in mediaLibrary.Where(c => c.Source == "Jiten vocabulary").ToList())
            if (corpus.DictionaryVersion != vocabularyMatcher!.Reference.Metadata.DictionaryVersion || !corpus.Analyzer.EndsWith(JitenClient.FormParserVersion, StringComparison.Ordinal))
                await SaveMedia(await Task.Run(() => JitenClient.Revalidate(corpus, vocabularyMatcher.Reference), token));
        token.ThrowIfCancellationRequested();
        MediaStatus.Text = snapshot is null ? "Reading vocabulary across every Anki deck…" : "Matching vocabulary from the refreshed collection…";
        var collection = snapshot ?? await ReaderFor(settings.ProfileFolder).ImportMedia(settings);
        token.ThrowIfCancellationRequested();
        var result = await Task.Run(() => vocabularyMatcher!.Analyze(collection,
            store.LoadCoverageDecisions<MatchDecision>(collection.CollectionKey, "matches"), [], aliases: LoadMappingAliases(collection.CollectionKey)));
        token.ThrowIfCancellationRequested();
        mediaCoverage = result; mediaCollectionKey = collection.CollectionKey;
        mediaComparisonCollection = collection;
        mediaSettingsSignature = settings.Signature;
        mappingCollection = collection;
        await UpdateMappings();
        MediaStatus.Text = $"Compared all decks · {collection.RefreshedAt.ToLocalTime():MMM d, yyyy HH:mm}. Mapped vocabulary and confirmed grammar links; suspended notes count. "
            + $"{result.Unresolved:N0} Anki notes need match review. Unmapped/ignored note types do not establish presence. Whole-title opportunities; reading position is not tracked.";
        DrawMedia();
    }
    private bool HasMediaComparison => mediaCoverage is not null && settings?.CollectionKey == mediaCollectionKey
        && settings.Signature == mediaSettingsSignature && vocabularyMatcher?.Reference == mediaCoverage.Reference;
    private void DrawMedia()
    {
        CurrentMediaCards.Children.Clear(); PlannedMediaCards.Children.Clear(); CompletedMediaCards.Children.Clear();
        foreach (var corpus in mediaLibrary)
        {
            var summary = HasMediaComparison ? MediaAnalysis.Compare(MappedCorpus(corpus), mediaCoverage!) : null;
            if (corpus.State == MediaShelves.Reading)
            {
                CurrentMediaCards.Children.Add(CreateReadingCard(corpus, summary));
                continue;
            }
            var tile = new Grid { Margin = new Thickness(16), MinHeight = 300 };
            tile.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var artwork = CreateMediaCover(corpus, false);
            artwork.VerticalAlignment = VerticalAlignment.Top;
            var panel = new StackPanel { Margin = new Thickness(16, 8, 8, 8), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = corpus.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = summary is null ? "Compare Anki to see mining counts" : $"{summary.Remaining:N0} eligible remaining\n{summary.Other:N0} other unique words\n{summary.Percent} · {summary.Present:N0} / {summary.Eligible:N0}\n{summary.NeedsReview:N0} possible Anki matches\n{summary.Unresolved:N0} unresolved source forms", FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), Foreground = (Brush)FindResource("Muted") });
            AddMediaShelfPicker(panel, corpus, false);
            Grid.SetColumn(panel, 1); tile.Children.Add(artwork); tile.Children.Add(panel);
            tile.SizeChanged += (_, _) =>
            {
                var ratio = (double)artwork.Tag;
                var width = Math.Clamp(tile.ActualWidth * .38, 90, 220);
                artwork.Height = Math.Min(340, width / ratio); artwork.Width = artwork.Height * ratio;
            };
            (corpus.State == MediaShelves.Completed ? CompletedMediaCards : PlannedMediaCards).Children.Add(tile);
        }
        CurrentMediaCards.Columns = CurrentMediaCards.Children.Count > 1 && CurrentMediaCards.ActualWidth >= 1500 ? 2 : 1;
        if (CurrentMediaCards.Children.Count == 0) CurrentMediaCards.Children.Add(new TextBlock { Text = "Choose a title from Planned to start reading, or add a new title.", FontSize = 24, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center });
        if (PlannedMediaCards.Children.Count == 0) PlannedMediaCards.Children.Add(new TextBlock { Text = "Build your reading shortlist here. New downloads go to Planned by default.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) });
        if (CompletedMediaCards.Children.Count == 0) CompletedMediaCards.Children.Add(new TextBlock { Text = "Completed titles will appear here.", Margin = new Thickness(20) });
        ShowMediaWords();
        RefreshMediaMining();
    }
    private string NewMediaShelfName => (string)((ComboBoxItem)NewMediaShelf.SelectedItem).Content;
    private void SelectMediaShelf(string shelf) => MediaTabs.SelectedItem = shelf switch
    {
        MediaShelves.Planned => PlannedMediaTab,
        MediaShelves.Completed => CompletedMediaTab,
        _ => ReadingMediaTab
    };
    private void AddMediaShelfPicker(Panel panel, MediaCorpus corpus, bool featured)
    {
        panel.Children.Add(new TextBlock { Text = "Shelf", FontSize = featured ? 18 : 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 8, 0, 4) });
        var picker = new ComboBox { ItemsSource = MediaShelves.All, SelectedItem = corpus.State, FontSize = featured ? 20 : 14, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(picker, "Shelf for " + corpus.Title);
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedItem is not string shelf || shelf == corpus.State) return;
            await MediaAction(async _ => await SaveMedia(MediaShelves.Move(corpus, shelf, mediaLibrary)));
            // Restore the displayed state after a rejected move or an operation already in progress.
            picker.SelectedItem = mediaLibrary.Find(c => c.Id == corpus.Id)?.State ?? corpus.State;
        };
        panel.Children.Add(picker);
    }
    private FrameworkElement CreateReadingCard(MediaCorpus corpus, MediaSummary? summary)
    {
        var layout = new Grid { Margin = new Thickness(20), MinHeight = 470 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var cover = CreateMediaCover(corpus, true);
        var detail = new StackPanel { Margin = new Thickness(32, 16, 16, 16), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = corpus.Title, FontSize = 38, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        detail.Children.Add(title);
        detail.Children.Add(new TextBlock { Text = corpus.Edition, FontSize = 18, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var metrics = new UniformGrid { Columns = 2 };
        var numbers = new List<TextBlock>();
        void Metric(string label, string value)
        {
            var content = new StackPanel { Margin = new Thickness(0, 0, 20, 20) };
            var number = new TextBlock { Text = value, FontSize = 40, FontWeight = FontWeights.SemiBold };
            numbers.Add(number); content.Children.Add(number);
            content.Children.Add(new TextBlock { Text = label, FontSize = 18, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            metrics.Children.Add(content);
        }
        Metric("Common / JLPT words remaining", summary?.Remaining.ToString("N0") ?? "—");
        Metric("Other unique words", summary?.Other.ToString("N0") ?? "—");
        detail.Children.Add(metrics);
        var percent = new TextBlock { Text = summary?.Percent ?? "Compare Anki to see mining progress", FontSize = 28, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        detail.Children.Add(percent);
        detail.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, summary?.Eligible ?? 0), Value = summary?.Present ?? 0, Height = 12, Foreground = new SolidColorBrush(Color.FromRgb(109, 169, 225)), Margin = new Thickness(0, 0, 0, 12) });
        detail.Children.Add(new TextBlock { Text = summary is null ? "Whole-title vocabulary opportunities" : $"{summary.Present:N0} of {summary.Eligible:N0} eligible words in Anki\n{summary.NeedsReview:N0} possible Anki matches · {summary.Unresolved:N0} unresolved source forms", FontSize = 18, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") });
        var inspect = new Button { Content = "Explore vocabulary →", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 8) };
        inspect.Click += (_, _) => OpenMediaWords(corpus);
        detail.Children.Add(inspect); AddMediaShelfPicker(detail, corpus, true);
        layout.Children.Add(cover); layout.Children.Add(detail);
        layout.SizeChanged += (_, _) =>
        {
            var narrow = layout.ActualWidth < 650;
            Grid.SetColumn(detail, narrow ? 0 : 1); Grid.SetRow(detail, narrow ? 1 : 0);
            Grid.SetColumnSpan(cover, narrow ? 2 : 1); Grid.SetColumnSpan(detail, narrow ? 2 : 1);
            layout.RowDefinitions[0].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            var ratio = cover.Tag is double aspect ? aspect : 9d / 16;
            var availableHeight = narrow ? 320 : Math.Clamp(CurrentMediaViewport.ActualHeight - 80, 400, 900);
            var availableWidth = narrow ? Math.Max(1, layout.ActualWidth - 24) : Math.Max(1, layout.ActualWidth * .4 - 20);
            cover.Height = Math.Min(availableHeight, availableWidth / ratio);
            cover.Width = cover.Height * ratio;
            detail.Margin = narrow ? new Thickness(12, 24, 12, 12) : new Thickness(32, 16, 16, 16);
            title.FontSize = Math.Clamp(layout.ActualWidth / 25, 30, 64);
            foreach (var number in numbers) number.FontSize = Math.Clamp(layout.ActualWidth / 28, 36, 72);
            percent.FontSize = Math.Clamp(layout.ActualWidth / 40, 26, 42);
        };
        return new Border { Child = layout, Background = new SolidColorBrush(Color.FromRgb(37, 40, 48)), CornerRadius = new CornerRadius(12), Margin = new Thickness(8) };
    }
    private void OpenMediaWords(MediaCorpus corpus)
    {
        selectedMedia = corpus; MediaWordImportStatus.Text = ""; ShowMediaWords(); MediaTabs.SelectedItem = MediaWordsTab;
    }
    private Button CreateMediaCover(MediaCorpus corpus, bool featured)
    {
            var aspect = 9d / 16;
            var coverGrid = new Grid { Height = featured ? 400 : 200, ClipToBounds = true };
            coverGrid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(55, 65, 87)), CornerRadius = new CornerRadius(10) });
            var coverPath = CoverPath(corpus);
            if (File.Exists(coverPath))
            {
                try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(coverPath); bitmap.EndInit(); bitmap.Freeze(); aspect = (double)bitmap.PixelWidth / bitmap.PixelHeight; coverGrid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform }); }
                catch (Exception) { /* Artwork is optional; the title remains usable. */ }
            }
            else coverGrid.Children.Add(new TextBlock { Text = corpus.MediaType == 7 ? "VN" : corpus.MediaType == 1 ? "ANIME" : "BOOK", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            coverGrid.Children.Add(new Border { IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) });
            var cover = new Button { Content = coverGrid, Padding = new Thickness(0), Margin = new Thickness(5, 8, 5, 12), ToolTip = "Inspect vocabulary and source", RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1), Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Opacity = .5 } };
            cover.Tag = aspect;
            RenderOptions.SetBitmapScalingMode(coverGrid, BitmapScalingMode.HighQuality);
            if (!featured) { cover.Width = 180; cover.Height = 180 / aspect; }
            cover.SizeChanged += (_, _) =>
            {
                coverGrid.Width = Math.Max(0, cover.ActualWidth);
                coverGrid.Height = Math.Max(0, cover.ActualHeight);
            };
            void Scale(double value) { var transform = (ScaleTransform)cover.RenderTransform; transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(140))); transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(140))); }
            cover.MouseEnter += (_, _) => Scale(1.04); cover.MouseLeave += (_, _) => Scale(1);
            cover.Click += (_, _) => OpenMediaWords(corpus);
            return cover;
    }
    private void MediaWordFilterChanged(object sender, SelectionChangedEventArgs e) { if (ready) ShowMediaWords(); }
    private void ShowMediaWords()
    {
        if (selectedMedia is not { } corpus) return;
        MediaDetails.Text = $"{corpus.Title} · {corpus.Edition}\n{corpus.Source} · saved {corpus.RetrievedAt.ToLocalTime():g}\n{corpus.Analyzer}\nJMdict {corpus.DictionaryVersion} · source hash {corpus.ContentHash[..Math.Min(12, corpus.ContentHash.Length)]}\n{corpus.Attribution}\n{corpus.SourceUrl}";
        if (MediaWordFilter.SelectedIndex == 3)
        {
            var ids = vocabularyMatcher?.Entries;
            MediaWordsGrid.ItemsSource = MappedCorpus(corpus).Words.Where(w => w.Status != MappingReview.ExcludedSource && (w.EntryId is null || ids is not null && !ids.ContainsKey(w.EntryId)))
                .Select(w => new MediaWordRow(w.EntryId ?? "", w.Form, w.Reading, "", "", w.Occurrences, w.EntryId is null ? w.Status : "Reference ID no longer installed", false)).ToList();
            return;
        }
        if (!HasMediaComparison) { MediaWordsGrid.ItemsSource = null; MediaDetails.Text += "\nCompare all Anki vocabulary to see resolved-word mining status."; return; }
        var summary = MediaAnalysis.Compare(MappedCorpus(corpus), mediaCoverage!);
        MediaWordsGrid.ItemsSource = summary.Words.Where(w => MediaWordFilter.SelectedIndex switch { 1 => w.Eligible && w.Status == "Remaining", 2 => !w.Eligible, _ => true }).ToList();
    }
    private string CoverPath(MediaCorpus corpus) => MediaStore.ArtworkPath(corpus.Id);
    private async void RefreshMediaCoversClick(object sender, RoutedEventArgs e) => await MediaAction(async token =>
    {
        var refreshed = 0;
        foreach (var corpus in mediaLibrary.Where(c => c.Source == "Jiten vocabulary").ToList())
        {
            MediaStatus.Text = "Refreshing original artwork: " + corpus.Title;
            var updated = await DownloadBestMediaCover(corpus, token);
            await SaveMedia(updated);
            if (updated.CoverUrl is not null && File.Exists(CoverPath(updated))) refreshed++;
            await Task.Delay(600, token);
        }
        MediaStatus.Text = $"Artwork checked for {refreshed:N0} cached titles. VNDB originals are used where available; source resolution varies. Vocabulary and Anki were unchanged.";
    });
    private async Task<MediaCorpus> DownloadBestMediaCover(MediaCorpus corpus, CancellationToken token)
    {
        if (corpus.MediaType == 7 && corpus.Id.StartsWith("jiten:", StringComparison.Ordinal)
            && int.TryParse(corpus.Id[6..], out var deckId))
        {
            try
            {
                var original = await new JitenClient(mediaHttp).OriginalVnCover(deckId, token);
                if (original is not null && await CacheMediaCover(corpus with { CoverUrl = original }, token))
                    return corpus with { CoverUrl = original };
            }
            catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or IOException) { /* Keep the existing provider cover as fallback. */ }
        }
        await CacheMediaCover(corpus, token);
        return corpus;
    }
    private async Task<bool> CacheMediaCover(MediaCorpus corpus, CancellationToken token)
    {
        if (!Uri.TryCreate(corpus.CoverUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host is not ("cdn.jiten.moe" or "t.vndb.org" or "s.vndb.org")) return false;
        try
        {
            using var response = await mediaHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 5_000_000) return false;
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream(); var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0) { bytes.Write(buffer, 0, read); if (bytes.Length > 5_000_000) return false; }
            bytes.Position = 0;
            var frame = BitmapDecoder.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 30_000_000) return false;
            var path = CoverPath(corpus); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, bytes.ToArray(), token); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or NotSupportedException or ArgumentException or FormatException) { return false; }
    }
}
