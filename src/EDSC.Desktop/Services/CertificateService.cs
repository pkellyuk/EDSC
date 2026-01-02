using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading.Tasks;
using System.ComponentModel;

namespace EDSC.Desktop.Services
{
    public class CertificateService
    {
        private const string CERT_FILENAME = "edsc-ssl.pfx";
        private const string CERT_PASSWORD = "edsc-local-cert";
        private const int CERT_KEY_SIZE = 2048;
        private const int CERT_VALIDITY_YEARS = 2;
        private const string CERT_SUBJECT = "CN=EDSC Development Certificate";

        private readonly string _certPath;
        private readonly string _appDataPath;

        public CertificateService()
        {
            Debug.WriteLine("[CertificateService] Entry: Constructor");

            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EDSC"
            );

            if (!Directory.Exists(_appDataPath))
            {
                Debug.WriteLine($"[CertificateService] Creating AppData directory: {_appDataPath}");
                Directory.CreateDirectory(_appDataPath);
            }

            _certPath = Path.Combine(_appDataPath, CERT_FILENAME);

            Debug.WriteLine($"[CertificateService] Certificate path: {_certPath}");
            Debug.WriteLine("[CertificateService] Exit: Constructor");
        }

        public async Task<CertificateResult> GenerateAndInstallCertificateAsync(string[] localIpAddresses)
        {
            Debug.WriteLine("[CertificateService] Entry: GenerateAndInstallCertificateAsync");

            if (localIpAddresses == null)
            {
                Debug.WriteLine("[CertificateService] localIpAddresses is null, using defaults");
                localIpAddresses = new[] { "127.0.0.1", "localhost" };
            }

            var result = new CertificateResult
            {
                Success = false,
                RequiresAdmin = false,
                CertificatePath = _certPath
            };

            try
            {
                Debug.WriteLine("[CertificateService] Generating self-signed certificate");

                // Generate certificate on background thread
                var certificate = await Task.Run(() => GenerateSelfSignedCertificate(localIpAddresses));

                if (certificate == null)
                {
                    Debug.WriteLine("[CertificateService] Certificate generation returned null");
                    result.Message = "Failed to generate certificate";
                    return result;
                }

                Debug.WriteLine("[CertificateService] Certificate generated successfully");
                Debug.WriteLine($"[CertificateService] Subject: {certificate.Subject}");
                Debug.WriteLine($"[CertificateService] Thumbprint: {certificate.Thumbprint}");
                Debug.WriteLine($"[CertificateService] Valid from: {certificate.NotBefore} to {certificate.NotAfter}");

                // Save certificate to file
                Debug.WriteLine($"[CertificateService] Saving certificate to: {_certPath}");
                var certBytes = certificate.Export(X509ContentType.Pfx, CERT_PASSWORD);
                await File.WriteAllBytesAsync(_certPath, certBytes);
                Debug.WriteLine("[CertificateService] Certificate saved to file");

                // Attempt to install to Windows Certificate Store
                bool installed = false;
                try
                {
                    Debug.WriteLine("[CertificateService] Attempting to install certificate to Trusted Root CA store");
                    InstallCertificateToStore(certificate);
                    installed = true;
                    Debug.WriteLine("[CertificateService] Certificate installed to store successfully");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"[CertificateService] Unauthorized access: {ex.Message}");
                    result.RequiresAdmin = true;
                    result.Message = "Certificate generated but administrator privileges required to install to system store";
                }
                catch (CryptographicException ex)
                {
                    Debug.WriteLine($"[CertificateService] Cryptographic exception: {ex.Message}");
                    result.RequiresAdmin = true;
                    result.Message = "Certificate generated but failed to install to system store (may require admin)";
                }

                if (installed)
                {
                    result.Success = true;
                    result.Message = "Certificate generated and installed successfully";
                }

                Debug.WriteLine($"[CertificateService] Installation result - Success: {result.Success}, RequiresAdmin: {result.RequiresAdmin}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CertificateService] Error generating certificate: {ex.Message}");
                Debug.WriteLine($"[CertificateService] Stack trace: {ex.StackTrace}");
                result.Message = $"Error: {ex.Message}";
            }

            Debug.WriteLine("[CertificateService] Exit: GenerateAndInstallCertificateAsync");
            return result;
        }

        public CertificateStatus GetCertificateStatus()
        {
            Debug.WriteLine("[CertificateService] Entry: GetCertificateStatus");

            try
            {
                // Check if certificate file exists
                if (!File.Exists(_certPath))
                {
                    Debug.WriteLine("[CertificateService] Certificate file does not exist");
                    Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - NotGenerated");
                    return CertificateStatus.NotGenerated;
                }

                Debug.WriteLine("[CertificateService] Certificate file exists, loading...");

                // Load certificate from file
                X509Certificate2? certificate = null;
                try
                {
                    certificate = new X509Certificate2(_certPath, CERT_PASSWORD);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CertificateService] Error loading certificate: {ex.Message}");
                    Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - Error");
                    return CertificateStatus.Error;
                }

                if (certificate == null)
                {
                    Debug.WriteLine("[CertificateService] Certificate is null after loading");
                    Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - Error");
                    return CertificateStatus.Error;
                }

                Debug.WriteLine($"[CertificateService] Certificate loaded - Thumbprint: {certificate.Thumbprint}");

                // Check if certificate is expired
                if (DateTime.Now > certificate.NotAfter)
                {
                    Debug.WriteLine($"[CertificateService] Certificate expired on: {certificate.NotAfter}");
                    Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - InstalledButExpired");
                    return CertificateStatus.InstalledButExpired;
                }

                Debug.WriteLine($"[CertificateService] Certificate valid until: {certificate.NotAfter}");

                // Check if certificate is installed in system store
                using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    try
                    {
                        store.Open(OpenFlags.ReadOnly);
                        var installedCerts = store.Certificates.Find(
                            X509FindType.FindByThumbprint,
                            certificate.Thumbprint,
                            validOnly: false
                        );

                        if (installedCerts.Count > 0)
                        {
                            Debug.WriteLine("[CertificateService] Certificate found in Trusted Root CA store");
                            Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - InstalledAndValid");
                            return CertificateStatus.InstalledAndValid;
                        }

                        Debug.WriteLine("[CertificateService] Certificate not found in Trusted Root CA store");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CertificateService] Error checking certificate store: {ex.Message}");
                    }
                }

                Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - GeneratedNotInstalled");
                return CertificateStatus.GeneratedNotInstalled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CertificateService] Unexpected error: {ex.Message}");
                Debug.WriteLine($"[CertificateService] Stack trace: {ex.StackTrace}");
                Debug.WriteLine("[CertificateService] Exit: GetCertificateStatus - Error");
                return CertificateStatus.Error;
            }
        }

        public bool IsRunningAsAdmin()
        {
            Debug.WriteLine("[CertificateService] Entry: IsRunningAsAdmin");

            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    if (identity == null)
                    {
                        Debug.WriteLine("[CertificateService] WindowsIdentity is null");
                        Debug.WriteLine("[CertificateService] Exit: IsRunningAsAdmin - false");
                        return false;
                    }

                    var principal = new WindowsPrincipal(identity);
                    bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    Debug.WriteLine($"[CertificateService] Is administrator: {isAdmin}");
                    Debug.WriteLine("[CertificateService] Exit: IsRunningAsAdmin");
                    return isAdmin;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CertificateService] Error checking admin status: {ex.Message}");
                Debug.WriteLine("[CertificateService] Exit: IsRunningAsAdmin - false");
                return false;
            }
        }

        public string GetCertificatePath()
        {
            Debug.WriteLine("[CertificateService] Entry: GetCertificatePath");
            Debug.WriteLine($"[CertificateService] Certificate path: {_certPath}");
            Debug.WriteLine("[CertificateService] Exit: GetCertificatePath");
            return _certPath;
        }

        private X509Certificate2 GenerateSelfSignedCertificate(string[] subjectAlternativeNames)
        {
            Debug.WriteLine("[CertificateService] Entry: GenerateSelfSignedCertificate");

            if (subjectAlternativeNames == null || subjectAlternativeNames.Length == 0)
            {
                Debug.WriteLine("[CertificateService] No SAN provided, using defaults");
                subjectAlternativeNames = new[] { "localhost", "127.0.0.1" };
            }

            Debug.WriteLine($"[CertificateService] SAN entries: {string.Join(", ", subjectAlternativeNames)}");

            using (var rsa = RSA.Create(CERT_KEY_SIZE))
            {
                if (rsa == null)
                {
                    Debug.WriteLine("[CertificateService] Failed to create RSA key");
                    throw new CryptographicException("Failed to create RSA key");
                }

                Debug.WriteLine($"[CertificateService] Created {CERT_KEY_SIZE}-bit RSA key");

                var certRequest = new CertificateRequest(
                    CERT_SUBJECT,
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );

                // Add Subject Alternative Name extension
                var sanBuilder = new SubjectAlternativeNameBuilder();
                foreach (var san in subjectAlternativeNames)
                {
                    if (string.IsNullOrEmpty(san))
                    {
                        continue;
                    }

                    // Check if it's an IP address or DNS name
                    if (System.Net.IPAddress.TryParse(san, out var ipAddress))
                    {
                        Debug.WriteLine($"[CertificateService] Adding IP SAN: {san}");
                        sanBuilder.AddIpAddress(ipAddress);
                    }
                    else
                    {
                        Debug.WriteLine($"[CertificateService] Adding DNS SAN: {san}");
                        sanBuilder.AddDnsName(san);
                    }
                }

                certRequest.CertificateExtensions.Add(sanBuilder.Build());

                // Add basic constraints (not a CA)
                certRequest.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(
                        certificateAuthority: false,
                        hasPathLengthConstraint: false,
                        pathLengthConstraint: 0,
                        critical: true
                    )
                );

                // Add key usage
                certRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                        critical: true
                    )
                );

                // Add enhanced key usage (server authentication)
                certRequest.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection
                        {
                            new Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
                        },
                        critical: true
                    )
                );

                var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
                var notAfter = DateTimeOffset.UtcNow.AddYears(CERT_VALIDITY_YEARS);

                Debug.WriteLine($"[CertificateService] Certificate valid from {notBefore} to {notAfter}");

                var certificate = certRequest.CreateSelfSigned(notBefore, notAfter);

                Debug.WriteLine("[CertificateService] Self-signed certificate created");
                Debug.WriteLine("[CertificateService] Exit: GenerateSelfSignedCertificate");

                return certificate;
            }
        }

        private void InstallCertificateToStore(X509Certificate2 certificate)
        {
            Debug.WriteLine("[CertificateService] Entry: InstallCertificateToStore");

            if (certificate == null)
            {
                Debug.WriteLine("[CertificateService] Certificate is null");
                throw new ArgumentNullException(nameof(certificate));
            }

            Debug.WriteLine($"[CertificateService] Installing certificate with thumbprint: {certificate.Thumbprint}");

            using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                Debug.WriteLine("[CertificateService] Opening LocalMachine\\Root certificate store");
                store.Open(OpenFlags.ReadWrite);

                // Check if certificate already exists
                var existingCerts = store.Certificates.Find(
                    X509FindType.FindByThumbprint,
                    certificate.Thumbprint,
                    validOnly: false
                );

                if (existingCerts.Count > 0)
                {
                    Debug.WriteLine("[CertificateService] Certificate already exists in store");
                    Debug.WriteLine("[CertificateService] Exit: InstallCertificateToStore");
                    return;
                }

                Debug.WriteLine("[CertificateService] Adding certificate to store");
                store.Add(certificate);
                Debug.WriteLine("[CertificateService] Certificate added successfully");
                Debug.WriteLine("[CertificateService] Exit: InstallCertificateToStore");
            }
        }

        public bool RequestElevatedInstallation(string[] localIpAddresses)
        {
            Debug.WriteLine("[CertificateService] Entry: RequestElevatedInstallation");

            if (localIpAddresses == null)
            {
                Debug.WriteLine("[CertificateService] localIpAddresses is null");
                Debug.WriteLine("[CertificateService] Exit: RequestElevatedInstallation - false");
                return false;
            }

            try
            {
                var currentProcess = Process.GetCurrentProcess();
                if (currentProcess == null || currentProcess.MainModule == null || string.IsNullOrEmpty(currentProcess.MainModule.FileName))
                {
                    Debug.WriteLine("[CertificateService] Cannot get current process path");
                    Debug.WriteLine("[CertificateService] Exit: RequestElevatedInstallation - false");
                    return false;
                }

                var exePath = currentProcess.MainModule.FileName;
                Debug.WriteLine($"[CertificateService] Current process path: {exePath}");

                // Build arguments: --install-certificate with IP addresses
                var args = $"--install-certificate {string.Join(" ", localIpAddresses)}";
                Debug.WriteLine($"[CertificateService] Arguments: {args}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    Verb = "runas", // Request elevation
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Debug.WriteLine("[CertificateService] Starting elevated process");

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Debug.WriteLine("[CertificateService] Failed to start elevated process");
                        Debug.WriteLine("[CertificateService] Exit: RequestElevatedInstallation - false");
                        return false;
                    }

                    Debug.WriteLine("[CertificateService] Elevated process started, waiting for completion");
                    process.WaitForExit();

                    var exitCode = process.ExitCode;
                    Debug.WriteLine($"[CertificateService] Elevated process exited with code: {exitCode}");
                    Debug.WriteLine("[CertificateService] Exit: RequestElevatedInstallation - true");

                    return exitCode == 0;
                }
            }
            catch (Win32Exception ex)
            {
                // User cancelled UAC prompt
                Debug.WriteLine($"[CertificateService] User cancelled elevation or error: {ex.Message}");
                Debug.WriteLine("[CertificateService] Exit: RequestElevatedInstallation - false");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CertificateService] Error requesting elevation: {ex.Message}");
                Debug.WriteLine($"[CertificateService] Stack trace: {ex.StackTrace}");
                Debug.WriteLine("[CertificateService] Exit: RequestElevatedInstallation - false");
                return false;
            }
        }
    }

    public class CertificateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; }
        public string CertificatePath { get; set; } = string.Empty;
    }

    public enum CertificateStatus
    {
        NotGenerated,
        GeneratedNotInstalled,
        InstalledAndValid,
        InstalledButExpired,
        Error
    }
}
