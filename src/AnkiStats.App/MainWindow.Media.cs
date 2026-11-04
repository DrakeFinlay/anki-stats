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
        CurrentMediaCards.SizeChanged += (_, _) => ResizeMediaCards(CurrentMediaCards);
        CompletedMediaCards.SizeChanged += (_, _) => ResizeMediaCards(CompletedMediaCards);
        try { mediaLibrary = MediaStore.Load(); DrawMedia(); }
        catch (Exception e) { MediaStatus.Text = "Media cache could not be loaded: " + e.Message; }
    }
    private static void ResizeMediaCards(UniformGrid panel) => panel.Columns = Math.Clamp((int)(panel.ActualWidth / 210), 1, 6);
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
        if (prior is not null) corpus = corpus with { State = prior.State };
        token.ThrowIfCancellationRequested();
        await CacheMediaCover(corpus, token);
        await SaveMedia(corpus);
        MediaStatus.Text = $"Saved {corpus.Title} locally. {corpus.Words.Count(w => w.EntryId is null):N0} source forms need review. Compare all Anki vocabulary to calculate mining progress.";
        MediaTabs.SelectedIndex = corpus.State == "Completed" ? 1 : 0;
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
            token.ThrowIfCancellationRequested();
            await Task.Run(() => MediaStore.SaveText(corpus, text), token);
            await SaveMedia(corpus);
            MediaStatus.Text = $"Saved {title}. {corpus.Words.Count(w => w.EntryId is null):N0} unresolved token forms remain inspectable. Compare all Anki vocabulary to calculate mining progress.";
            MediaTabs.SelectedIndex = 0;
        });
    }
    private async Task SaveMedia(MediaCorpus corpus)
    {
        await Task.Run(() => MediaStore.Save(corpus));
        mediaLibrary = mediaLibrary.Where(c => c.Id != corpus.Id).Append(corpus).OrderBy(c => c.Title).ToList();
        if (selectedMedia?.Id == corpus.Id) selectedMedia = corpus;
        DrawMedia();
    }
    private async void CompareMediaClick(object sender, RoutedEventArgs e) => await MediaAction(CompareMedia);
    private async Task CompareMedia(CancellationToken token = default)
    {
        if (settings is null) throw new InvalidOperationException("Connect an Anki collection and map vocabulary fields in Settings first.");
        await RequireMediaReference();
        foreach (var corpus in mediaLibrary.Where(c => c.Source == "Jiten vocabulary").ToList())
            if (corpus.DictionaryVersion != vocabularyMatcher!.Reference.Metadata.DictionaryVersion || !corpus.Analyzer.EndsWith("form parser v1", StringComparison.Ordinal))
                await SaveMedia(await Task.Run(() => JitenClient.Revalidate(corpus, vocabularyMatcher.Reference), token));
        MediaStatus.Text = "Reading vocabulary across every Anki deck using your configured vocabulary field mappings…";
        var metadata = await ReaderFor(settings.ProfileFolder).Discover(settings.ProfileFolder);
        token.ThrowIfCancellationRequested();
        var allSettings = MediaAnalysis.CollectionSettings(settings, metadata);
        var collection = await ReaderFor(settings.ProfileFolder).Import(allSettings);
        token.ThrowIfCancellationRequested();
        var result = await Task.Run(() => vocabularyMatcher!.Analyze(collection,
            store.LoadCoverageDecisions<MatchDecision>(collection.CollectionKey, "matches"), []));
        token.ThrowIfCancellationRequested();
        mediaCoverage = result; mediaCollectionKey = collection.CollectionKey;
        mediaSettingsSignature = settings.Signature;
        MediaStatus.Text = $"Compared all decks · {collection.RefreshedAt.ToLocalTime():MMM d, yyyy HH:mm}. Dedicated mapped vocabulary only; suspended notes count. "
            + $"{result.Unresolved:N0} Anki notes need match review. Unmapped/ignored note types do not establish presence. Whole-title opportunities; reading position is not tracked.";
        DrawMedia();
    }
    private bool HasMediaComparison => mediaCoverage is not null && settings?.CollectionKey == mediaCollectionKey
        && settings.Signature == mediaSettingsSignature && vocabularyMatcher?.Reference == mediaCoverage.Reference;
    private void DrawMedia()
    {
        CurrentMediaCards.Children.Clear(); CompletedMediaCards.Children.Clear();
        foreach (var corpus in mediaLibrary)
        {
            var panel = new StackPanel { Margin = new Thickness(8), MaxWidth = 250 };
            var coverGrid = new Grid { Height = 200, ClipToBounds = true };
            coverGrid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(55, 65, 87)), CornerRadius = new CornerRadius(10) });
            var coverPath = CoverPath(corpus);
            if (File.Exists(coverPath))
            {
                try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 400; bitmap.UriSource = new Uri(coverPath); bitmap.EndInit(); bitmap.Freeze(); coverGrid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.UniformToFill }); }
                catch (Exception) { /* Artwork is optional; the title remains usable. */ }
            }
            else coverGrid.Children.Add(new TextBlock { Text = corpus.MediaType == 7 ? "VN" : corpus.MediaType == 1 ? "ANIME" : "BOOK", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            coverGrid.Children.Add(new Border { IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(100, 255, 255, 255), 0), new(Colors.Transparent, .45), new(Color.FromArgb(100, 0, 0, 0), 1) }, 90) });
            var cover = new Button { Content = coverGrid, Padding = new Thickness(0), Margin = new Thickness(5, 8, 5, 12), ToolTip = "Inspect vocabulary and source", RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1), Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Opacity = .5 } };
            cover.SizeChanged += (_, _) => coverGrid.Width = Math.Max(0, cover.ActualWidth);
            void Scale(double value) { var transform = (ScaleTransform)cover.RenderTransform; transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(140))); transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(140))); }
            cover.MouseEnter += (_, _) => Scale(1.04); cover.MouseLeave += (_, _) => Scale(1);
            cover.Click += (_, _) => { selectedMedia = corpus; ShowMediaWords(); MediaTabs.SelectedIndex = 3; };
            panel.Children.Add(cover);
            panel.Children.Add(new TextBlock { Text = corpus.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var summary = HasMediaComparison ? MediaAnalysis.Compare(corpus, mediaCoverage!) : null;
            panel.Children.Add(new TextBlock { Text = summary is null ? "Compare Anki to see mining counts" : $"{summary.Remaining:N0} eligible remaining\n{summary.Other:N0} other unique words\n{summary.Percent} · {summary.Present:N0} / {summary.Eligible:N0}\n{summary.NeedsReview:N0} possible Anki matches\n{summary.Unresolved:N0} unresolved source forms", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Foreground = (Brush)FindResource("Muted") });
            var state = new Button { Content = corpus.State == "Completed" ? "Move to currently reading" : "Mark completed", FontSize = 11, Padding = new Thickness(8) };
            state.Click += async (_, _) => await MediaAction(async _ => await SaveMedia(corpus with { State = corpus.State == "Completed" ? "Currently reading" : "Completed" }));
            panel.Children.Add(state);
            (corpus.State == "Completed" ? CompletedMediaCards : CurrentMediaCards).Children.Add(panel);
        }
        if (CurrentMediaCards.Children.Count == 0) CurrentMediaCards.Children.Add(new TextBlock { Text = "No current titles. Open Add to search online or analyze local text.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) });
        if (CompletedMediaCards.Children.Count == 0) CompletedMediaCards.Children.Add(new TextBlock { Text = "Completed titles will appear here.", Margin = new Thickness(20) });
        ShowMediaWords();
    }
    private void MediaWordFilterChanged(object sender, SelectionChangedEventArgs e) { if (ready) ShowMediaWords(); }
    private void ShowMediaWords()
    {
        if (selectedMedia is not { } corpus) return;
        MediaDetails.Text = $"{corpus.Title} · {corpus.Edition}\n{corpus.Source} · saved {corpus.RetrievedAt.ToLocalTime():g}\n{corpus.Analyzer}\nJMdict {corpus.DictionaryVersion} · source hash {corpus.ContentHash[..Math.Min(12, corpus.ContentHash.Length)]}\n{corpus.Attribution}\n{corpus.SourceUrl}";
        if (MediaWordFilter.SelectedIndex == 3)
        {
            var ids = vocabularyMatcher?.Entries;
            MediaWordsGrid.ItemsSource = corpus.Words.Where(w => w.EntryId is null || ids is not null && !ids.ContainsKey(w.EntryId))
                .Select(w => new MediaWordRow(w.EntryId ?? "", w.Form, w.Reading, "", "", w.Occurrences, w.EntryId is null ? w.Status : "Reference ID no longer installed", false)).ToList();
            return;
        }
        if (!HasMediaComparison) { MediaWordsGrid.ItemsSource = null; MediaDetails.Text += "\nCompare all Anki vocabulary to see resolved-word mining status."; return; }
        var summary = MediaAnalysis.Compare(corpus, mediaCoverage!);
        MediaWordsGrid.ItemsSource = summary.Words.Where(w => MediaWordFilter.SelectedIndex switch { 1 => w.Eligible && w.Status == "Remaining", 2 => !w.Eligible, _ => true }).ToList();
    }
    private string CoverPath(MediaCorpus corpus) => Path.Combine(store.Root, "media", "covers", MediaAnalysis.Hash([new(corpus.Id, "", "", 0, "")]) + ".jpg");
    private async Task CacheMediaCover(MediaCorpus corpus, CancellationToken token)
    {
        if (!Uri.TryCreate(corpus.CoverUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "cdn.jiten.moe") return;
        try
        {
            using var response = await mediaHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 5_000_000) return;
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream(); var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0) { bytes.Write(buffer, 0, read); if (bytes.Length > 5_000_000) return; }
            var path = CoverPath(corpus); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes.ToArray(), token);
        }
        catch (Exception e) when (e is HttpRequestException or IOException) { /* Keep a usable title without artwork. */ }
    }
}
