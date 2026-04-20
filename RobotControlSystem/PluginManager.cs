using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RobotControlSystem
{
    public static class PluginManager
    {
        public static T LoadFirstPlugin<T>(string pluginsDirectory = "Plugins") where T : class
        {
            //负责动态加载 Plugins 文件夹里的 dll,反射
            string dirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, pluginsDirectory);
            if (!Directory.Exists(dirPath))
            {
                return null;
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    var type = assembly.GetTypes().FirstOrDefault(t => typeof(T).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    if (type != null)
                    {
                        return (T)Activator.CreateInstance(type);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load plugin from {file}: {ex.Message}");
                }
            }
            return null;
        }
    }
}
