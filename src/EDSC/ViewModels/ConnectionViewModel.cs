using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using EDSC.Models.Discovery;
using EDSC.Services.Discovery;

namespace EDSC.ViewModels
{
    /// <summary>
    /// ViewModel for the connection view (mobile) - manages server discovery and connection
    /// </summary>
    public class ConnectionViewModel : ViewModelBase
    {
        private readonly IDiscoveryService _discoveryService;
        private ObservableCollection<DiscoveredServer> _discoveredServers;
        private DiscoveredServer _selectedServer;
        private bool _isDiscovering;
        private string _manualIpAddress;
        private int _manualPort;
        private string _statusMessage;
        private CancellationTokenSource _discoveryCts;

        public ObservableCollection<DiscoveredServer> DiscoveredServers
        {
            get
            {
                if (_discoveredServers == null)
                {
                    return new ObservableCollection<DiscoveredServer>();
                }
                return _discoveredServers;
            }
            set
            {
                if (_discoveredServers == value)
                {
                    return;
                }

                _discoveredServers = value;
                OnPropertyChanged(nameof(DiscoveredServers));
                OnPropertyChanged(nameof(HasDiscoveredServers));
            }
        }

        public DiscoveredServer SelectedServer
        {
            get
            {
                return _selectedServer;
            }
            set
            {
                if (_selectedServer == value)
                {
                    return;
                }

                _selectedServer = value;
                OnPropertyChanged(nameof(SelectedServer));
            }
        }

        public bool IsDiscovering
        {
            get
            {
                return _isDiscovering;
            }
            set
            {
                if (_isDiscovering == value)
                {
                    return;
                }

                _isDiscovering = value;
                OnPropertyChanged(nameof(IsDiscovering));
            }
        }

        public string ManualIpAddress
        {
            get
            {
                if (string.IsNullOrEmpty(_manualIpAddress))
                {
                    return string.Empty;
                }
                return _manualIpAddress;
            }
            set
            {
                if (_manualIpAddress == value)
                {
                    return;
                }

                _manualIpAddress = value;
                OnPropertyChanged(nameof(ManualIpAddress));
            }
        }

        public int ManualPort
        {
            get
            {
                if (_manualPort <= 0)
                {
                    return 5000;
                }
                return _manualPort;
            }
            set
            {
                if (_manualPort == value)
                {
                    return;
                }

                _manualPort = value;
                OnPropertyChanged(nameof(ManualPort));
            }
        }

        public string StatusMessage
        {
            get
            {
                if (string.IsNullOrEmpty(_statusMessage))
                {
                    return string.Empty;
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

        public bool HasDiscoveredServers => DiscoveredServers != null && DiscoveredServers.Count > 0;

        public ICommand DiscoverServersCommand { get; }
        public ICommand ConnectCommand { get; }

        public ConnectionViewModel(IDiscoveryService discoveryService)
        {
            Debug.WriteLine("[ConnectionVM] Entry: Constructor");

            if (discoveryService == null)
            {
                throw new ArgumentNullException(nameof(discoveryService));
            }

            _discoveryService = discoveryService;
            _discoveredServers = new ObservableCollection<DiscoveredServer>();
            _manualPort = 5000;
            _manualIpAddress = string.Empty;
            _statusMessage = "Ready to discover servers";

            // Initialize commands
            DiscoverServersCommand = new RelayCommand(async () => await DiscoverServersAsync(), () => !IsDiscovering);
            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), CanConnect);

            Debug.WriteLine("[ConnectionVM] Exit: Constructor");
        }

        public async Task DiscoverServersAsync()
        {
            Debug.WriteLine("[ConnectionVM] Entry: DiscoverServersAsync");

            if (IsDiscovering)
            {
                Debug.WriteLine("[ConnectionVM] Already discovering, returning");
                return;
            }

            IsDiscovering = true;
            StatusMessage = "Discovering servers...";
            DiscoveredServers.Clear();

            try
            {
                Debug.WriteLine("[ConnectionVM] Starting discovery");

                _discoveryCts = new CancellationTokenSource();
                var servers = await _discoveryService.DiscoverServersAsync(timeoutSeconds: 3, _discoveryCts.Token);

                if (servers == null)
                {
                    Debug.WriteLine("[ConnectionVM] Discovery returned null");
                    StatusMessage = "Discovery failed";
                    return;
                }

                Debug.WriteLine($"[ConnectionVM] Found {servers.Count} server(s)");

                foreach (var server in servers)
                {
                    if (server == null)
                    {
                        continue;
                    }

                    DiscoveredServers.Add(server);
                    Debug.WriteLine($"[ConnectionVM] Added server: {server.Name}");
                }

                OnPropertyChanged(nameof(HasDiscoveredServers));

                if (servers.Count == 0)
                {
                    StatusMessage = "No servers found. Try manual connection.";
                    Debug.WriteLine("[ConnectionVM] No servers found");
                }
                else
                {
                    StatusMessage = $"Found {servers.Count} server(s)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectionVM] Error during discovery: {ex.Message}");
                StatusMessage = $"Discovery error: {ex.Message}";
            }
            finally
            {
                IsDiscovering = false;
                _discoveryCts?.Dispose();
                _discoveryCts = null;
                Debug.WriteLine("[ConnectionVM] Exit: DiscoverServersAsync");
            }
        }

        private bool CanConnect()
        {
            // Can connect if we have a selected server OR manual IP is entered
            if (SelectedServer != null)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(ManualIpAddress) && ManualPort > 0 && ManualPort <= 65535)
            {
                return true;
            }

            return false;
        }

        private async Task ConnectAsync()
        {
            Debug.WriteLine("[ConnectionVM] Entry: ConnectAsync");

            DiscoveredServer serverToConnect = null;

            if (SelectedServer != null)
            {
                serverToConnect = SelectedServer;
                Debug.WriteLine($"[ConnectionVM] Connecting to selected server: {serverToConnect.Name}");
            }
            else if (!string.IsNullOrEmpty(ManualIpAddress))
            {
                serverToConnect = new DiscoveredServer
                {
                    Name = "Manual Entry",
                    IpAddress = ManualIpAddress,
                    Port = ManualPort,
                    IsManualEntry = true,
                    DiscoveredAt = DateTime.UtcNow
                };
                Debug.WriteLine($"[ConnectionVM] Connecting to manual entry: {serverToConnect.IpAddress}:{serverToConnect.Port}");
            }

            if (serverToConnect == null)
            {
                Debug.WriteLine("[ConnectionVM] No server to connect to");
                StatusMessage = "Please select a server or enter IP address";
                return;
            }

            try
            {
                StatusMessage = $"Connecting to {serverToConnect.Name}...";

                // TODO: Implement actual connection logic
                // This would typically involve:
                // 1. Creating HTTP client
                // 2. Testing connection to server
                // 3. Storing connection details
                // 4. Navigating to main view

                Debug.WriteLine($"[ConnectionVM] Connection to {serverToConnect.IpAddress}:{serverToConnect.Port} would be established here");
                StatusMessage = $"Connected to {serverToConnect.Name}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectionVM] Error connecting: {ex.Message}");
                StatusMessage = $"Connection error: {ex.Message}";
            }

            Debug.WriteLine("[ConnectionVM] Exit: ConnectAsync");
            await Task.CompletedTask;
        }

        public void CancelDiscovery()
        {
            Debug.WriteLine("[ConnectionVM] Entry: CancelDiscovery");

            if (_discoveryCts == null)
            {
                Debug.WriteLine("[ConnectionVM] No active discovery to cancel");
                return;
            }

            _discoveryCts.Cancel();
            StatusMessage = "Discovery cancelled";

            Debug.WriteLine("[ConnectionVM] Exit: CancelDiscovery");
        }
    }

    /// <summary>
    /// Base class for ViewModels with property change notification
    /// </summary>
    public abstract class ViewModelBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Simple relay command implementation
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute = null)
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

            return _canExecute();
        }

        public async void Execute(object parameter)
        {
            if (_executeAsync == null)
            {
                return;
            }

            await _executeAsync();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
