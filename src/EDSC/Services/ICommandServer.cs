using System.Threading;
using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// HTTP server for receiving commands from mobile clients
    /// </summary>
    public interface ICommandServer
    {
        /// <summary>
        /// Start the command server on the specified port
        /// </summary>
        Task StartAsync(int port, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop the command server
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Gets whether the server is currently running
        /// </summary>
        bool IsRunning { get; }
    }
}
