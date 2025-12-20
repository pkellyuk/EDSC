using EDSC.Models;
using EDSC.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EDSC.ViewModels
{
    /// <summary>
    /// Main view model for the button grid interface
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly ICommandClient _commandClient;
        private readonly IConfigurationService _configService;
        private ObservableCollection<ButtonConfig> _buttons;
        private ObservableCollection<ButtonCategoryGroup> _categories;
        private string _statusMessage;
        private bool _isConnected;
        private string _serverAddress;

        public ObservableCollection<ButtonConfig> Buttons
        {
            get
            {
                if (_buttons == null)
                {
                    return new ObservableCollection<ButtonConfig>();
                }
                return _buttons;
            }
            set
            {
                if (_buttons == value)
                {
                    return;
                }

                _buttons = value;
                OnPropertyChanged(nameof(Buttons));
            }
        }

        public ObservableCollection<ButtonCategoryGroup> Categories
        {
            get
            {
                if (_categories == null)
                {
                    return new ObservableCollection<ButtonCategoryGroup>();
                }
                return _categories;
            }
            set
            {
                if (_categories == value)
                {
                    return;
                }

                _categories = value;
                OnPropertyChanged(nameof(Categories));
            }
        }

        public string StatusMessage
        {
            get
            {
                if (string.IsNullOrEmpty(_statusMessage))
                {
                    return "Not connected";
                }
                return _statusMessage;
            }
            set
            {
                if (_statusMessage == value)
                {
                    return;
                }

                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public bool IsConnected
        {
            get
            {
                return _isConnected;
            }
            set
            {
                if (_isConnected == value)
                {
                    return;
                }

                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectionStatus));
            }
        }

        public string ServerAddress
        {
            get
            {
                if (string.IsNullOrEmpty(_serverAddress))
                {
                    return "Not connected";
                }
                return _serverAddress;
            }
            set
            {
                if (_serverAddress == value)
                {
                    return;
                }

                _serverAddress = value;
                OnPropertyChanged(nameof(ServerAddress));
            }
        }

        public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";

        public ICommand ButtonPressCommand { get; }

        public MainViewModel(ICommandClient commandClient, IConfigurationService configService)
        {
            Debug.WriteLine("[MainViewModel] Entry: Constructor");

            if (commandClient == null)
            {
                throw new ArgumentNullException(nameof(commandClient));
            }

            if (configService == null)
            {
                throw new ArgumentNullException(nameof(configService));
            }

            _commandClient = commandClient;
            _configService = configService;
            _buttons = new ObservableCollection<ButtonConfig>();
            _categories = new ObservableCollection<ButtonCategoryGroup>();
            _statusMessage = "Initializing...";
            _isConnected = false;
            _serverAddress = string.Empty;

            ButtonPressCommand = new RelayCommand<ButtonConfig>(async (button) => await OnButtonPressedAsync(button), CanPressButton);

            Debug.WriteLine("[MainViewModel] Exit: Constructor");
        }

        public async Task InitializeAsync(string ipAddress, int port)
        {
            Debug.WriteLine($"[MainViewModel] Entry: InitializeAsync(ipAddress={ipAddress}, port={port})");

            try
            {
                // Set server address
                _commandClient.SetServerAddress(ipAddress, port);
                ServerAddress = $"{ipAddress}:{port}";

                // Test connection
                StatusMessage = "Testing connection...";
                var connected = await _commandClient.TestConnectionAsync();

                if (!connected)
                {
                    StatusMessage = "Connection failed";
                    IsConnected = false;
                    Debug.WriteLine("[MainViewModel] Connection test failed");
                    return;
                }

                IsConnected = true;
                StatusMessage = "Connected";
                Debug.WriteLine("[MainViewModel] Connected successfully");

                // Load configuration
                StatusMessage = "Loading buttons...";
                var config = await _configService.LoadConfigurationAsync();

                if (config == null || config.Buttons == null)
                {
                    Debug.WriteLine("[MainViewModel] Failed to load configuration");
                    StatusMessage = "Error loading buttons";
                    return;
                }

                // Populate buttons
                Buttons.Clear();
                Categories.Clear();
                foreach (var button in config.Buttons)
                {
                    if (button == null)
                    {
                        continue;
                    }

                    Buttons.Add(button);
                    Debug.WriteLine($"[MainViewModel] Added button: {button}");
                }

                var groupedButtons = Buttons
                    .GroupBy(button => string.IsNullOrWhiteSpace(button.Category) ? "General" : button.Category.Trim())
                    .OrderBy(group => group.Key);

                foreach (var group in groupedButtons)
                {
                    Categories.Add(new ButtonCategoryGroup(group.Key, group.ToList()));
                    Debug.WriteLine($"[MainViewModel] Added category: {group.Key} ({group.Count()} buttons)");
                }

                StatusMessage = $"Ready - {Buttons.Count} buttons loaded";
                Debug.WriteLine($"[MainViewModel] Loaded {Buttons.Count} buttons in {Categories.Count} categories");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] Error during initialization: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
                IsConnected = false;
            }

            Debug.WriteLine("[MainViewModel] Exit: InitializeAsync");
        }

        private bool CanPressButton(ButtonConfig button)
        {
            if (!IsConnected)
            {
                return false;
            }

            if (button == null)
            {
                return false;
            }

            return true;
        }

        private async Task OnButtonPressedAsync(ButtonConfig button)
        {
            Debug.WriteLine($"[MainViewModel] Entry: OnButtonPressedAsync(button={button})");

            if (button == null)
            {
                Debug.WriteLine("[MainViewModel] Button is null");
                return;
            }

            if (!IsConnected)
            {
                Debug.WriteLine("[MainViewModel] Not connected");
                StatusMessage = "Not connected to server";
                return;
            }

            try
            {
                Debug.WriteLine($"[MainViewModel] Sending command for button: {button.Id}");
                StatusMessage = $"Pressing {button.Label}...";

                var response = await _commandClient.SendCommandAsync(button.Id, button.Key);

                if (response == null)
                {
                    Debug.WriteLine("[MainViewModel] Response is null");
                    StatusMessage = "No response from server";
                    return;
                }

                if (response.Success)
                {
                    Debug.WriteLine($"[MainViewModel] Command successful: {response.Message}");
                    StatusMessage = $"{button.Label} pressed successfully";
                }
                else
                {
                    Debug.WriteLine($"[MainViewModel] Command failed: {response.Message}");
                    StatusMessage = $"Error: {response.Message}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] Error pressing button: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }

            Debug.WriteLine("[MainViewModel] Exit: OnButtonPressedAsync");
        }
    }

    /// <summary>
    /// Relay command with generic parameter
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _executeAsync;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Func<T, Task> executeAsync, Func<T, bool> canExecute = null)
        {
            if (executeAsync == null)
            {
                throw new ArgumentNullException(nameof(executeAsync));
            }

            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
            {
                return true;
            }

            if (parameter is T typedParameter)
            {
                return _canExecute(typedParameter);
            }

            return false;
        }

        public async void Execute(object parameter)
        {
            if (_executeAsync == null)
            {
                return;
            }

            if (parameter is T typedParameter)
            {
                await _executeAsync(typedParameter);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class ButtonCategoryGroup
    {
        public string Name { get; }
        public ObservableCollection<ButtonConfig> Buttons { get; }

        public ButtonCategoryGroup(string name, IEnumerable<ButtonConfig> buttons)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "General" : name.Trim();
            Buttons = new ObservableCollection<ButtonConfig>(buttons ?? Enumerable.Empty<ButtonConfig>());
        }
    }
}
