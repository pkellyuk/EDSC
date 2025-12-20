using EDSC.Models;
using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// Service for loading and saving application configuration
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Load configuration from file
        /// </summary>
        Task<AppConfig?> LoadConfigurationAsync();

        /// <summary>
        /// Save configuration to file
        /// </summary>
        Task SaveConfigurationAsync(AppConfig config);

        /// <summary>
        /// Get the path to the configuration file
        /// </summary>
        string GetConfigurationPath();
    }
}
