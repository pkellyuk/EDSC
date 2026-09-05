using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// Service for simulating keyboard input
    /// </summary>
    public interface IKeyboardService
    {
        /// <summary>
        /// Simulate a key press (key down + key up)
        /// </summary>
        /// <param name="key">The key to press (e.g., "F1", "A", "Escape")</param>
        Task SendKeyPressAsync(string key);

        /// <summary>
        /// Simulate a key press held for a given time before release (0 = a normal tap)
        /// </summary>
        Task SendKeyPressAsync(string key, int holdMs);

        /// <summary>
        /// Simulate a key down event
        /// </summary>
        Task SendKeyDownAsync(string key);

        /// <summary>
        /// Simulate a key up event
        /// </summary>
        Task SendKeyUpAsync(string key);
    }
}
