using RobotControlSystem.Interfaces;

namespace RobotControlSystem
{
    public static class GlobalServices
    {
        //存储插件单例，让整个程序都能访问
        public static IUserDevice UserDevice { get; set; } = default!;//null
    }
}
