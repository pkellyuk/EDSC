using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
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

        // Video tracking properties
        private bool _showVideoPreview;
        private Bitmap? _videoFrameImage;
        private string _videoStatusText;
        private string _videoFps;
        private double _translationScale;
        private double _yawScale;
        private double _rotationScale;
        private double _rollScale;
        private double _smoothingStrength;

        // Pose output properties
        private bool _directOutputEnabled;
        private string _directOutputStatus;
        private ICommand? _centerCommand;

        // Certificate properties
        private string _certificateStatus;
        private ICommand? _installCertificateCommand;

        // URL command
        private ICommand? _openUrlCommand;

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

        public bool ShowVideoPreview
        {
            get
            {
                return _showVideoPreview;
            }
            set
            {
                if (_showVideoPreview == value)
                {
                    return;
                }

                _showVideoPreview = value;
                OnPropertyChanged(nameof(ShowVideoPreview));
            }
        }

        public Bitmap? VideoFrameImage
        {
            get
            {
                return _videoFrameImage;
            }
            set
            {
                if (_videoFrameImage == value)
                {
                    return;
                }

                _videoFrameImage = value;
                OnPropertyChanged(nameof(VideoFrameImage));
            }
        }

        public string VideoStatusText
        {
            get
            {
                if (string.IsNullOrEmpty(_videoStatusText))
                {
                    return "Waiting for video stream...";
                }
                return _videoStatusText;
            }
            set
            {
                if (_videoStatusText == value)
                {
                    return;
                }

                _videoStatusText = value;
                OnPropertyChanged(nameof(VideoStatusText));
            }
        }

        public string VideoFps
        {
            get
            {
                if (string.IsNullOrEmpty(_videoFps))
                {
                    return "0.0";
                }
                return _videoFps;
            }
            set
            {
                if (_videoFps == value)
                {
                    return;
                }

                _videoFps = value;
                OnPropertyChanged(nameof(VideoFps));
            }
        }

        public double TranslationScale
        {
            get
            {
                return _translationScale;
            }
            set
            {
                if (Math.Abs(_translationScale - value) < 0.0001)
                {
                    return;
                }

                _translationScale = value;
                OnPropertyChanged(nameof(TranslationScale));
            }
        }

        public double YawScale
        {
            get
            {
                return _yawScale;
            }
            set
            {
                if (Math.Abs(_yawScale - value) < 0.0001)
                {
                    return;
                }

                _yawScale = value;
                OnPropertyChanged(nameof(YawScale));
            }
        }

        public double RotationScale
        {
            get
            {
                return _rotationScale;
            }
            set
            {
                if (Math.Abs(_rotationScale - value) < 0.0001)
                {
                    return;
                }

                _rotationScale = value;
                OnPropertyChanged(nameof(RotationScale));
            }
        }

        public double RollScale
        {
            get
            {
                return _rollScale;
            }
            set
            {
                if (Math.Abs(_rollScale - value) < 0.0001)
                {
                    return;
                }

                _rollScale = value;
                OnPropertyChanged(nameof(RollScale));
            }
        }

        public double SmoothingStrength
        {
            get
            {
                return _smoothingStrength;
            }
            set
            {
                if (Math.Abs(_smoothingStrength - value) < 0.0001)
                {
                    return;
                }

                _smoothingStrength = value;
                OnPropertyChanged(nameof(SmoothingStrength));
            }
        }

        /// <summary>
        /// True to bypass Opentrack and feed the game's TrackIR interface directly.
        /// </summary>
        public bool DirectOutputEnabled
        {
            get
            {
                return _directOutputEnabled;
            }
            set
            {
                if (_directOutputEnabled == value)
                {
                    return;
                }

                _directOutputEnabled = value;
                OnPropertyChanged(nameof(DirectOutputEnabled));
            }
        }

        public string DirectOutputStatus
        {
            get
            {
                if (string.IsNullOrEmpty(_directOutputStatus))
                {
                    return string.Empty;
                }
                return _directOutputStatus;
            }
            set
            {
                if (_directOutputStatus == value)
                {
                    return;
                }

                _directOutputStatus = value;
                OnPropertyChanged(nameof(DirectOutputStatus));
            }
        }

        public ICommand? CenterCommand
        {
            get
            {
                return _centerCommand;
            }
            set
            {
                if (_centerCommand == value)
                {
                    return;
                }

                _centerCommand = value;
                OnPropertyChanged(nameof(CenterCommand));
            }
        }

        public string CertificateStatus
        {
            get
            {
                if (string.IsNullOrEmpty(_certificateStatus))
                {
                    return "Not Installed";
                }
                return _certificateStatus;
            }
            set
            {
                if (_certificateStatus == value)
                {
                    return;
                }

                _certificateStatus = value;
                OnPropertyChanged(nameof(CertificateStatus));
            }
        }

        public ICommand? InstallCertificateCommand
        {
            get
            {
                return _installCertificateCommand;
            }
            set
            {
                if (_installCertificateCommand == value)
                {
                    return;
                }

                _installCertificateCommand = value;
                OnPropertyChanged(nameof(InstallCertificateCommand));
            }
        }

        public ICommand? OpenUrlCommand
        {
            get
            {
                return _openUrlCommand;
            }
            set
            {
                if (_openUrlCommand == value)
                {
                    return;
                }

                _openUrlCommand = value;
                OnPropertyChanged(nameof(OpenUrlCommand));
            }
        }

        public ConnectionViewModel()
        {
            Debug.WriteLine("[ConnectionVM] Entry: Constructor");

            _statusMessage = "Select an IP address to generate the QR code";
            _showQrCode = false;
            _qrCodeImage = null;
            _qrCodeUrl = string.Empty;
            _localIpAddresses = new ObservableCollection<string>();
            _selectedLocalIpAddress = string.Empty;

            // Initialize video tracking properties
            _showVideoPreview = false;
            _videoFrameImage = null;
            _videoStatusText = "Waiting for video stream...";
            _videoFps = "0.0";
            _translationScale = 1.0;
            _yawScale = 1.0;
            _rotationScale = 1.0;
            _rollScale = 1.0;
            _smoothingStrength = 0.5;

            // Initialize pose output properties
            _directOutputEnabled = false;
            _directOutputStatus = string.Empty;
            _centerCommand = null;

            // Initialize certificate properties
            _certificateStatus = "Not Installed";
            _installCertificateCommand = null;

            // Initialize URL command
            _openUrlCommand = null;

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

        public void UpdateVideoFrame(Bitmap? frameImage, double fps, string? trackingStatus = null)
        {
            VideoFrameImage = frameImage;
            VideoFps = fps.ToString("F1");

            if (frameImage != null)
            {
                ShowVideoPreview = true;
                VideoStatusText = string.IsNullOrEmpty(trackingStatus)
                    ? "Receiving video stream"
                    : trackingStatus;
            }
        }

        public void HideVideoPreview()
        {
            ShowVideoPreview = false;
            VideoFrameImage = null;
            VideoFps = "0.0";
            VideoStatusText = "Waiting for video stream...";
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
