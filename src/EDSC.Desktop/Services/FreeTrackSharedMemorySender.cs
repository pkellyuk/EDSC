using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Writes head pose straight into the FreeTrack shared-memory block that Opentrack's
    /// NPClient / freetrackclient proxy DLLs read, so a TrackIR-aware game receives the
    /// pose without Opentrack running. The DLLs themselves still come from an Opentrack
    /// install, which registers their location in the registry.
    ///
    /// Layout mirrors FTHeap in opentrack/freetrackclient/fttypes.h:
    ///   FTData  (92 bytes): DataID, CamWidth, CamHeight, Yaw, Pitch, Roll, X, Y, Z,
    ///                       RawYaw..RawZ, X1..Y4
    ///   GameID  (4), table (8), GameID2 (4)
    /// </summary>
    public sealed class FreeTrackSharedMemorySender : IDisposable
    {
        private const string MapName = "FT_SharedMem";
        private const string MutexName = "FT_Mutext";
        private const int HeapSize = 108;

        private const int OffsetDataId = 0;
        private const int OffsetCamWidth = 4;
        private const int OffsetCamHeight = 8;
        private const int OffsetYaw = 12;
        private const int OffsetPitch = 16;
        private const int OffsetRoll = 20;
        private const int OffsetX = 24;
        private const int OffsetY = 28;
        private const int OffsetZ = 32;
        private const int OffsetRawYaw = 36;
        private const int OffsetRawPitch = 40;
        private const int OffsetRawRoll = 44;
        private const int OffsetRawX = 48;
        private const int OffsetRawY = 52;
        private const int OffsetRawZ = 56;
        private const int OffsetGameId = 92;
        private const int OffsetTable = 96;
        private const int OffsetGameId2 = 104;

        private const string NpClientRegistryKey = @"Software\NaturalPoint\NATURALPOINT\NPClient Location";
        private const string FreeTrackRegistryKey = @"Software\Freetrack\FreeTrackClient";
        private const string DummyTrackIrExe = "TrackIR.exe";
        private const string GamesCsvRelativePath = @"..\doc\settings\facetracknoir supported games.csv";

        private const int EliteDangerousGameId = 3475;
        private static readonly byte[] EliteDangerousTable = { 0xEC, 0x5E, 0x48, 0xA9, 0xBE, 0x18, 0x2E, 0xA1 };

        private readonly object _lock = new object();
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;
        private Mutex? _mutex;
        private Process? _dummyTrackIr;
        private int _lastGameId = -1;
        private uint _dataId = 1;

        public bool IsOpen
        {
            get
            {
                lock (_lock)
                {
                    return _view != null;
                }
            }
        }

        /// <summary>
        /// Name of the game that has registered with the proxy DLL, or empty if none yet.
        /// </summary>
        public string ConnectedGame { get; private set; } = string.Empty;

        /// <summary>
        /// Human-readable state for the UI.
        /// </summary>
        public string Status { get; private set; } = "Direct output off";

        /// <summary>
        /// Folder the game will load NPClient.dll from, as registered by Opentrack, or null if not registered.
        /// </summary>
        public static string? GetRegisteredNpClientPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(NpClientRegistryKey);
                var path = key?.GetValue("Path") as string;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                return path.Replace('/', '\\').TrimEnd('\\');
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FreeTrackSender] Registry read failed: {ex.Message}");
                return null;
            }
        }

        public static string? GetRegisteredFreeTrackPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(FreeTrackRegistryKey);
                var path = key?.GetValue("Path") as string;
                return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\').TrimEnd('\\');
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Create the shared-memory block and start the dummy TrackIR process games look for.
        /// </summary>
        public bool Open()
        {
            lock (_lock)
            {
                if (_view != null)
                {
                    return true;
                }

                var npClientPath = GetRegisteredNpClientPath();
                var freeTrackPath = GetRegisteredFreeTrackPath();

                if (npClientPath == null && freeTrackPath == null)
                {
                    Status = "NPClient DLL not registered. Run Opentrack once with the 'freetrack 2.0 Enhanced' output selected.";
                    Debug.WriteLine("[FreeTrackSender] No NPClient or FreeTrack registry path");
                    return false;
                }

                if (npClientPath != null && !File.Exists(Path.Combine(npClientPath, "NPClient64.dll")))
                {
                    Status = $"NPClient64.dll missing from registered folder: {npClientPath}";
                    Debug.WriteLine($"[FreeTrackSender] {Status}");
                    return false;
                }

                try
                {
                    _map = MemoryMappedFile.CreateOrOpen(MapName, HeapSize, MemoryMappedFileAccess.ReadWrite);
                    _view = _map.CreateViewAccessor(0, HeapSize, MemoryMappedFileAccess.ReadWrite);
                    _mutex = new Mutex(false, MutexName);

                    _dataId = 1;
                    _lastGameId = -1;
                    ConnectedGame = string.Empty;

                    // Same initial header Opentrack writes
                    _view.Write(OffsetDataId, (int)_dataId);
                    _view.Write(OffsetCamWidth, 100);
                    _view.Write(OffsetCamHeight, 250);
                    _view.Write(OffsetGameId2, 0);
                    for (int i = 0; i < 8; i++)
                    {
                        _view.Write(OffsetTable + i, (byte)0);
                    }
                    _view.Flush();

                    StartDummyTrackIr(npClientPath);

                    Status = "Waiting for game to connect";
                    Debug.WriteLine("[FreeTrackSender] Shared memory opened");
                    return true;
                }
                catch (Exception ex)
                {
                    Status = $"Cannot open shared memory: {ex.Message}";
                    Debug.WriteLine($"[FreeTrackSender] {Status}");
                    CloseInternal();
                    return false;
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                CloseInternal();
                Status = "Direct output off";
            }
        }

        /// <summary>
        /// Publish one pose. Angles in degrees, translation in centimetres, all relative to the
        /// centred position. Input convention: positive yaw and roll to the left, positive pitch up.
        /// Pitch is inverted on the way in to match what the game expects.
        /// </summary>
        public void WritePose(double yawDeg, double pitchDeg, double rollDeg, double xCm, double yCm, double zCm)
        {
            lock (_lock)
            {
                if (_view == null)
                {
                    return;
                }

                bool locked = false;
                try
                {
                    if (_mutex != null)
                    {
                        try
                        {
                            locked = _mutex.WaitOne(16);
                        }
                        catch (AbandonedMutexException)
                        {
                            locked = true;
                        }

                        if (!locked)
                        {
                            return;
                        }
                    }

                    // Pitch is negated: confirmed in-game that Elite reads positive FreeTrack pitch as looking down,
                    // and Opentrack negates pitch on this path as well.
                    const double d2r = Math.PI / 180.0;
                    float yaw = (float)(yawDeg * d2r);
                    float pitch = (float)(-pitchDeg * d2r);
                    float roll = (float)(rollDeg * d2r);
                    float x = (float)(xCm * 10.0);
                    float y = (float)(yCm * 10.0);
                    float z = (float)(zCm * 10.0);

                    _view.Write(OffsetYaw, yaw);
                    _view.Write(OffsetPitch, pitch);
                    _view.Write(OffsetRoll, roll);
                    _view.Write(OffsetX, x);
                    _view.Write(OffsetY, y);
                    _view.Write(OffsetZ, z);

                    _view.Write(OffsetRawYaw, yaw);
                    _view.Write(OffsetRawPitch, pitch);
                    _view.Write(OffsetRawRoll, roll);
                    _view.Write(OffsetRawX, x);
                    _view.Write(OffsetRawY, y);
                    _view.Write(OffsetRawZ, z);

                    int gameId = _view.ReadInt32(OffsetGameId);
                    if (gameId != _lastGameId)
                    {
                        // The proxy DLL copies the table the first time GameID2 == GameID,
                        // so the table has to land before the ID is echoed back.
                        var table = LookupTable(gameId, out var gameName);
                        for (int i = 0; i < 8; i++)
                        {
                            _view.Write(OffsetTable + i, table[i]);
                        }

                        _view.Write(OffsetGameId2, gameId);
                        _dataId = 0;
                        _view.Write(OffsetDataId, (int)_dataId);

                        _lastGameId = gameId;
                        ConnectedGame = gameId == 0 ? string.Empty : gameName;
                        Status = gameId == 0
                            ? "Waiting for game to connect"
                            : $"Game connected: {gameName} (id {gameId})";
                        Debug.WriteLine($"[FreeTrackSender] {Status}");
                    }
                    else
                    {
                        _dataId++;
                        if (_dataId > (1u << 29))
                        {
                            _dataId = 0;
                        }
                        _view.Write(OffsetDataId, (int)_dataId);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FreeTrackSender] Write failed: {ex.Message}");
                }
                finally
                {
                    if (locked)
                    {
                        try
                        {
                            _mutex?.ReleaseMutex();
                        }
                        catch
                        {
                            // Ignore release failures
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void CloseInternal()
        {
            StopDummyTrackIr();

            _view?.Dispose();
            _view = null;

            _map?.Dispose();
            _map = null;

            _mutex?.Dispose();
            _mutex = null;

            _lastGameId = -1;
            ConnectedGame = string.Empty;
        }

        private void StartDummyTrackIr(string? npClientPath)
        {
            if (string.IsNullOrEmpty(npClientPath))
            {
                return;
            }

            var exe = Path.Combine(npClientPath, DummyTrackIrExe);
            if (!File.Exists(exe))
            {
                Debug.WriteLine($"[FreeTrackSender] Dummy {DummyTrackIrExe} not found at {exe}");
                return;
            }

            try
            {
                // Some games refuse TrackIR input unless a process by this name exists
                foreach (var existing in Process.GetProcessesByName("TrackIR"))
                {
                    existing.Dispose();
                    Debug.WriteLine("[FreeTrackSender] A TrackIR process is already running; not starting another");
                    return;
                }

                _dummyTrackIr = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                Debug.WriteLine("[FreeTrackSender] Dummy TrackIR process started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FreeTrackSender] Could not start dummy TrackIR: {ex.Message}");
                _dummyTrackIr = null;
            }
        }

        private void StopDummyTrackIr()
        {
            if (_dummyTrackIr == null)
            {
                return;
            }

            try
            {
                if (!_dummyTrackIr.HasExited)
                {
                    _dummyTrackIr.Kill();
                    _dummyTrackIr.WaitForExit(500);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FreeTrackSender] Could not stop dummy TrackIR: {ex.Message}");
            }
            finally
            {
                _dummyTrackIr.Dispose();
                _dummyTrackIr = null;
            }
        }

        /// <summary>
        /// Encryption table for a game ID: Opentrack's shipped CSV if available, with Elite Dangerous built in.
        /// A table of all zeros means the game uses unencrypted data.
        /// </summary>
        private static byte[] LookupTable(int gameId, out string gameName)
        {
            gameName = "Unknown game";
            var table = new byte[8];

            if (gameId == 0)
            {
                gameName = string.Empty;
                return table;
            }

            try
            {
                var npClientPath = GetRegisteredNpClientPath();
                if (npClientPath != null)
                {
                    var csvPath = Path.GetFullPath(Path.Combine(npClientPath, GamesCsvRelativePath));
                    if (File.Exists(csvPath) && TryParseGamesCsv(csvPath, gameId, table, out var name))
                    {
                        gameName = name;
                        return table;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FreeTrackSender] Games CSV lookup failed: {ex.Message}");
            }

            if (gameId == EliteDangerousGameId)
            {
                gameName = "Elite: Dangerous";
                Array.Copy(EliteDangerousTable, table, 8);
            }

            return table;
        }

        /// <summary>
        /// Same parsing as opentrack/csv/csv.cpp: the 22-hex-digit key holds the eight table bytes
        /// in a scrambled order with three fuzz bytes around them.
        /// </summary>
        private static bool TryParseGamesCsv(string csvPath, int gameId, byte[] table, out string gameName)
        {
            gameName = string.Empty;
            var idText = gameId.ToString(CultureInfo.InvariantCulture);

            foreach (var rawLine in File.ReadLines(csvPath))
            {
                var line = rawLine.TrimEnd('\r', '\n');
                if (line.Length == 0)
                {
                    continue;
                }

                var cols = line.Split(';');
                if (cols.Length != 8)
                {
                    continue;
                }

                if (!string.Equals(cols[6], idText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                gameName = cols[1];
                var proto = cols[3];
                var key = cols[7];

                Array.Clear(table, 0, 8);

                if (proto == "V160" || key.Length != 22)
                {
                    return true;
                }

                var pairs = new byte[11];
                for (int i = 0; i < 11; i++)
                {
                    if (!byte.TryParse(key.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pairs[i]))
                    {
                        return true;
                    }
                }

                // pairs[0], pairs[1], pairs[10] are fuzz; the rest map as in do_scanf
                table[3] = pairs[2];
                table[2] = pairs[3];
                table[1] = pairs[4];
                table[0] = pairs[5];
                table[7] = pairs[6];
                table[6] = pairs[7];
                table[5] = pairs[8];
                table[4] = pairs[9];
                return true;
            }

            return false;
        }
    }
}
