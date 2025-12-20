using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EDSC.Models.Discovery;

namespace EDSC.Services.Discovery
{
    /// <summary>
    /// Service for automatic network discovery between PC and mobile devices
    /// </summary>
    public interface IDiscoveryService
    {
        /// <summary>
        /// PC: Start listening for discovery requests on the specified UDP port
        /// </summary>
        /// <param name="udpPort">The UDP port to listen on (default: 5001)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task StartListeningAsync(int udpPort = 5001, CancellationToken cancellationToken = default);

        /// <summary>
        /// PC: Stop listening for discovery requests
        /// </summary>
        Task StopListeningAsync();

        /// <summary>
        /// Mobile: Discover servers on the network
        /// </summary>
        /// <param name="timeoutSeconds">Timeout in seconds for each discovery attempt (default: 3)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of discovered servers</returns>
        Task<List<DiscoveredServer>> DiscoverServersAsync(int timeoutSeconds = 3, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets whether the service is currently running
        /// </summary>
        bool IsRunning { get; }
    }
}
