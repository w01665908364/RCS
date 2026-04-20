using System;
using System.Threading.Tasks;

namespace RobotControlSystem.Interfaces
{
    public interface IRobot
    {
        string RobotId { get; }
        bool IsConnected { get; }

        event EventHandler<DeviceEventArgs> StatusChanged;//no，可以不要

        Task<bool> ConnectAsync();
        void Disconnect();
        void SetParameters(string json);
        string GetParameters();

        Task<bool> PowerOnAsync();
        Task<bool> ReleaseBrakeAsync();
        Task<bool> RunTaskAsync(string taskName);
        Task<string> QueryStatusAsync();
        Task<bool> MoveToAsync(double x, double y, double z, double rx, double ry, double rz);
    }
}