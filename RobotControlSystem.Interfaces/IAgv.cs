using System;
using System.Threading.Tasks;

namespace RobotControlSystem.Interfaces
{
    public interface IAgv
    {
        string AgvId { get; }
        bool IsConnected { get; }

        event EventHandler<DeviceEventArgs> StatusChanged; //no
        Task<bool> ConnectAsync();
        void Disconnect();
        void SetParameters(string json);
        string GetParameters();

        Task<bool> LockAsync();
        Task<bool> UnlockAsync();
        Task<bool> SetSoftEmergencyStopAsync(bool enable);
        Task<bool> NavigateToSiteAsync(string siteName);
        Task<bool> ClearOrdersAsync();
        Task<string> GetStatusAsync();
    }
}