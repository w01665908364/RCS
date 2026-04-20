using RobotControlSystem.Interfaces;

namespace RobotControlSystem
{
    public sealed class DeviceSingleton
    {
        //设备实例的单例封装
        private static readonly DeviceSingleton _instance = new DeviceSingleton();
        public static DeviceSingleton Instance => _instance;

        public IUserDevice? UserDevice { get; private set; }
        public IRobot? Robot { get; private set; }
        public IAgv? Agv { get; private set; }

        private DeviceSingleton() { }

        public void Initialize()
        {
            UserDevice = PluginManager.LoadFirstPlugin<IUserDevice>();
            Robot = PluginManager.LoadFirstPlugin<IRobot>();
            Agv = PluginManager.LoadFirstPlugin<IAgv>();
        }
    }
}