using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using EDSC.Models;

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

        // Preview mode (what the desktop preview panel shows)
        private PreviewMode _previewMode = PreviewMode.CameraWithLandmarks;
        private PreviewModeOption? _selectedPreviewModeOption;

        // Pose output properties
        private bool _directOutputEnabled;
        private string _directOutputStatus;
        private ICommand? _centerCommand;
        private ICommand? _changeCenterHotkeyCommand;
        private ICommand? _resetTrackingCommand;
        private string _centerHotkey = "OEM_PLUS";
        private string _centerHotkeyDisplay = "=";
        private bool _isCapturingHotkey;

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

        private bool _hasVideoFrame;

        /// <summary>
        /// True while actual video frames are arriving; false when only poses arrive from the phone.
        /// </summary>
        public bool HasVideoFrame
        {
            get
            {
                return _hasVideoFrame;
            }
            set
            {
                if (_hasVideoFrame == value)
                {
                    return;
                }

                _hasVideoFrame = value;
                OnPropertyChanged(nameof(HasVideoFrame));
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
        /// The choices offered by the preview mode dropdown, in display order.
        /// </summary>
        public ObservableCollection<PreviewModeOption> PreviewModeOptions { get; } = new ObservableCollection<PreviewModeOption>
        {
            new PreviewModeOption(PreviewMode.Off, "Off (saves CPU; phone stops sending preview)"),
            new PreviewModeOption(PreviewMode.Camera, "Camera only"),
            new PreviewModeOption(PreviewMode.CameraWithLandmarks, "Camera with face mesh"),
            new PreviewModeOption(PreviewMode.LandmarksOnly, "Face mesh only (no camera image)")
        };

        /// <summary>
        /// The dropdown's selected entry. Bound by the view; changes flow through to <see cref="PreviewMode"/>.
        /// </summary>
        public PreviewModeOption? SelectedPreviewModeOption
        {
            get
            {
                return _selectedPreviewModeOption;
            }
            set
            {
                if (value == null || ReferenceEquals(_selectedPreviewModeOption, value))
                {
                    return;
                }

                Debug.WriteLine($"[ConnectionViewModel] SelectedPreviewModeOption -> {value.Mode}");
                _selectedPreviewModeOption = value;
                OnPropertyChanged(nameof(SelectedPreviewModeOption));
                PreviewMode = value.Mode;
            }
        }

        /// <summary>
        /// What the desktop preview panel shows. Setting it also updates the dropdown selection.
        /// </summary>
        public PreviewMode PreviewMode
        {
            get
            {
                return _previewMode;
            }
            set
            {
                if (_previewMode == value)
                {
                    return;
                }

                Debug.WriteLine($"[ConnectionViewModel] PreviewMode {_previewMode} -> {value}");
                _previewMode = value;
                OnPropertyChanged(nameof(PreviewMode));
                OnPropertyChanged(nameof(ShowPcPreview));

                foreach (var option in PreviewModeOptions)
                {
                    if (option.Mode != value)
                    {
                        continue;
                    }

                    SelectedPreviewModeOption = option;
                    break;
                }
            }
        }

        /// <summary>
        /// True when the preview panel shows anything at all (any mode other than Off).
        /// </summary>
        public bool ShowPcPreview
        {
            get
            {
                return _previewMode != PreviewMode.Off;
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

        public ICommand? ChangeCenterHotkeyCommand
        {
            get
            {
                return _changeCenterHotkeyCommand;
            }
            set
            {
                if (_changeCenterHotkeyCommand == value)
                {
                    return;
                }

                _changeCenterHotkeyCommand = value;
                OnPropertyChanged(nameof(ChangeCenterHotkeyCommand));
            }
        }

        public ICommand? ResetTrackingCommand
        {
            get
            {
                return _resetTrackingCommand;
            }
            set
            {
                if (_resetTrackingCommand == value)
                {
                    return;
                }

                _resetTrackingCommand = value;
                OnPropertyChanged(nameof(ResetTrackingCommand));
            }
        }

        /// <summary>
        /// Virtual key name of the re-centre hotkey, as stored in config.
        /// </summary>
        public string CenterHotkey
        {
            get
            {
                return string.IsNullOrEmpty(_centerHotkey) ? "OEM_PLUS" : _centerHotkey;
            }
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "OEM_PLUS" : value.Trim();
                if (_centerHotkey == next)
                {
                    return;
                }

                _centerHotkey = next;
                OnPropertyChanged(nameof(CenterHotkey));
                OnPropertyChanged(nameof(CenterButtonText));
            }
        }

        /// <summary>
        /// Friendly name of the hotkey for the current keyboard layout, set by the desktop app.
        /// </summary>
        public string CenterHotkeyDisplay
        {
            get
            {
                return string.IsNullOrEmpty(_centerHotkeyDisplay) ? CenterHotkey : _centerHotkeyDisplay;
            }
            set
            {
                if (_centerHotkeyDisplay == value)
                {
                    return;
                }

                _centerHotkeyDisplay = value ?? string.Empty;
                OnPropertyChanged(nameof(CenterHotkeyDisplay));
                OnPropertyChanged(nameof(CenterButtonText));
            }
        }

        public bool IsCapturingHotkey
        {
            get
            {
                return _isCapturingHotkey;
            }
            set
            {
                if (_isCapturingHotkey == value)
                {
                    return;
                }

                _isCapturingHotkey = value;
                OnPropertyChanged(nameof(IsCapturingHotkey));
                OnPropertyChanged(nameof(CenterButtonText));
                OnPropertyChanged(nameof(ChangeHotkeyButtonText));
            }
        }

        public string CenterButtonText
        {
            get
            {
                return $"Center view  (hotkey: {CenterHotkeyDisplay})";
            }
        }

        public string ChangeHotkeyButtonText
        {
            get
            {
                return IsCapturingHotkey ? "Press a key... (Esc cancels)" : "Change key";
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
            _selectedPreviewModeOption = PreviewModeOptions[2];   // CameraWithLandmarks, matching _previewMode's default
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
            _changeCenterHotkeyCommand = null;
            _centerHotkey = "OEM_PLUS";
            _centerHotkeyDisplay = "=";
            _isCapturingHotkey = false;

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

        /// <param name="frameImage">Frame to show.</param>
        /// <param name="fps">Frame rate to display.</param>
        /// <param name="trackingStatus">Status line to show, or null for the default.</param>
        /// <param name="preserveStatus">True to update only the image, leaving the status line and rate alone.</param>
        public void UpdateVideoFrame(Bitmap? frameImage, double fps, string? trackingStatus = null, bool preserveStatus = false)
        {
            VideoFrameImage = frameImage;
            HasVideoFrame = frameImage != null;

            if (frameImage == null)
            {
                return;
            }

            ShowVideoPreview = true;

            if (preserveStatus)
            {
                return;
            }

            VideoFps = fps.ToString("F1");
            VideoStatusText = string.IsNullOrEmpty(trackingStatus)
                ? "Receiving video stream"
                : trackingStatus;
        }

        /// <summary>
        /// Show the tracking panel with pose numbers, for poses computed on the phone.
        /// Any preview image already showing is left in place.
        /// </summary>
        public void UpdatePhoneTracking(string status, double posesPerSecond)
        {
            VideoFps = posesPerSecond.ToString("F1");
            VideoStatusText = string.IsNullOrEmpty(status) ? "Phone tracking" : status;
            ShowVideoPreview = true;
        }

        public void HideVideoPreview()
        {
            ShowVideoPreview = false;
            VideoFrameImage = null;
            HasVideoFrame = false;
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

    /// <summary>
    /// One entry in the preview mode dropdown: the mode plus its display text.
    /// </summary>
    public class PreviewModeOption
    {
        public PreviewModeOption(PreviewMode mode, string label)
        {
            Mode = mode;
            Label = label ?? mode.ToString();
        }

        public PreviewMode Mode { get; }

        public string Label { get; }

        public override string ToString()
        {
            return Label;
        }
    }
}
