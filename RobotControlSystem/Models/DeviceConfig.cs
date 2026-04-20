using System;

namespace RobotControlSystem.Models
{
    public class DeviceConfig
    {
        public int Id { get; set; }
        public string DeviceType { get; set; }      // 设备类型：UserTransmitter, AGV, RoboticArm
        public string Name { get; set; }             // 设备名称
        public string IPAddress { get; set; }        // IP地址
        public int Port { get; set; }                 // 端口号
        public string Protocol { get; set; }          // 协议类型：TCP, HTTP, MQTT
        public bool IsEnabled { get; set; }           // 是否启用
        public DateTime LastChecked { get; set; }     // 最后检查时间
        public string ConnectionStatus { get; set; }  // 连接状态：在线/离线/未知
    }
}