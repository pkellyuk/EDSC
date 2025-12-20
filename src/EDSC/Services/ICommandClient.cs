using EDSC.Models;
using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// HTTP client for sending commands to PC server
    /// </summary>
    public interface ICommandClient
    {
        /// <summary>
        /// Send a command to the server
        /// </summary>
        Task<CommandResponse> SendCommandAsync(string buttonId, string key);

        /// <summary>
        /// Set the server address
        /// </summary>
        void SetServerAddress(string ipAddress, int port);

        /// <summary>
        /// Test connection to server
        /// </summary>
        Task<bool> TestConnectionAsync();
    }
}
