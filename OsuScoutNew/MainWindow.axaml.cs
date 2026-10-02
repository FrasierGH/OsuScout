using OsuScout;
using OsuScoutNew.Controls;
using OsuScoutNew.Core;
using OsuScoutNew.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Velopack;
using Velopack.Sources;

namespace OsuScoutNew
{
    public partial class MainWindow : Window
    {
        private OsuClassifier _classifier;
        private IBeatmapSource _source;

        // Which client's library is shown, and the folders the user picked for each.
        private OsuClient _client;
        private string _songsFolder;
        private string _lazerDataFolder;
        private List<string> _gameProcessNames;
        // Set while OpenLibrary makes the client picker match _client, so that isn't a switch.
        private bool _showingClient;

        private OsuLibraryService _libraryService;
        private OsuLiveTrackerService _liveTrackerService;

        // The list's sort, by column (SortMemberPath). Every refresh replaces the list's items,
        // so the sort lives here and is reapplied each time.
        private List<(string Path, ListSortDirection Direction)> _sort = new();
        // Shift held on the last click in the list, for multi-column sorting.
        private bool _shiftOnLastClick;
        private bool _restoringSettings;
        private AppSettings _settings;

        // Every range filter: its slider, value label and clear button, and how a value reads.
        private (RangeSlider Slider, TextBlock Label, Button Reset, Func<double, string> Format, string Unit)[] _ranges;

        // The map list's columns as the XAML defines them, for "Reset columns".
        private Dictionary<DataGridColumn, (DataGridLength Width, bool Visible)> _defaultColumns;

        // The window's size and place while not maximised, kept so it can be saved even while
        // maximised (Avalonia has no RestoreBounds).
        private PixelPoint _normalPosition;
        private Size _normalSize;

        // The model that produced each library's stored tags (AppSettings.TaggedWithModel and
        // LazerTaggedWithModel).
        private string _taggedWithModel;
        private string _lazerTaggedWithModel;

        private string TaggedWithModel
        {
            get => _client == OsuClient.Lazer ? _lazerTaggedWithModel : _taggedWithModel;
            set
            {
                if (_client == OsuClient.Lazer) _lazerTaggedWithModel = value;
                else _taggedWithModel = value;
            }
        }

        // Above this are gimmick maps (Aspire and the like) that would otherwise fill the top of a
        // stars-descending list. They only show up when the user searches for one by name.
        private const double GimmickStarThreshold = 15;

        public MainWindow()
        {
            InitializeComponent();
            _ranges = new (RangeSlider, TextBlock, Button, Func<double, string>, string)[]
            {
                (StarSlider, StarValueText, StarResetButton, v => $"{v:0.#}★", ""),
                (BpmSlider, BpmValueText, BpmResetButton, v => $"{v:0}", ""),
                (LengthSlider, LengthValueText, LengthResetButton, v => $"{v:0}", " min"),
                (CsSlider, CsValueText, CsResetButton, v => $"{v:0.0}", ""),
                (ArSlider, ArValueText, ArResetButton, v => $"{v:0.0}", ""),
                (OdSlider, OdValueText, OdResetButton, v => $"{v:0.0}", ""),
                (HpSlider, HpValueText, HpResetButton, v => $"{v:0.0}", ""),
            };
            foreach (var range in _ranges)
            {
                range.Slider.ValueChanged += Slider_ValueChanged;
                range.Reset.Tag = range.Slider;
            }
            _defaultColumns = BeatmapGrid.Columns.ToDictionary(c => c, c => (c.Width, c.IsVisible));
            BeatmapGrid.AddHandler(PointerPressedEvent, (_, e) => _shiftOnLastClick = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel);
            BeatmapGrid.AddHandler(ContextRequestedEvent, BeatmapGrid_ContextRequested, RoutingStrategies.Bubble);

            _settings = SettingsService.Load();
            _taggedWithModel = _settings.TaggedWithModel;
            _lazerTaggedWithModel = _settings.LazerTaggedWithModel;
            _songsFolder = _settings.SongsFolder;
            _lazerDataFolder = _settings.LazerDataFolder;
            _gameProcessNames = _settings.GameProcessNames;
            RestorePlacement(_settings.Window);

            _classifier = new OsuClassifier();
            _classifier.Initialize();
            SetUpTagSuggestions(_classifier.Config.tags);

            _libraryService = new OsuLibraryService(_classifier);
            _liveTrackerService = new OsuLiveTrackerService(_libraryService);
            _liveTrackerService.MapProcessed += () => Dispatcher.UIThread.Post(UpdateGrid);

            PositionChanged += (_, _) => RememberNormalBounds();
            Resized += (_, _) => RememberNormalBounds();
            Opened += async (_, _) => await StartAsync();
        }

        // What needs the window on screen: questions asked in dialogs, then the library.
        private async Task StartAsync()
        {
            SystemInteropService.UseDarkTitleBar(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
            RememberNormalBounds();
            await ReportPreviousScanCrash();
            _client = _settings.Client ?? await PickClientOnFirstRun();

            RestoreSettings(_settings);
            OpenLibrary();
            UpdateFilterLabels();
            UpdateGrid();
            _ = UpdateAppAsync();
        }

        // A scan that took the whole app down leaves a log with no ending (see ScanLog). Say so
        // once, skip the maps it was reading, and point at the log so the crash can be reported.
        private async Task ReportPreviousScanCrash()
        {
            var maps = ScanLog.RecoverFromCrash();
            if (maps.Count == 0) return;

            string list = string.Join("\n", maps.Take(12).Select(m => "• " + m));
            if (maps.Count > 12) list += $"\n…and {maps.Count - 12} more";
            var answer = await MessageDialog.ShowAsync(this,
                "Scoutsu closed unexpectedly the last time it scanned your maps. It was reading these when it stopped:\n\n" +
                list + "\n\n" +
                "They'll be skipped from now on so the scan can finish.\n\n" +
                "If you can, please report this at github.com/FrasierGH/OsuScout/issues and attach the file " +
                "scan-previous.log. Open the folder with that file now?",
                "Scoutsu closed unexpectedly", DialogButtons.YesNo, DialogIcon.Warning);
            if (answer == DialogResult.Yes)
                SystemInteropService.ShowInFileManager(ScanLog.PreviousLogPath);
        }

        // Only asked once: afterwards AppSettings.Client remembers the choice.
        private async Task<OsuClient> PickClientOnFirstRun()
        {
            bool stable = OsuLocationService.FindOsuSongsFolder() != null;
            bool lazer = LazerLocationService.FindDataFolder() != null;

            var pick = GameClients.PickOnFirstRun(stable, lazer);
            if (pick != null) return pick.Value;

            var answer = await MessageDialog.ShowAsync(this,
                "Scoutsu found both osu!stable and osu!lazer on this PC.\n\nShow your osu!lazer library? Choose No for osu!stable.\n\nYou can switch at any time with the Library picker at the top.",
                "Which osu!?", DialogButtons.YesNo, DialogIcon.Question);
            return answer == DialogResult.Yes ? OsuClient.Lazer : OsuClient.Stable;
        }

        private IBeatmapSource CreateSource(OsuClient client)
        {
            if (client == OsuClient.Lazer)
                return new LazerFilesSource(LazerLocationService.FindDataFolder(_lazerDataFolder));

            // A folder picked with Folder… wins; auto-detection is only the fallback.
            var stable = new StableSongsSource(System.IO.Directory.Exists(_songsFolder)
                ? _songsFolder
                : OsuLocationService.FindOsuSongsFolder());
            _songsFolder = stable.Root;
            return stable;
        }

        // Shows _client's library: its own folder, watcher, database and scan. Each client
        // has its own database file, so switching never mixes the two libraries.
        private void OpenLibrary()
        {
            _liveTrackerService.StopTracking();
            _source = CreateSource(_client);
            _liveTrackerService.StartTracking(_source);

            // Scan on every launch, not just the first: the folder watcher only sees maps added
            // while the app is open. ScanLibraryAsync skips files already in the DB, so this only
            // processes maps that are new since last time.
            // The exception is a library that came from a different folder. Older versions didn't
            // save a folder picked with Folder… (once ⚙ DIR), so auto-detection can land somewhere else, and
            // scanning that would mix two libraries (or report osu! missing on every launch).
            // The library always comes from one folder (changing folder wipes it), so one map is
            // enough to tell.
            string anyMap;
            using (var db = new OsuDbContext(_source.Kind))
            {
                db.EnsureSchema();
                anyMap = db.Beatmaps.Select(b => b.FilePath).FirstOrDefault();
            }
            bool libraryIsFromThisFolder = anyMap == null
                || (_source.Root != null && anyMap.StartsWith(_source.Root, StringComparison.OrdinalIgnoreCase));
            if (libraryIsFromThisFolder) RunBackgroundScan();

            _showingClient = true;
            ClientCombo.SelectedIndex = _client == OsuClient.Lazer ? 1 : 0;
            _showingClient = false;
            ToolTip.SetTip(FolderButton, _client == OsuClient.Lazer ? "Pick your osu!lazer data folder" : "Pick your osu! Songs folder");
        }

        private void ClientCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // OpenLibrary sets the selection to match _client; only a user's pick switches.
            if (_showingClient || _source == null) return;
            var picked = ClientCombo.SelectedIndex == 1 ? OsuClient.Lazer : OsuClient.Stable;
            if (picked == _client) return;

            _client = picked;
            BeatmapGrid.ItemsSource = null;
            OpenLibrary();
            UpdateGrid();
            SaveSettings();
        }

        private async void RunBackgroundScan()
        {
            ProgressPanel.IsVisible = true;
            PlayButton.IsEnabled = false;
            // Switching mid-scan would leave this scan's progress on the other library's screen.
            ClientCombo.IsEnabled = false;

            var progress = new Progress<int>(percent =>
            {
                ScanProgressBar.Value = percent;
                ScanProgressText.Text = $"Scanning... {percent}%";
            });

            try
            {
                if (!System.IO.Directory.Exists(_source.Root))
                {
                    await MessageDialog.ShowAsync(this, _client == OsuClient.Lazer
                        ? "Could not find your osu!lazer data folder.\n\nPick it with Folder…: it's the folder holding client.realm and a folder called files."
                        : $"Could not find osu! at {_source.Root}.\n\nIf you installed it somewhere else, pick your Songs folder with Folder….",
                        "Folder not found", DialogButtons.Ok, DialogIcon.Warning);
                    return;
                }

                // A model update (new app version, new model files) makes every stored
                // tag stale, and the scan below only tags new maps. Re-tag first.
                if (TaggedWithModel != _classifier.ModelId)
                {
                    var retagProgress = new Progress<int>(percent =>
                    {
                        ScanProgressBar.Value = percent;
                        ScanProgressText.Text = $"Updating tags for the new model... {percent}%";
                    });
                    await _libraryService.RetagLibraryAsync(_source, retagProgress);
                    TaggedWithModel = _classifier.ModelId;
                    SaveSettings();
                }

                // Libraries stored before the mapper and CS/AR/OD/HP were recorded: fill those in once.
                var detailsProgress = new Progress<int>(percent =>
                {
                    ScanProgressBar.Value = percent;
                    ScanProgressText.Text = $"Reading mapper and CS/AR/OD/HP... {percent}%";
                });
                await _libraryService.FillMissingDetailsAsync(_source.Kind, detailsProgress);

                await _libraryService.ScanLibraryAsync(_source, progress);
            }
            catch (Exception ex)
            {
                await MessageDialog.ShowAsync(this, $"Something went wrong while scanning. Press Ctrl+C to copy this report.\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "Scan failed", DialogButtons.Ok, DialogIcon.Error);
            }
            finally
            {
                ProgressPanel.IsVisible = false;
                PlayButton.IsEnabled = true;
                ClientCombo.IsEnabled = true;
                UpdateGrid();
            }
        }

        // --- UI UTILITY HANDLERS ---

        private IBrush Resource(string key) =>
            this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

        private void BeatmapGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool selected = BeatmapGrid.SelectedItem is BeatmapRecord;
            SelectionText.Text = BeatmapGrid.SelectedItem is BeatmapRecord map
                ? $"{map.Artist} - {map.Title} [{map.Version}]"
                : "Select a map, or double-click it, to find it in osu!'s song select.";
            SelectionText.Foreground = Resource(selected ? "Brush.Text" : "Brush.TextMuted");
        }

        // Alternate row shading. Rows are recycled as the list scrolls, so it follows the index.
        private void BeatmapGrid_LoadingRow(object sender, DataGridRowEventArgs e) =>
            e.Row.Classes.Set("alt", e.Row.Index % 2 == 1);

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateGrid();

        // The tag box suggests tags for the one being typed, after the last comma, and keeps
        // a leading "-" (exclude) when one is picked.
        private void SetUpTagSuggestions(IEnumerable<string> tags)
        {
            TagSearchBox.ItemsSource = tags.ToList();
            TagSearchBox.ItemFilter = (search, item) =>
            {
                string last = LastTag(search).Trim().TrimStart('-').Trim();
                return item is string tag && (last.Length == 0 || tag.Contains(last, StringComparison.OrdinalIgnoreCase));
            };
            TagSearchBox.TextSelector = (search, item) =>
            {
                search ??= "";
                int comma = search.LastIndexOf(',');
                string before = comma >= 0 ? search[..(comma + 1)] + " " : "";
                string dash = LastTag(search).TrimStart().StartsWith('-') ? "-" : "";
                return before + dash + item;
            };
        }

        private static string LastTag(string text)
        {
            text ??= "";
            int comma = text.LastIndexOf(',');
            return comma >= 0 ? text[(comma + 1)..] : text;
        }

        private void Slider_ValueChanged(object sender, EventArgs e)
        {
            UpdateFilterLabels();
            UpdateGrid();
        }

        private void ResetSlider_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is RangeSlider slider)
            {
                slider.UpperValue = slider.Maximum;
                slider.LowerValue = slider.Minimum;
            }
        }

        private void UpdateFilterLabels()
        {
            if (_ranges == null) return;
            foreach (var range in _ranges)
                ShowRange(range.Slider, range.Label, range.Reset, range.Format, range.Unit);
        }

        // Describes a range the way UpperBound/LowerBound filter it: a handle at the end of the
        // track is "no limit", so both open reads "Any" and one open end reads "Up to x" or "x+".
        private void ShowRange(RangeSlider slider, TextBlock label, Button reset, Func<double, string> format, string unit)
        {
            bool openLow = slider.LowerValue <= slider.Minimum;
            bool openHigh = slider.UpperValue >= slider.Maximum;
            bool active = !(openLow && openHigh);

            if (!active) label.Text = "Any";
            else if (openLow) label.Text = $"Up to {format(slider.UpperValue)}{unit}";
            else if (openHigh) label.Text = $"{format(slider.LowerValue)}+{unit}";
            else label.Text = $"{format(slider.LowerValue)} – {format(slider.UpperValue)}{unit}";

            label.Foreground = Resource(active ? "Brush.Accent" : "Brush.TextMuted");
            slider.Foreground = Resource(active ? "Brush.AccentStrong" : "Brush.BorderStrong");
            reset.IsVisible = active;
        }

        private async void UpdateGrid()
        {
            if (_ranges == null || _source == null) return;
            // Each restored value fires its own change event; one refresh at the end is enough.
            if (_restoringSettings) return;

            string searchText = (SearchBox.Text ?? "").ToLower().Trim();
            string tagText = (TagSearchBox.Text ?? "").ToLower().Trim();
            double maxStars = UpperBound(StarSlider);
            if (double.IsPositiveInfinity(maxStars) && searchText.Length == 0) maxStars = GimmickStarThreshold;

            var tagQueries = tagText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .ToList();

            var requiredTags = tagQueries.Where(t => !t.StartsWith("-")).ToList();
            var excludedTags = tagQueries.Where(t => t.StartsWith("-") && t.Length > 1)
                                         .Select(t => t.Substring(1).Trim())
                                         .ToList();

            var filter = new MapFilter
            {
                SearchText = searchText,
                RequiredTags = requiredTags,
                ExcludedTags = excludedTags,
                Stars = new Bounds(LowerBound(StarSlider), maxStars),
                Bpm = BoundsOf(BpmSlider),
                LengthMinutes = BoundsOf(LengthSlider),
                CS = BoundsOf(CsSlider),
                AR = BoundsOf(ArSlider),
                OD = BoundsOf(OdSlider),
                HP = BoundsOf(HpSlider),
            };

            var client = _client;
            var results = await _libraryService.SearchBeatmapsAsync(client, filter);
            // The user switched client while this ran: these rows belong to the other library.
            if (client != _client) return;
            BeatmapGrid.ItemsSource = new DataGridCollectionView(results);
            ApplySort();

            string name = client == OsuClient.Lazer ? "osu!lazer" : "osu!stable";
            string where = System.IO.Directory.Exists(_source?.Root) ? _source.Root : "folder not found";
            LibraryStatusText.Text = $"{name}  ·  {where}  ·  {results.Count:N0} maps shown";
        }

        private void ApplySort()
        {
            if (BeatmapGrid.ItemsSource is not DataGridCollectionView view) return;
            view.SortDescriptions.Clear();
            foreach (var (path, direction) in _sort)
            {
                if (BeatmapGrid.Columns.Any(c => c.SortMemberPath == path))
                    view.SortDescriptions.Add(DataGridSortDescription.FromPath(path, direction));
            }
        }

        // Click: ascending, descending, unsorted. Shift-click sorts by more than one column.
        private void BeatmapGrid_Sorting(object sender, DataGridColumnEventArgs e)
        {
            e.Handled = true;
            string path = e.Column.SortMemberPath;
            int current = _sort.FindIndex(s => s.Path == path);
            ListSortDirection? next = current < 0 ? ListSortDirection.Ascending
                : _sort[current].Direction == ListSortDirection.Ascending ? ListSortDirection.Descending
                : null;

            if (!_shiftOnLastClick) _sort.Clear();
            else _sort.RemoveAll(s => s.Path == path);

            if (next != null) _sort.Add((path, next.Value));
            ApplySort();
        }

        private void RestoreSettings(AppSettings settings)
        {
            _restoringSettings = true;
            SearchBox.Text = settings.SearchText ?? "";
            TagSearchBox.Text = settings.TagText ?? "";
            SetRange(StarSlider, settings.MinStars, settings.MaxStars);
            SetRange(BpmSlider, settings.MinBpm, settings.MaxBpm);
            SetRange(LengthSlider, settings.MinLength, settings.MaxLength);
            SetRange(CsSlider, settings.MinCS, settings.MaxCS);
            SetRange(ArSlider, settings.MinAR, settings.MaxAR);
            SetRange(OdSlider, settings.MinOD, settings.MaxOD);
            SetRange(HpSlider, settings.MinHP, settings.MaxHP);
            RestoreColumns(settings.Columns);
            _sort = (settings.Sort ?? new List<SortSetting>())
                .Select(s => (s.Column, s.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending))
                .ToList();
            _restoringSettings = false;
        }

        private void SaveSettings()
        {
            SettingsService.Save(new AppSettings
            {
                Client = _client,
                SongsFolder = _songsFolder,
                LazerDataFolder = _lazerDataFolder,
                GameProcessNames = _gameProcessNames,
                SearchText = SearchBox.Text,
                TagText = TagSearchBox.Text,
                MinStars = Finite(LowerBound(StarSlider)),
                MaxStars = Finite(UpperBound(StarSlider)),
                MinBpm = Finite(LowerBound(BpmSlider)),
                MaxBpm = Finite(UpperBound(BpmSlider)),
                MinLength = Finite(LowerBound(LengthSlider)),
                MaxLength = Finite(UpperBound(LengthSlider)),
                MinCS = Finite(LowerBound(CsSlider)),
                MaxCS = Finite(UpperBound(CsSlider)),
                MinAR = Finite(LowerBound(ArSlider)),
                MaxAR = Finite(UpperBound(ArSlider)),
                MinOD = Finite(LowerBound(OdSlider)),
                MaxOD = Finite(UpperBound(OdSlider)),
                MinHP = Finite(LowerBound(HpSlider)),
                MaxHP = Finite(UpperBound(HpSlider)),
                Columns = CurrentColumns(),
                Sort = _sort.Select(s => new SortSetting { Column = s.Path, Descending = s.Direction == ListSortDirection.Descending }).ToList(),
                TaggedWithModel = _taggedWithModel,
                LazerTaggedWithModel = _lazerTaggedWithModel,
                Window = CurrentPlacement()
            });
        }

        // Saved positions are in device-independent units (as the Windows-only version stored
        // them); Avalonia places windows in pixels, so they're scaled by the screen's DPI.
        private void RestorePlacement(WindowPlacement placement)
        {
            if (placement == null || placement.Width < MinWidth || placement.Height < MinHeight) return;

            var screens = Screens?.All;
            if (screens == null || screens.Count == 0) return;
            // Skip a position that is no longer on any screen (e.g. a monitor was unplugged).
            var screen = screens.FirstOrDefault(s =>
                s.Bounds.Intersects(new PixelRect((int)(placement.Left * s.Scaling), (int)(placement.Top * s.Scaling),
                                                  (int)(placement.Width * s.Scaling), (int)(placement.Height * s.Scaling))));
            if (screen == null) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)(placement.Left * screen.Scaling), (int)(placement.Top * screen.Scaling));
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximized) WindowState = WindowState.Maximized;
        }

        private void RememberNormalBounds()
        {
            if (WindowState != WindowState.Normal) return;
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
        }

        // The normal-state bounds, even while maximised or minimised, so un-maximising later
        // returns to the size the user chose.
        private WindowPlacement CurrentPlacement()
        {
            if (_normalSize.Width <= 0) return null;
            double scaling = DesktopScaling > 0 ? DesktopScaling : 1;
            return new WindowPlacement
            {
                Left = _normalPosition.X / scaling,
                Top = _normalPosition.Y / scaling,
                Width = _normalSize.Width,
                Height = _normalSize.Height,
                Maximized = WindowState == WindowState.Maximized
            };
        }

        // null (open end) puts the handle at the end of the track, i.e. back to "no limit".
        private static void SetRange(RangeSlider slider, double? lower, double? upper)
        {
            slider.UpperValue = upper ?? slider.Maximum;
            slider.LowerValue = lower ?? slider.Minimum;
        }

        private static double? Finite(double bound) => double.IsInfinity(bound) ? null : bound;

        // A handle parked at the end of its track means "no limit". Otherwise anything outside the
        // track's range (under 1 minute, over 300 BPM, over 10 stars) could never be shown at all.
        private static double LowerBound(RangeSlider s) => s.LowerValue <= s.Minimum ? double.NegativeInfinity : s.LowerValue;
        private static double UpperBound(RangeSlider s) => s.UpperValue >= s.Maximum ? double.PositiveInfinity : s.UpperValue;
        private static Bounds BoundsOf(RangeSlider s) => new Bounds(LowerBound(s), UpperBound(s));

        // --- MAP LIST COLUMNS ---
        // Right-clicking a column header opens a menu to show or hide columns and size them
        // to fit. Columns are identified by the property they show (SortMemberPath).

        private static string ColumnName(DataGridColumn column) =>
            column.Header as string == "★" ? "Stars (★)" : column.Header as string;

        private void BeatmapGrid_ContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var header = (e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true);
            if (header == null) return;   // only the header row has a menu
            e.Handled = true;

            var clicked = BeatmapGrid.Columns.FirstOrDefault(c => Equals(c.Header, header.Content));
            var menu = new ContextMenu();
            var toggles = new List<MenuItem>();

            // The list always keeps at least one column: the last one shown can't be unticked.
            void UpdateToggles()
            {
                int shown = BeatmapGrid.Columns.Count(c => c.IsVisible);
                foreach (var toggle in toggles) toggle.IsEnabled = !(toggle.IsChecked && shown == 1);
            }

            foreach (var column in BeatmapGrid.Columns)
            {
                var toggle = new MenuItem
                {
                    Header = ColumnName(column),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = column.IsVisible,
                    StaysOpenOnClick = true
                };
                toggle.Click += (_, _) =>
                {
                    column.IsVisible = toggle.IsChecked;
                    UpdateToggles();
                    SaveSettings();
                };
                toggles.Add(toggle);
                menu.Items.Add(toggle);
            }
            UpdateToggles();

            menu.Items.Add(new Separator());
            if (clicked != null)
                AddMenuAction(menu, $"Size “{ColumnName(clicked)}” to fit", () => SizeColumnsToFit(new[] { clicked }, fillWindow: false));
            AddMenuAction(menu, "Size all columns to fit", () => SizeColumnsToFit(BeatmapGrid.Columns.Where(c => c.IsVisible), fillWindow: true));
            AddMenuAction(menu, "Reset columns", ResetColumns);

            menu.Open(header);
        }

        private static void AddMenuAction(ContextMenu menu, string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        // Measures each column against its header and the rows currently on screen (the list
        // only creates the rows you can see), then pins the result so it doesn't keep changing
        // as you scroll. With fillWindow, the text columns (the ones that share the space by
        // default) split the width left over in proportion to how much text they hold, so
        // long names can't push the other columns out of view; otherwise every column gets
        // exactly its content width.
        private void SizeColumnsToFit(IEnumerable<DataGridColumn> columns, bool fillWindow)
        {
            var list = columns.ToList();
            foreach (var column in list) column.Width = DataGridLength.Auto;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var column in list)
                {
                    bool sharesSpace = fillWindow && _defaultColumns[column].Width.IsStar;
                    column.Width = new DataGridLength(column.ActualWidth, sharesSpace ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel);
                }
                SaveSettings();
            }, DispatcherPriority.Background);
        }

        private void ResetColumns()
        {
            foreach (var (column, (width, visible)) in _defaultColumns)
            {
                column.Width = width;
                column.IsVisible = visible;
            }
            SaveSettings();
        }

        private void RestoreColumns(List<ColumnSetting> saved)
        {
            if (saved == null) return;
            foreach (var setting in saved)
            {
                var column = BeatmapGrid.Columns.FirstOrDefault(c => c.SortMemberPath == setting.Key);
                if (column == null) continue;
                column.IsVisible = setting.Visible;
                if (setting.Width > 0)
                    column.Width = new DataGridLength(setting.Width, setting.Star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel);
            }
            // A hand-edited settings file could hide everything; an empty list helps nobody.
            if (BeatmapGrid.Columns.All(c => !c.IsVisible)) ResetColumns();
        }

        private List<ColumnSetting> CurrentColumns() =>
            BeatmapGrid.Columns.Select(c => new ColumnSetting
            {
                Key = c.SortMemberPath,
                Visible = c.IsVisible,
                Star = c.Width.IsStar,
                Width = c.Width.IsStar || c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth
            }).ToList();

        private void PlayButton_Click(object sender, RoutedEventArgs e) => LaunchSelectedMap();

        // Double-clicking a map (not the header) finds it in osu!.
        private void BeatmapGrid_DoubleTapped(object sender, TappedEventArgs e)
        {
            if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) != null)
                LaunchSelectedMap();
        }

        private async void ChangeFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Start where the current library is, or the home folder when there isn't one.
            string start = System.IO.Directory.Exists(_source?.Root) ? _source.Root
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = _client == OsuClient.Lazer
                    ? "Select your osu!lazer data folder (the one holding client.realm and files)"
                    : "Select your osu! Songs folder",
                AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start)
            });
            string newPath = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(newPath) || newPath.Equals(_source.Root, StringComparison.OrdinalIgnoreCase)) return;

            if (_client == OsuClient.Lazer)
            {
                if (!LazerLocationService.IsDataFolder(newPath))
                {
                    await MessageDialog.ShowAsync(this, "That isn't an osu!lazer data folder. The right one holds client.realm and a folder called files.",
                        "Not a lazer folder", DialogButtons.Ok, DialogIcon.Warning);
                    return;
                }
                _lazerDataFolder = newPath;
                _source = new LazerFilesSource(newPath);
            }
            else
            {
                _songsFolder = newPath;
                _source = new StableSongsSource(newPath);
            }
            _liveTrackerService.StartTracking(_source);

            using (var db = new OsuDbContext(_source.Kind))
            {
                db.Beatmaps.RemoveRange(db.Beatmaps);
                db.SaveChanges();
            }

            BeatmapGrid.ItemsSource = null;
            RunBackgroundScan();
        }

        private async Task UpdateAppAsync()
        {
            try
            {
                // This fork's own releases. The original project's would replace the lazer
                // support with its stable-only build (it ships as a different app, OsuScoutNew).
                var mgr = new UpdateManager(new GithubSource("https://github.com/FrasierGH/OsuScout", null, false));

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion != null)
                {
                    var result = await MessageDialog.ShowAsync(this,
                        $"A new update ({newVersion.TargetFullRelease.Version}) is available!\n\nWould you like to download and restart the app now?\nIf you click No, it will silently download and update automatically after you close the app.",
                        "Update available", DialogButtons.YesNo, DialogIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        await mgr.DownloadUpdatesAsync(newVersion);
                        SaveSettings(); // the restart exits without closing the window normally
                        mgr.ApplyUpdatesAndRestart(newVersion);
                    }
                    else
                    {
                        await mgr.DownloadUpdatesAsync(newVersion);
                        mgr.WaitExitThenApplyUpdates(newVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update failed: {ex.Message}");
            }
        }

        private async void LaunchSelectedMap()
        {
            if (BeatmapGrid.SelectedItem is not BeatmapRecord selectedMap) return;
            try
            {
                string searchQuery = GameClients.SongSelectSearch(_client, selectedMap.BeatmapID, selectedMap.Artist, selectedMap.Title, selectedMap.Version);
                if (Clipboard != null) await Clipboard.SetTextAsync(searchQuery);

                switch (SystemInteropService.FocusOsuProcess(_client, _gameProcessNames))
                {
                    case FocusResult.NotRunning:
                        await MessageDialog.ShowAsync(this, $"osu! isn't running, so Scoutsu couldn't switch to it.\n\nThe search is on your clipboard ({searchQuery}): paste it into song select once osu! is open.",
                            "osu! isn't running", DialogButtons.Ok, DialogIcon.Information);
                        break;
                    case FocusResult.CouldNotFocus:
                        // Wayland (and X11 without xdotool) doesn't let an app bring another to the front.
                        await MessageDialog.ShowAsync(this, $"The search is on your clipboard ({searchQuery}).\n\nSwitch to osu! and paste it into song select: this desktop doesn't let Scoutsu bring osu! to the front itself.",
                            "Copied", DialogButtons.Ok, DialogIcon.Information);
                        break;
                }
            }
            catch (Exception ex)
            {
                await MessageDialog.ShowAsync(this, $"Couldn't switch to osu!: {ex.Message}", "Couldn't switch to osu!", DialogButtons.Ok, DialogIcon.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            ScanLog.MarkAppClosed();
            if (_source != null) SaveSettings();
            _liveTrackerService?.Dispose();
            _classifier?.Dispose();
            base.OnClosed(e);
        }
    }
}
