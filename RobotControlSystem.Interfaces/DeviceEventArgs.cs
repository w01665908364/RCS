using System;

namespace RobotControlSystem.Interfaces
{
    public class DeviceEventArgs : EventArgs
    {
        public DeviceStatus Status { get; set; }
        public string Message { get; set; }       // 附加信息
        //可以再加一个address专门返回地址
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}