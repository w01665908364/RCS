namespace RobotControlSystem.Interfaces
{
    public enum DeviceStatus
    {
        Offline,        // 离线
        Online,         // 在线
        Error,          // 错误
        Busy,           // 繁忙
        FireAlarm,      // 火警（用户传输装置特有）
        Custom          // 自定义（附加信息见 Message）
    }
}