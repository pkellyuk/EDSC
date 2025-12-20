using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace EDSC.ViewModels
{
    /// <summary>
    /// ViewModel for the connection view (desktop) - QR code and IP selection.
    /// </summary>
    public class ConnectionViewModel : ViewModelBase
    {
        private string _statusMessage;
        private bool _showQrCode;
        private Bitmap? _qrCodeImage;
        private string _qrCodeUrl;
        private ObservableCollection<string> _localIpAddresses;
        private string _selectedLocalIpAddress;

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

        public bool ShowQrCode
        {
            get
            {
                return _showQrCode;
            }
            set
            {
                if (_showQrCode == value)
                {
                    return;
                }

                _showQrCode = value;
                OnPropertyChanged(nameof(ShowQrCode));
            }
        }

        public Bitmap? QrCodeImage
        {
            get
            {
                return _qrCodeImage;
            }
            set
            {
                if (_qrCodeImage == value)
                {
                    return;
                }

                _qrCodeImage = value;
                OnPropertyChanged(nameof(QrCodeImage));
            }
        }

        public string QrCodeUrl
        {
            get
            {
                if (string.IsNullOrEmpty(_qrCodeUrl))
                {
                    return string.Empty;
                }
                return _qrCodeUrl;
            }
            set
            {
                if (_qrCodeUrl == value)
                {
                    return;
                }

                _qrCodeUrl = value;
                OnPropertyChanged(nameof(QrCodeUrl));
            }
        }

        public ObservableCollection<string> LocalIpAddresses
        {
            get
            {
                if (_localIpAddresses == null)
                {
                    return new ObservableCollection<string>();
                }
                return _localIpAddresses;
            }
            set
            {
                if (_localIpAddresses == value)
                {
                    return;
                }

                _localIpAddresses = value;
                OnPropertyChanged(nameof(LocalIpAddresses));
            }
        }

        public string SelectedLocalIpAddress
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedLocalIpAddress))
                {
                    return string.Empty;
                }
                return _selectedLocalIpAddress;
            }
            set
            {
                if (_selectedLocalIpAddress == value)
                {
                    return;
                }

                _selectedLocalIpAddress = value;
                OnPropertyChanged(nameof(SelectedLocalIpAddress));
                LocalIpAddressChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<string>? LocalIpAddressChanged;

        public ConnectionViewModel()
        {
            Debug.WriteLine("[ConnectionVM] Entry: Constructor");

            _statusMessage = "Select an IP address to generate the QR code";
            _showQrCode = false;
            _qrCodeImage = null;
            _qrCodeUrl = string.Empty;
            _localIpAddresses = new ObservableCollection<string>();
            _selectedLocalIpAddress = string.Empty;

            Debug.WriteLine("[ConnectionVM] Exit: Constructor");
        }

        public void SetQrCode(Bitmap qrCodeImage, string url)
        {
            if (qrCodeImage == null)
            {
                return;
            }

            QrCodeImage = qrCodeImage;
            QrCodeUrl = url ?? string.Empty;
            ShowQrCode = true;
            StatusMessage = "Scan the QR code to open the web controls";
        }

        public void SetLocalIpAddresses(IEnumerable<string> addresses)
        {
            if (addresses == null)
            {
                return;
            }

            LocalIpAddresses.Clear();
            foreach (var address in addresses)
            {
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                LocalIpAddresses.Add(address);
            }

            if (LocalIpAddresses.Count > 0 && string.IsNullOrEmpty(SelectedLocalIpAddress))
            {
                SelectedLocalIpAddress = LocalIpAddresses[0];
            }
        }
    }

    /// <summary>
    /// Base class for ViewModels with property change notification.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
