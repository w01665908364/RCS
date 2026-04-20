using System;

namespace RobotControlSystem.Models
{
    public class FireMonitorData
    {
        public DateTime Timestamp { get; set; }
        public string DeviceCode { get; set; }
        public string StatusName { get; set; }
        public string Address { get; set; }
        public string RawJson { get; set; }
        public int StatusCode { get; set; }
        public string MessageType { get; set; }
    }
}
