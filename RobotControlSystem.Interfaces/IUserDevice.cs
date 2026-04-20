using System;
using System.Threading.Tasks;

namespace RobotControlSystem.Interfaces
{
    /// <summary>
    /// yc
    /// </summary>
    public interface IUserDevice
    {
        string DeviceId { get; }
        bool IsConnected { get; }

        event EventHandler<DeviceEventArgs> StatusChanged;
        //因为用传的事件要返回地址那些东西，DeviceEventArgs也可以写成单独的
        Task<bool> ConnectAsync();
        void Disconnect();
        void SetParameters(string json);
        string GetParameters();
        void StartMonitoring();
        void StopMonitoring();
    }
}