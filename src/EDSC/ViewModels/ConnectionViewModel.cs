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
        private FaceMeshFrame? _meshFrame;
        private string _videoStatusText;
        private string _videoFps;
        private double _translationScale;
        private double _yawScale;
        private double _rotationScale;
        private double _rollScale;
        private double _smoothingStrength;
        private double _gazeNudgeYaw;
        private double _gazeNudgePitch;
        private GazeIndicator? _gazeIndicator;

        // Whether the desktop preview panel draws the face mesh
        private bool _showPcPreview = true;

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

        private bool _hasMeshFrame;

        /// <summary>
        /// True while face mesh frames are arriving for the preview panel; false when only poses arrive.
        /// </summary>
        public bool HasMeshFrame
        {
            get
            {
                return _hasMeshFrame;
            }
            set
            {
                if (_hasMeshFrame == value)
                {
                    return;
                }

                _hasMeshFrame = value;
                OnPropertyChanged(nameof(HasMeshFrame));
            }
        }

        /// <summary>
        /// The latest face mesh for the preview panel, drawn by the FaceMeshView control.
        /// </summary>
        public FaceMeshFrame? MeshFrame
        {
            get
            {
                return _meshFrame;
            }
            set
            {
                if (ReferenceEquals(_meshFrame, value))
                {
                    return;
                }

                _meshFrame = value;
                OnPropertyChanged(nameof(MeshFrame));
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
        /// Fraction of the sideways eye gaze angle added to head yaw, 0 to 2. Phone-side tracking only.
        /// </summary>
        public double GazeNudgeYaw
        {
            get
            {
                return _gazeNudgeYaw;
            }
            set
            {
                if (Math.Abs(_gazeNudgeYaw - value) < 0.0001)
                {
                    return;
                }

                _gazeNudgeYaw = value;
                OnPropertyChanged(nameof(GazeNudgeYaw));
            }
        }

        /// <summary>
        /// Fraction of the up/down eye gaze angle added to head pitch, 0 to 2. Phone-side tracking only.
        /// </summary>
        public double GazeNudgePitch
        {
            get
            {
                return _gazeNudgePitch;
            }
            set
            {
                if (Math.Abs(_gazeNudgePitch - value) < 0.0001)
                {
                    return;
                }

                _gazeNudgePitch = value;
                OnPropertyChanged(nameof(GazeNudgePitch));
            }
        }

        /// <summary>
        /// Head and eye directions for the preview panel inset, or null to hide it.
        /// </summary>
        public GazeIndicator? GazeIndicator
        {
            get
            {
                return _gazeIndicator;
            }
            set
            {
                if (ReferenceEquals(_gazeIndicator, value))
                {
                    return;
                }

                _gazeIndicator = value;
                OnPropertyChanged(nameof(GazeIndicator));
            }
        }

        /// <summary>
        /// True to draw the face mesh in the desktop preview panel. Off saves a little CPU on the PC
        /// and tells the phone to stop sending mesh frames. The camera image never reaches the panel.
        /// </summary>
        public bool ShowPcPreview
        {
            get
            {
                return _showPcPreview;
            }
            set
            {
                if (_showPcPreview == value)
                {
                    return;
                }

                Debug.WriteLine($"[ConnectionViewModel] ShowPcPreview {_showPcPreview} -> {value}");
                _showPcPreview = value;
                OnPropertyChanged(nameof(ShowPcPreview));
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
            _meshFrame = null;
            _videoStatusText = "Waiting for video stream...";
            _videoFps = "0.0";
            _translationScale = 1.0;
            _yawScale = 1.0;
            _rotationScale = 1.0;
            _rollScale = 1.0;
            _smoothingStrength = 0.5;
            _gazeNudgeYaw = TrackingConfig.DefaultGazeNudgeYaw;
            _gazeNudgePitch = TrackingConfig.DefaultGazeNudgePitch;

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

        /// <summary>
        /// Show a face mesh frame in the preview panel, or clear it with null.
        /// </summary>
        /// <param name="frame">Mesh to draw, or null to clear the panel.</param>
        /// <param name="fps">Frame rate to display.</param>
        /// <param name="trackingStatus">Status line to show, or null for the default.</param>
        /// <param name="preserveStatus">True to update only the mesh, leaving the status line and rate alone.</param>
        public void UpdateMesh(FaceMeshFrame? frame, double fps, string? trackingStatus = null, bool preserveStatus = false)
        {
            MeshFrame = frame;
            HasMeshFrame = frame != null;

            if (frame == null)
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
            MeshFrame = null;
            HasMeshFrame = false;
            GazeIndicator = null;
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
