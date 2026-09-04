using EDSC.Desktop.Services;
using EDSC.Models;
using EDSC.Services;
using EDSC.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EDSC.Desktop.ViewModels
{
    /// <summary>
    /// Editable view of one button.
    /// </summary>
    public sealed class ButtonItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _label = string.Empty;
        private string _key = string.Empty;
        private string _color = "#4CAF50";
        private string _iconSvg = string.Empty;
        private string _icon = string.Empty;
        private int _size = 80;
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id
        {
            get { return _id; }
            set { Set(ref _id, value ?? string.Empty, nameof(Id)); }
        }

        public string Label
        {
            get { return _label; }
            set { Set(ref _label, value ?? string.Empty, nameof(Label)); }
        }

        public string Key
        {
            get { return _key; }
            set
            {
                if (Set(ref _key, value ?? string.Empty, nameof(Key)))
                {
                    Raise(nameof(IsUnbound));
                    Raise(nameof(KeyDisplay));
                }
            }
        }

        public string Color
        {
            get { return _color; }
            set { Set(ref _color, string.IsNullOrWhiteSpace(value) ? "#4CAF50" : value, nameof(Color)); }
        }

        public string IconSvg
        {
            get { return _iconSvg; }
            set { Set(ref _iconSvg, value ?? string.Empty, nameof(IconSvg)); }
        }

        public string Icon
        {
            get { return _icon; }
            set { Set(ref _icon, value ?? string.Empty, nameof(Icon)); }
        }

        public int Size
        {
            get { return _size; }
            set { Set(ref _size, value <= 0 ? 80 : value, nameof(Size)); }
        }

        private string _voiceAliases = string.Empty;

        /// <summary>
        /// Comma-separated spoken phrases that also trigger this button.
        /// </summary>
        public string VoiceAliases
        {
            get { return _voiceAliases; }
            set { Set(ref _voiceAliases, value ?? string.Empty, nameof(VoiceAliases)); }
        }

        private static List<string> SplitAliases(string? text)
        {
            return (text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => a.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { Set(ref _isSelected, value, nameof(IsSelected)); }
        }

        public bool IsUnbound
        {
            get { return string.IsNullOrWhiteSpace(_key); }
        }

        public string KeyDisplay
        {
            get { return IsUnbound ? "not bound" : _key; }
        }

        public static ButtonItem FromConfig(ButtonConfig config)
        {
            return new ButtonItem
            {
                Id = config.Id,
                Label = config.Label,
                Key = config.Key,
                Color = config.Color,
                IconSvg = config.IconSvg,
                Icon = config.Icon,
                Size = config.Size,
                VoiceAliases = config.VoiceAliases != null ? string.Join(", ", config.VoiceAliases) : string.Empty
            };
        }

        public ButtonConfig ToConfig(string category)
        {
            return new ButtonConfig
            {
                Id = Id,
                Label = Label,
                Key = Key,
                Color = Color,
                IconSvg = IconSvg,
                Icon = Icon,
                Size = Size,
                VoiceAliases = SplitAliases(VoiceAliases),
                Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim()
            };
        }

        private bool Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            Raise(name);
            return true;
        }

        private void Raise(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// A named group of buttons in display order.
    /// </summary>
    public sealed class ButtonCategory : INotifyPropertyChanged
    {
        private string _name;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ButtonItem> Buttons { get; } = new ObservableCollection<ButtonItem>();

        public ButtonCategory(string name)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "General" : name.Trim();
            Buttons.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        }

        public string Name
        {
            get { return _name; }
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "General" : value.Trim();
                if (_name == next)
                {
                    return;
                }

                _name = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public bool IsEmpty
        {
            get { return Buttons.Count == 0; }
        }
    }

    /// <summary>
    /// Drag-and-drop editor for the web control layout.
    /// </summary>
    public sealed class ButtonEditorViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly EliteBindingsService _eliteBindings;
        private AppConfig _config = new AppConfig();
        private ButtonItem? _selectedButton;
        private string _statusMessage = string.Empty;
        private string _newCategoryName = string.Empty;
        private bool _isDirty;

        public ObservableCollection<ButtonCategory> Categories { get; } = new ObservableCollection<ButtonCategory>();
        public ObservableCollection<string> AvailableIcons { get; } = new ObservableCollection<string>();

        public ICommand ReloadCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand AddCategoryCommand { get; }
        public ICommand AddButtonCommand { get; }
        public ICommand DeleteButtonCommand { get; }
        public ICommand ImportFromEliteCommand { get; }
        public ICommand BindInEliteCommand { get; }

        /// <summary>
        /// Raised after a successful save so the server can pick up the new layout.
        /// </summary>
        public event EventHandler? Saved;

        public ButtonEditorViewModel(IConfigurationService configService, EliteBindingsService eliteBindings)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _eliteBindings = eliteBindings ?? throw new ArgumentNullException(nameof(eliteBindings));

            ReloadCommand = new RelayCommand(LoadAsync, () => true);
            SaveCommand = new RelayCommand(SaveAsync, () => true);
            AddCategoryCommand = new RelayCommand(() => { AddCategory(); return Task.CompletedTask; }, () => true);
            AddButtonCommand = new RelayCommand(() => { AddButton(); return Task.CompletedTask; }, () => true);
            DeleteButtonCommand = new RelayCommand<ButtonItem>(item => { DeleteButton(item); return Task.CompletedTask; }, _ => true);
            ImportFromEliteCommand = new RelayCommand(ImportFromEliteAsync, () => true);
            BindInEliteCommand = new RelayCommand(BindInEliteAsync, () => true);
            ConfirmBindCommand = new RelayCommand(ConfirmBindAsync, () => true);
            CancelBindCommand = new RelayCommand(() => { CancelBind(); return Task.CompletedTask; }, () => true);

            LoadAvailableIcons();
        }

        public ButtonItem? SelectedButton
        {
            get { return _selectedButton; }
            set
            {
                if (_selectedButton == value)
                {
                    return;
                }

                if (_selectedButton != null)
                {
                    _selectedButton.IsSelected = false;
                }

                _selectedButton = value;

                if (_selectedButton != null)
                {
                    _selectedButton.IsSelected = true;
                }

                OnPropertyChanged(nameof(SelectedButton));
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection
        {
            get { return _selectedButton != null; }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set
            {
                if (_statusMessage == value)
                {
                    return;
                }

                _statusMessage = value ?? string.Empty;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public string NewCategoryName
        {
            get { return _newCategoryName; }
            set
            {
                if (_newCategoryName == value)
                {
                    return;
                }

                _newCategoryName = value ?? string.Empty;
                OnPropertyChanged(nameof(NewCategoryName));
            }
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            private set
            {
                if (_isDirty == value)
                {
                    return;
                }

                _isDirty = value;
                OnPropertyChanged(nameof(IsDirty));
            }
        }

        public void SelectButton(ButtonItem? item)
        {
            SelectedButton = item;
        }

        public async Task LoadAsync()
        {
            try
            {
                _config = await _configService.LoadConfigurationAsync() ?? new AppConfig();
                Populate(_config.Buttons);
                IsDirty = false;
                StatusMessage = $"Loaded {Categories.Sum(c => c.Buttons.Count)} buttons in {Categories.Count} categories";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditor] Load failed: {ex.Message}");
                StatusMessage = $"Load failed: {ex.Message}";
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                var buttons = new List<ButtonConfig>();
                foreach (var category in Categories)
                {
                    foreach (var item in category.Buttons)
                    {
                        if (string.IsNullOrWhiteSpace(item.Id))
                        {
                            item.Id = MakeId(item.Label);
                        }

                        buttons.Add(item.ToConfig(category.Name));
                    }
                }

                // Merge into the file on disk so tracking settings saved since we loaded are kept
                var latest = await _configService.LoadConfigurationAsync() ?? _config;
                latest.Buttons = buttons;
                latest.EliteControlSchemesPath = _config.EliteControlSchemesPath ?? latest.EliteControlSchemesPath;
                latest.LastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                latest.LastUpdatedBy = "desktop-editor";
                latest.ConfigVersion = Math.Max(1, latest.ConfigVersion) + 1;

                await _configService.SaveConfigurationAsync(latest);
                _config = latest;
                IsDirty = false;
                StatusMessage = $"Saved {buttons.Count} buttons. The phone picks this up within a few seconds.";
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditor] Save failed: {ex.Message}");
                StatusMessage = $"Save failed: {ex.Message}";
            }
        }

        public async Task ImportFromEliteAsync()
        {
            try
            {
                StatusMessage = "Reading Elite Dangerous bindings...";

                var result = await Task.Run(() => _eliteBindings.Load(_config.EliteControlSchemesPath));

                if (!result.Found)
                {
                    StatusMessage = string.Join(" ", result.Notes.DefaultIfEmpty("Elite Dangerous bindings not found."));
                    return;
                }

                // Work on the current editor state so unsaved edits are respected
                _config.Buttons = Categories.SelectMany(c => c.Buttons.Select(b => b.ToConfig(c.Name))).ToList();

                var summary = EliteBindingsService.ApplyToConfig(_config, result);
                Populate(_config.Buttons);
                IsDirty = true;

                var presetNote = result.Notes.FirstOrDefault(n => n.StartsWith("Active presets", StringComparison.Ordinal));
                StatusMessage = presetNote != null ? $"{summary} {presetNote}. Press Save to apply." : $"{summary} Press Save to apply.";

                foreach (var note in result.Notes)
                {
                    Debug.WriteLine($"[ButtonEditor] Elite import: {note}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditor] Import failed: {ex.Message}");
                StatusMessage = $"Import failed: {ex.Message}";
            }
        }

        // Bind-in-Elite is a two step flow: show exactly what would change, then write only on confirmation
        private List<EliteAction> _pendingBindActions = new List<EliteAction>();
        private bool _isBindPlanVisible;

        public ObservableCollection<string> BindPlan { get; } = new ObservableCollection<string>();

        public bool IsBindPlanVisible
        {
            get { return _isBindPlanVisible; }
            private set
            {
                if (_isBindPlanVisible == value)
                {
                    return;
                }

                _isBindPlanVisible = value;
                OnPropertyChanged(nameof(IsBindPlanVisible));
            }
        }

        public ICommand ConfirmBindCommand { get; private set; } = null!;
        public ICommand CancelBindCommand { get; private set; } = null!;

        private List<EliteAction> UnboundCatalogueActions()
        {
            var unboundIds = Categories
                .SelectMany(c => c.Buttons)
                .Where(b => b.IsUnbound && !string.IsNullOrEmpty(b.Id))
                .Select(b => b.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return EliteActionCatalog.Actions
                .Where(a => unboundIds.Contains(a.Id))
                .ToList();
        }

        /// <summary>
        /// Step 1: work out what binding in Elite would change and show it. Nothing is written.
        /// </summary>
        public async Task BindInEliteAsync()
        {
            try
            {
                IsBindPlanVisible = false;
                BindPlan.Clear();

                var actions = UnboundCatalogueActions();
                if (actions.Count == 0)
                {
                    StatusMessage = "No unbound buttons match a known Elite action. Import from Elite Dangerous first.";
                    return;
                }

                var plan = await Task.Run(() => _eliteBindings.BindMissingKeys(actions, _config.EliteControlSchemesPath, dryRun: true));

                if (!plan.Success)
                {
                    StatusMessage = plan.Message;
                    return;
                }

                foreach (var line in plan.Details)
                {
                    BindPlan.Add(line);
                }

                _pendingBindActions = actions;
                IsBindPlanVisible = true;
                StatusMessage = "Review the changes below. Nothing has been written to the game yet.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditor] Bind plan failed: {ex.Message}");
                StatusMessage = $"Could not plan the bindings: {ex.Message}";
            }
        }

        /// <summary>
        /// Step 2: write the planned keys into the game's Custom preset, then re-import.
        /// </summary>
        public async Task ConfirmBindAsync()
        {
            var actions = _pendingBindActions;
            IsBindPlanVisible = false;
            BindPlan.Clear();

            if (actions.Count == 0)
            {
                return;
            }

            try
            {
                StatusMessage = $"Writing keys for {actions.Count} action(s) into the game's Custom preset...";

                var result = await Task.Run(() => _eliteBindings.BindMissingKeys(actions, _config.EliteControlSchemesPath));

                foreach (var detail in result.Details)
                {
                    Debug.WriteLine($"[ButtonEditor] Bind: {detail}");
                }

                if (!result.Success)
                {
                    StatusMessage = result.Message;
                    return;
                }

                // Pick the new keys up straight away
                var bindings = await Task.Run(() => _eliteBindings.Load(_config.EliteControlSchemesPath));
                _config.Buttons = Categories.SelectMany(c => c.Buttons.Select(b => b.ToConfig(c.Name))).ToList();
                EliteBindingsService.ApplyToConfig(_config, bindings);
                Populate(_config.Buttons);
                IsDirty = true;

                var backup = result.BackupPath != null ? $" Backup saved as {Path.GetFileName(result.BackupPath)}." : string.Empty;
                StatusMessage = $"{result.Message}{backup} Press Save to keep the new keys.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditor] Bind in Elite failed: {ex.Message}");
                StatusMessage = $"Bind failed: {ex.Message}";
            }
            finally
            {
                _pendingBindActions = new List<EliteAction>();
            }
        }

        public void CancelBind()
        {
            _pendingBindActions = new List<EliteAction>();
            BindPlan.Clear();
            IsBindPlanVisible = false;
            StatusMessage = "No changes were made to the game's bindings.";
        }

        /// <summary>
        /// Move a button into a category, optionally placing it before another button.
        /// </summary>
        public void MoveButton(ButtonItem item, ButtonCategory target, ButtonItem? before)
        {
            if (item == null || target == null || item == before)
            {
                return;
            }

            var source = Categories.FirstOrDefault(c => c.Buttons.Contains(item));
            if (source != null)
            {
                source.Buttons.Remove(item);
            }

            int index = before != null ? target.Buttons.IndexOf(before) : -1;
            if (index < 0)
            {
                target.Buttons.Add(item);
            }
            else
            {
                target.Buttons.Insert(index, item);
            }

            SelectedButton = item;
            IsDirty = true;
            StatusMessage = $"Moved '{item.Label}' to {target.Name}. Press Save to apply.";
        }

        public void AddCategory()
        {
            var name = NewCategoryName.Trim();
            if (name.Length == 0)
            {
                StatusMessage = "Type a category name first.";
                return;
            }

            if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"Category '{name}' already exists.";
                return;
            }

            Categories.Add(new ButtonCategory(name));
            NewCategoryName = string.Empty;
            IsDirty = true;
            StatusMessage = $"Added category '{name}'. Drag buttons into it.";
        }

        public void AddButton()
        {
            var target = SelectedButton != null
                ? Categories.FirstOrDefault(c => c.Buttons.Contains(SelectedButton))
                : null;
            target ??= Categories.FirstOrDefault();

            if (target == null)
            {
                target = new ButtonCategory("General");
                Categories.Add(target);
            }

            var item = new ButtonItem
            {
                Id = MakeId("newbutton" + (Categories.Sum(c => c.Buttons.Count) + 1)),
                Label = "New Button",
                Key = string.Empty,
                Color = "#4B5563"
            };

            target.Buttons.Add(item);
            SelectedButton = item;
            IsDirty = true;
            StatusMessage = "Added a button. Set its label and key on the right.";
        }

        public void DeleteButton(ButtonItem? item)
        {
            item ??= SelectedButton;
            if (item == null)
            {
                return;
            }

            var owner = Categories.FirstOrDefault(c => c.Buttons.Contains(item));
            owner?.Buttons.Remove(item);

            if (SelectedButton == item)
            {
                SelectedButton = null;
            }

            IsDirty = true;
            StatusMessage = $"Removed '{item.Label}'. Press Save to apply.";
        }

        public void RemoveEmptyCategory(ButtonCategory category)
        {
            if (category == null || !category.IsEmpty)
            {
                return;
            }

            Categories.Remove(category);
            IsDirty = true;
        }

        public void MarkDirty()
        {
            IsDirty = true;
        }

        private void Populate(IEnumerable<ButtonConfig>? buttons)
        {
            SelectedButton = null;
            Categories.Clear();

            var order = new List<ButtonCategory>();
            var lookup = new Dictionary<string, ButtonCategory>(StringComparer.OrdinalIgnoreCase);

            foreach (var config in buttons ?? Enumerable.Empty<ButtonConfig>())
            {
                if (config == null)
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(config.Category) ? "General" : config.Category.Trim();
                if (!lookup.TryGetValue(name, out var category))
                {
                    category = new ButtonCategory(name);
                    lookup[name] = category;
                    order.Add(category);
                }

                var item = ButtonItem.FromConfig(config);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName != nameof(ButtonItem.IsSelected))
                    {
                        IsDirty = true;
                    }
                };
                category.Buttons.Add(item);
            }

            foreach (var category in order)
            {
                Categories.Add(category);
            }
        }

        private void LoadAvailableIcons()
        {
            AvailableIcons.Clear();
            AvailableIcons.Add(string.Empty);

            try
            {
                var iconsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
                if (Directory.Exists(iconsDir))
                {
                    foreach (var file in Directory.GetFiles(iconsDir, "*.svg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        AvailableIcons.Add(Path.GetFileName(file));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ButtonEditor] Icon listing failed: {ex.Message}");
            }
        }

        private static string MakeId(string label)
        {
            var chars = (label ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            var id = new string(chars);
            return id.Length == 0 ? "button" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() : id;
        }
    }
}
