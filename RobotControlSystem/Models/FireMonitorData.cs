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
        // 追加解析字段
        public int LoopNo { get; set; }
        public int NodeNo { get; set; }
        // 状态发生时间（BCD解析后的人类可读字符串）
        public string EventTime { get; set; }
    }
}
