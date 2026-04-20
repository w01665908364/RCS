using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RobotControlSystem.Services
{
    /// <summary>
    /// 艾利特机械臂控制服务
    /// 基于Dashboard协议（29999端口）
    /// </summary>
    public class ElibotRobotService : IDisposable
    {
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private readonly object _lockObject = new object();
        private readonly ILogService _logService;

        // 配置参数
        public string RobotIP { get; set; } = "192.168.1.60";
        public int RobotPort { get; set; } = 29999;
        public int TimeoutMilliseconds { get; set; } = 5000;

        public bool IsConnected => _tcpClient?.Connected == true;

        public ElibotRobotService(ILogService logService = null)
        {
            _logService = logService;
        }

        /// <summary>
        /// 连接到机械臂
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                Log($"正在连接机械臂: {RobotIP}:{RobotPort}");

                _tcpClient = new TcpClient();
                _tcpClient.SendTimeout = TimeoutMilliseconds;
                _tcpClient.ReceiveTimeout = TimeoutMilliseconds;

                // 异步连接
                var connectTask = _tcpClient.ConnectAsync(RobotIP, RobotPort);
                var timeoutTask = Task.Delay(TimeoutMilliseconds);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Log("连接超时，请检查网络和机器人IP");
                    _tcpClient.Close();
                    return false;
                }

                if (!_tcpClient.Connected)
                {
                    Log("连接失败");
                    return false;
                }

                _networkStream = _tcpClient.GetStream();
                Log("✅ 机械臂连接成功");

                // 清空接收缓冲区
                //ClearReceiveBuffer();

                return true;
            }
            catch (SocketException ex)
            {
                Log($"❌ 网络连接错误: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ 连接异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 机器人上电（单次尝试，无重试）
        /// </summary>
        public async Task<bool> PowerOnAsync()
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            try
            {
                Log("发送上电指令...");
                var response = await SendCommandAsync("robotControl -on");

                if (response.Contains("Powering on"))
                {
                    Log("✅ 机器人上电成功");
                    await Task.Delay(1500); // 等待电源稳定
                    return true;
                }

                Log($"上电返回: {response}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"上电异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 释放抱闸（单次尝试，无重试）
        /// </summary>
        public async Task<bool> ReleaseBrakeAsync()
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            try
            {
                Log("发送释放抱闸指令...");
                var response = await SendCommandAsync("brakeRelease");

                if (response.Contains("Brake is released"))
                {
                    Log("✅ 抱闸释放成功");
                    await Task.Delay(1000); // 等待抱闸完全释放
                    return true;
                }

                Log($"释放抱闸返回: {response}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"释放抱闸异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 运行指定名称的任务（单次尝试，无重试）
        /// </summary>
        public async Task<bool> RunTaskAsync(string taskName)
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            try
            {
                Log($"发送运行任务指令...");
                var responseTask = await SendCommandAsync($"task -p {taskName}");
                var responsePlay = await SendCommandAsync("play");
                await Task.Delay(2000); // 等待任务开始执行
                Log("等待了2s");
                while (true)
                {
                    var response1 = await SendCommandAsync("task -r");
                    if (response1.Trim().EndsWith("Task is not running.")) { break; }
                    Log("✅ 任务启动成功");
                    return true;
                }
                //if (responsePlay.Trim().EndsWith("Starting task") || responsePlay.Trim().EndsWith("OK"))
                //{
                //    Log("✅ 任务启动成功");
                //    return true;
                //}

                Log($"运行任务返回: {responseTask}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"运行任务异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 运行默认任务（兼容原有代码）
        /// </summary>
        public async Task<bool> RunTaskAsync()
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            return await RunTaskAsync("动作3.task");
        }

        /// <summary>
        /// 查询任务状态
        /// </summary>
        public async Task<string> QueryTaskStatusAsync()
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            try
            {
                var response = await SendCommandAsync("task -r");
                Log($"任务状态: {response}");
                return response;
            }
            catch (Exception ex)
            {
                Log($"❌ 查询状态异常: {ex.Message}");
                return $"错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 发送机械臂运动指令
        /// </summary>
        public async Task<bool> MoveToPositionAsync(double x, double y, double z, double rx, double ry, double rz)
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            try
            {
                // 根据艾利特指令格式生成命令
                string command = $"MOVL({x},{y},{z},{rx},{ry},{rz})";
                Log($"发送运动指令: {command}");

                // 注意：运动控制通常使用另一个端口（如8055），这里仅示例
                var response = await SendCommandAsync(command);

                if (response.Contains("OK") || response.Contains("Success"))
                {
                    Log("✅ 运动指令执行成功");
                    return true;
                }
                else
                {
                    Log($"❌ 运动指令失败: {response}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 运动指令异常: {ex.Message}");
                return false;
            }
        }

        // ==================== 集成的新功能方法 ====================
        /// <summary>
        /// 组合操作：连接到指定机器人 -> 运行任务 -> 查询任务状态
        /// 此方法实现了“接收任务信息后自动运行”的流程。
        /// </summary>
        /// <param name="taskName">任务文件名（如 "动作3.task"）</param>
        /// <param name="ip">机器人IP，若不指定则使用当前 RobotIP 属性</param>
        /// <param name="port">端口号，默认29999</param>
        /// <returns>(任务是否成功启动, 状态查询结果)</returns>
        public async Task<(bool taskSuccess, string statusResult)> RunTaskAndGetStatusAsync(string taskName, string ip = null, int port = 29999)
        {
            // 等待当前任务结束
            while (true)
            {
                var response1 = await SendCommandAsync("task -r");
                if (response1.Trim().EndsWith("Task is not running.")) { break; }
            }

            // 保存原始IP以便恢复
            string originalIP = RobotIP;
            int originalPort = RobotPort;

            try
            {
                // 如果传入了IP，临时覆盖
                if (!string.IsNullOrEmpty(ip))
                {
                    RobotIP = ip;
                    RobotPort = port;
                }

                // 1. 连接机器人
                bool connected = await ConnectAsync();
                if (!connected)
                {
                    Log("❌ 无法连接到机器人，操作取消");
                    return (false, "连接失败");
                }

                // 2. 运行任务
                bool taskStarted = await RunTaskAsync(taskName);
                if (!taskStarted)
                {
                    Log("❌ 任务启动失败");
                    await Task.Delay(500); // 等待可能的错误信息
                    string errorStatus = await QueryTaskStatusAsync();
                    return (false, errorStatus);
                }

                // 3. 查询状态（等待一小段时间让任务真正运行）
                await Task.Delay(1000);
                string status = await QueryTaskStatusAsync();

                return (true, status);
            }
            catch (Exception ex)
            {
                Log($"❌ 执行任务序列异常: {ex.Message}");
                return (false, $"异常: {ex.Message}");
            }
            finally
            {
                // 恢复原始配置并断开连接
                RobotIP = originalIP;
                RobotPort = originalPort;
                Disconnect();
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_tcpClient?.Connected == true)
                {
                    _networkStream?.Close();
                    _tcpClient.Close();
                    Log("机械臂连接已断开");
                }
            }
            catch (Exception ex)
            {
                Log($"断开连接异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送命令到机械臂
        /// </summary>
        private async Task<string> SendCommandAsync(string command)
        {
            lock (_lockObject)
            {
                if (_tcpClient == null || !_tcpClient.Connected)
                {
                    throw new InvalidOperationException("未连接到机械臂");
                }

                try
                {
                    // 发送命令（必须加换行符）
                    string commandWithNewline = command + '\n';
                    byte[] sendData = Encoding.UTF8.GetBytes(commandWithNewline);
                    _networkStream.Write(sendData, 0, sendData.Length);//发送命令
                    Task.Delay(2000);
                    // 接收响应
                    byte[] buffer = new byte[4096];
                    int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    return response.TrimEnd('\r', '\n');
                }
                catch (Exception ex)
                {
                    Log($"发送命令失败: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 清空接收缓冲区
        /// </summary>
        private void ClearReceiveBuffer()
        {
            try
            {
                if (_tcpClient?.Available > 0)
                {
                    byte[] buffer = new byte[_tcpClient.Available];
                    _networkStream.Read(buffer, 0, buffer.Length);
                    Log($"清空缓冲区: {buffer.Length} 字节");
                }
            }
            catch
            {
                // 忽略清空缓冲区的错误
            }
        }

        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logMessage = $"[{timestamp}] {message}";

            // 输出到控制台
            Console.WriteLine(logMessage);

            // 如果配置了日志服务，也输出到日志
            _logService?.Log(logMessage);
        }

        public void Dispose()
        {
            Disconnect();
            _networkStream?.Dispose();
            _tcpClient?.Dispose();
        }
    }

    // 简单的日志服务接口
    public interface ILogService
    {
        void Log(string message);
    }
}