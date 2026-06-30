using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RobotControlSystem.Interfaces;

namespace Plugin.UserDevice
{
    /// <summary>
    /// 用户传输装置插件 - 基于 GB/T 26875.3-2011 标准
    /// </summary>
    public class UserDevicePlugin : IUserDevice
    {
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private bool _isMonitoring;
        private int _listenPort = 7799;
        private string _deviceId = "UserDevice-Default";
        private bool _isConnected;

        public string DeviceId => _deviceId;
        public bool IsConnected => _isConnected;

        public event EventHandler<DeviceEventArgs>? StatusChanged;

        /// <summary>
        /// 设置参数（JSON格式）
        /// 示例：{ "listenPort": 20000, "deviceId": "GST200" }
        /// </summary>
        public void SetParameters(string json)
        {
            try
            {
                var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (obj != null)
                {
                    if (obj.TryGetValue("listenPort", out object? portObj))
                        _listenPort = Convert.ToInt32(portObj);
                    if (obj.TryGetValue("deviceId", out object? idObj))
                        _deviceId = idObj.ToString() ?? _deviceId;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"参数解析失败: {ex.Message}"
                });
            }
        }

        public string GetParameters()
        {
            return $"{{\"listenPort\":{_listenPort},\"deviceId\":\"{_deviceId}\"}}";
        }

        /// <summary>
        /// 启动监听服务（连接）
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_tcpListener != null)
                    Disconnect();

                _tcpListener = new TcpListener(IPAddress.Any, _listenPort);
                _tcpListener.Start();
                _isConnected = true;
                _cts = new CancellationTokenSource();

                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Online,
                    Message = $"监听服务已启动，端口: {_listenPort}"
                });

                // 启动接受连接的循环
                _ = Task.Run(() => AcceptClientsAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"启动监听失败: {ex.Message}"
                });
                return false;
            }
        }

        public void Disconnect()
        {
            _isMonitoring = false;
            _cts?.Cancel();
            _tcpListener?.Stop();
            _tcpListener = null;
            _isConnected = false;
            StatusChanged?.Invoke(this, new DeviceEventArgs
            {
                Status = DeviceStatus.Offline,
                Message = "监听服务已停止"
            });
        }

        public void StartMonitoring()
        {
            _isMonitoring = true;
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, new DeviceEventArgs
                    {
                        Status = DeviceStatus.Error,
                        Message = $"接受连接异常: {ex.Message}"
                    });
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                var stream = client.GetStream();
                var readBuffer = new byte[4096];
                var buffer = new List<byte>();

                while (!token.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token);
                        if (bytesRead <= 0)
                            break;

                        // 记录收到的原始数据
                        try
                        {
                            var receivedBytes = new byte[bytesRead];
                            Array.Copy(readBuffer, receivedBytes, bytesRead);
                            System.Diagnostics.Debug.WriteLine($"[原始接收] {BitConverter.ToString(receivedBytes).Replace("-", "")} 字节数={bytesRead}");
                        }
                        catch { }

                        // Append received bytes to buffer
                        for (int i = 0; i < bytesRead; i++) buffer.Add(readBuffer[i]);

                        // Try to parse one or more packets from buffer
                        while (true)
                        {
                            if (buffer.Count < 30) break; // minimal packet size

                            // Find header 0x40 0x40
                            int start = -1;
                            for (int i = 0; i < buffer.Count - 1; i++)
                            {
                                if (buffer[i] == 0x40 && buffer[i + 1] == 0x40)
                                {
                                    start = i;
                                    break;
                                }
                            }

                            if (start == -1)
                            {
                                buffer.Clear();
                                break;
                            }

                            if (start > 0) buffer.RemoveRange(0, start);

                            if (buffer.Count < 30) break;

                            // 应用数据单元长度在索引 24-25（小端字节序）
                            // GB/T 26875.3标准：低字节在前，高字节在后
                            int appLen = (buffer[25] << 8) | buffer[24];
                            int totalLen = 30 + appLen; // 整包长度 = 30(固定头尾+crc) + 应用长度

                            if (buffer.Count < totalLen) break; // 等待完整包

                            // 提取包
                            var packet = buffer.GetRange(0, totalLen).ToArray();

                            // 记录解析的包信息
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[解析包] 长度={totalLen} 应用数据长度={appLen} 命令=0x{packet[26]:X2}");
                            }
                            catch { }

                            // 验证结束符 0x23 0x23
                            if (packet[totalLen - 2] != 0x23 || packet[totalLen - 1] != 0x23)
                            {
                                // 丢弃首字节并继续寻找下一个头
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[结束符错误] 期望2323, 实际={packet[totalLen - 2]:X2}{packet[totalLen - 1]:X2}");
                                }
                                catch { }
                                buffer.RemoveAt(0);
                                continue;
                            }

                            // 校验和位于 totalLen-3
                            int checksumIdx = totalLen - 3;
                            byte expected = packet[checksumIdx];
                            byte calc = CalculateChecksum(packet, 2, checksumIdx - 2);
                            if (expected != calc)
                            {
                                // 校验失败，记录期望值和计算值，丢弃首字节继续
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[校验失败] index={checksumIdx} expected=0x{expected:X2} calc=0x{calc:X2} 包预览={BitConverter.ToString(packet, 0, Math.Min(30, packet.Length))}");
                                }
                                catch { }
                                buffer.RemoveAt(0);
                                continue;
                            }

                            // 注意：对于业务包，不用packet[26]（命令字节），而是用appData[0]（协议类型）来分发
                            // packet[26]只在心跳包(totalLen==30)时有意义

                            if (totalLen == 30)
                            {
                                // 心跳包，回复心跳
                                var resp = BuildHeartbeatResponse(packet);
                                try { await stream.WriteAsync(resp, 0, resp.Length, token); await stream.FlushAsync(token); } catch { }
                                if (_isMonitoring)
                                {
                                    StatusChanged?.Invoke(this, new DeviceEventArgs { Status = DeviceStatus.Online, Message = "收到心跳并已回复" });
                                }
                            }
                            else if (totalLen > 30)
                            {
                                // 提取应用数据
                                int appDataLen = appLen;
                                byte[] appData = new byte[appDataLen];
                                if (appDataLen > 0) Array.Copy(packet, 27, appData, 0, appDataLen);

                                // 设备编码从源地址(12-17)构造
                                var src = new byte[6]; Array.Copy(packet, 12, src, 0, 6);
                                string deviceCode = BitConverter.ToString(src).Replace("-", "");

                                // ⭐ 关键修正：根据应用数据单元的协议类型分发（与JAVA完全一致）
                                // JAVA: if (data.startsWith("01")) → 协议类型在第1个字节
                                // 不要用packet[26]（命令字节），应该用appData[0]（协议类型）
                                byte protocolType = appData.Length > 0 ? appData[0] : (byte)0x00;

                                // 记录业务数据包信息
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[业务数据] 协议类型=0x{protocolType:X2} 设备={deviceCode} 应用数据={BitConverter.ToString(appData).Replace("-", "")}");
                                }
                                catch { }

                                // 回复确认包
                                var resp = BuildHeartbeatResponse(packet);
                                try { await stream.WriteAsync(resp, 0, resp.Length, token); await stream.FlushAsync(token); } catch { }

                                switch (protocolType)
                                {
                                    case 0x01: // 消防主机运行状态
                                        ParseFireMainStatus(appData, deviceCode);
                                        break;
                                    case 0x02: // 建筑消防设施部件运行状态
                                        ParsePartStatus(appData, deviceCode);
                                        break;
                                    case 0x03: // 模拟量
                                        ParseAnalogQuantity(appData, deviceCode);
                                        break;
                                    case 0x04: // 消防主机操作信息
                                        ParseFireMainOperation(appData, deviceCode);
                                        break;
                                    case 0x15: // 用户传输装置运行状态 ⭐关键
                                        ParseUserDeviceStatus(appData, deviceCode);
                                        break;
                                    case 0x18: // 用户传输装置操作信息
                                        ParseUserDeviceOperation(appData, deviceCode);
                                        break;
                                    case 0x1C: // 心跳包（业务包形式）
                                        // 心跳包通常作为独立包（totalLen=30），但如果有业务形式也处理
                                        if (_isMonitoring)
                                        {
                                            StatusChanged?.Invoke(this, new DeviceEventArgs
                                            {
                                                Status = DeviceStatus.Online,
                                                Message = "收到心跳包（业务形式）"
                                            });
                                        }
                                        break;
                                    default:
                                        if (_isMonitoring)
                                        {
                                            StatusChanged?.Invoke(this, new DeviceEventArgs
                                            {
                                                Status = DeviceStatus.Online,
                                                Message = $"收到未知协议类型:0x{protocolType:X2}"
                                            });
                                        }
                                        break;
                                }
                            }

                            // 移除已处理的字节
                            buffer.RemoveRange(0, totalLen);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke(this, new DeviceEventArgs
                        {
                            Status = DeviceStatus.Error,
                            Message = $"接收数据异常: {ex.Message}"
                        });
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 构建心跳回复包（与Java代码完全一致）
        /// 关键修正：使用请求包的时间，而不是当前时间
        /// </summary>
        private byte[] BuildHeartbeatResponse(byte[] request)
        {
            try
            {
                var resp = new List<byte>();

                // 1. 起始符
                resp.Add(0x40);
                resp.Add(0x40);

                // 2. 流水号 - 从请求复制 (索引2-3)
                resp.Add(request[2]);
                resp.Add(request[3]);

                // 3. 版本号 - 从请求复制 (索引4-5)
                resp.Add(request[4]);
                resp.Add(request[5]);

                // 4. 时间 - 从请求复制 (索引6-11) ⭐ 关键修正
                // JAVA代码：resp的时间字段应该与request完全一致
                for (int i = 6; i <= 11; i++) resp.Add(request[i]);

                // 5. 目的地址 - 从请求的目的地址复制 (索引18-23)
                for (int i = 18; i <= 23; i++) resp.Add(request[i]);

                // 6. 源地址 - 从请求的源地址复制 (索引12-17)
                for (int i = 12; i <= 17; i++) resp.Add(request[i]);

                // 7. 数据长度 (0000)
                resp.Add(0x00);
                resp.Add(0x00);

                // 8. 命令字节 (03)
                resp.Add(0x03);

                // 9. 计算校验和（按Java代码逻辑）
                int sum = 0;
                // 流水号
                sum += request[2] + request[3];
                // 版本号
                sum += request[4] + request[5];
                // 时间 (从请求复制)
                for (int i = 6; i <= 11; i++) sum += request[i];
                // 请求源地址
                for (int i = 12; i <= 17; i++) sum += request[i];
                // 请求目的地址
                for (int i = 18; i <= 23; i++) sum += request[i];
                // 长度和命令
                sum += 0 + 0 + 3;

                resp.Add((byte)(sum & 0xFF));

                // 调试日志
                try
                {
                    string reqSrc = string.Format("{0:X2} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2}", request[12], request[13], request[14], request[15], request[16], request[17]);
                    string reqDest = string.Format("{0:X2} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2}", request[18], request[19], request[20], request[21], request[22], request[23]);

                    System.Diagnostics.Debug.WriteLine($"[收到请求] {BitConverter.ToString(request).Replace("-", "")} ");
                    System.Diagnostics.Debug.WriteLine($"[发送回复] {BitConverter.ToString(resp.ToArray()).Replace("-", "")} 长度={resp.Count}");
                }
                catch { }

                // 10. 结束符
                resp.Add(0x23);
                resp.Add(0x23);

                return resp.ToArray();
            }
            catch { return Array.Empty<byte>(); }
        }

        /// <summary>
        /// 计算校验和，从 start 开始，长度 length 个字节，取低8位
        /// </summary>
        private byte CalculateChecksum(byte[] data, int start, int length)
        {
            int sum = 0;
            for (int i = start; i < start + length; i++) sum += data[i];
            return (byte)(sum & 0xFF);
        }

        /// <summary>
        /// 解析火警应用数据单元，每条信息体 92 字节
        /// </summary>
        private void ParseFireAlarm(byte[] appData, string deviceCode)
        {
            try
            {
                if (appData == null || appData.Length == 0) return;
                int unitSize = 92;
                int count = appData.Length / unitSize;
                for (int i = 0; i < count; i++)
                {
                    int idx = i * unitSize;
                    if (idx + unitSize > appData.Length) break;
                    var unit = new byte[unitSize]; Array.Copy(appData, idx, unit, 0, unitSize);

                    // 状态字段位于单元第14-18字节范围，我们取第15字节(索引14)作为状态字节
                    byte status = unit.Length > 14 ? unit[14] : (byte)0x00;
                    bool isFire = (status & 0x40) != 0; // bit6
                    bool isFault = (status & 0x20) != 0; // bit5

                    // 部件说明：尝试从单元中提取可打印字符串(例如索引20长度20)
                    string partDesc = ExtractPrintableString(unit, 20, 20);
                    // 部件类型：索引12-13
                    string partType = unit.Length > 13 ? $"0x{unit[12]:X2}{unit[13]:X2}" : "N/A";
                    // 时间：单元6-11字节
                    string time = "";
                    if (unit.Length >= 12)
                    {
                        int sec = unit[6]; int min = unit[7]; int hour = unit[8]; int day = unit[9]; int month = unit[10]; int year = unit[11];
                        time = $"20{year:D2}-{month:D2}-{day:D2} {hour:D2}:{min:D2}:{sec:D2}";
                    }

                    if (isFire || isFault)
                    {
                        string level = isFire ? "火警" : "故障";
                        string msg = $"{level}-{deviceCode}-{partDesc}-{partType}-{time}";
                        StatusChanged?.Invoke(this, new DeviceEventArgs { Status = DeviceStatus.FireAlarm, Message = msg });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs { Status = DeviceStatus.Error, Message = $"解析火警数据异常: {ex.Message}" });
            }
        }

        /// <summary>
        /// 解析用户传输装置运行状态 (0x15) - 与JAVA的UserStatus对应
        /// 输出: 手动报警、正常、备电故障、主电故障、故障、火警、测试状态
        /// </summary>
        private void ParseUserDeviceStatus(byte[] appData, string deviceCode)
        {
            try
            {
                // 应用数据格式: 类型(2字节) + 信息数量(2字节) + 状态字节(2字节十六进制)
                // JAVA: data.substring(4,6) 取状态字节
                if (appData.Length < 6) return;

                // 状态字节位于索引2（即第3个字节，对应substring(4,6)）
                byte statusByte = appData[2];

                // 转成8位二进制字符串
                string bit = Convert.ToString(statusByte, 2).PadLeft(8, '0');

                // 按位判断状态类型（与JAVA的UserStatus完全一致）
                string statusName;
                if (bit[1] == '1')
                    statusName = "监测连接线路故障";
                else if (bit[2] == '1')
                    statusName = "手动报警";  // ⭐这是JAVA输出的"手动报警"
                else if (bit[3] == '1')
                    statusName = "备电故障";
                else if (bit[4] == '1')
                    statusName = "主电故障";
                else if (bit[5] == '1')
                    statusName = "故障";
                else if (bit[6] == '1')
                    statusName = "火警";
                else if (bit[7] == '1')
                    statusName = "测试状态";
                else
                    statusName = "正常";

                // 输出JSON格式（与JAVA一致）
                var result = new
                {
                    statusName = statusName,
                    uploadTime = DateTime.Now.Ticks / 10000, // 毫秒时间戳
                    userCode = deviceCode,
                    status = 2
                };

                string json = JsonConvert.SerializeObject(result);
                Console.WriteLine($"[0x15 用户装置状态] {json}");
                System.Diagnostics.Debug.WriteLine(json);

                // 触发事件
                DeviceStatus eventStatus = DeviceStatus.Online;
                if (statusName == "火警") eventStatus = DeviceStatus.FireAlarm;
                else if (statusName.Contains("故障")) eventStatus = DeviceStatus.Error;

                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = eventStatus,
                    Message = json
                });
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析用户装置状态异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 解析建筑消防设施部件运行状态 (0x02) - 按GB/T 26875.3-2011国标修正
        /// 国标定义：类型标志(1字节) + 信息对象数目(1字节) + 信息对象(N×40字节)
        /// </summary>
        private void ParsePartStatus(byte[] appData, string deviceCode)
        {
            try
            {
                // 【国标修正】应用数据格式：类型(1字节) + 数目(1字节) + 信息体
                if (appData.Length < 2) return;

                // 【国标修正】数目为1字节
                byte messageNumber = appData[1];

                // 如果数目为0，可能设备使用了2字节数目，尝试兼容
                if (messageNumber == 0 && appData.Length >= 4)
                {
                    // 尝试2字节数目（兼容模式）
                    int altNumber = (appData[2] << 8) | appData[3];
                    if (altNumber > 0 && (4 + altNumber * 40) <= appData.Length)
                    {
                        ParsePartStatusLegacy(appData, deviceCode);
                        return;
                    }
                }

                if (messageNumber <= 0) return;

                // 【国标修正】每条信息体40字节（GB/T 26875.3-2011 图7）
                const int unitSize = 40;

                // 【国标修正】信息体从索引2开始（类型1字节 + 数目1字节 = 2字节头部）
                for (int i = 0; i < messageNumber && (2 + (i + 1) * unitSize) <= appData.Length; i++)
                {
                    int idx = 2 + i * unitSize;
                    byte[] unit = new byte[unitSize];
                    Array.Copy(appData, idx, unit, 0, unitSize);

                    // 【国标修正】按40字节结构解析
                    // 字段偏移（基于unit数组）：
                    // [0]系统类型(1B) [1]系统地址(1B) [2]部件类型(1B) [3-6]部件地址(4B) [7-8]部件状态(2B) [9-39]部件说明(31B)

                    int systemType = unit[0];           // 系统类型 - 1字节
                    int systemAddr = unit[1];           // 系统地址 - 1字节（消防主机编码）
                    int partType = unit[2];             // 部件类型 - 1字节

                    // 部件地址 - 4字节，格式：节点号低 + 节点号高 + 回路号低 + 回路号高
                    int nodeNo = unit[3] | (unit[4] << 8);   // 节点号
                    int loopNo = unit[5] | (unit[6] << 8);   // 回路号
                    string addressNo = nodeNo.ToString();
                    string loopNoStr = loopNo.ToString();

                    // 部件状态 - 2字节，低字节在前
                    int partStatus = unit[7] | (unit[8] << 8);

                    // 状态位解析（16位二进制）
                    // bitStat[0]=bit15, bitStat[14]=bit1, bitStat[15]=bit0
                    string bitStat = Convert.ToString(partStatus, 2).PadLeft(16, '0');

                    // 解析状态（按国标位定义，字符串索引=15-bit号）
                    string partStat = "正常";
                    int status = 2; // 0故障 1火警 2正常

                    if (bitStat[14] == '1') { partStat = "火警"; status = 1; }           // bit1
                    else if (bitStat[13] == '1') { partStat = "故障"; status = 0; }      // bit2
                    else if (bitStat[12] == '1') { partStat = "屏蔽"; }                  // bit3
                    else if (bitStat[11] == '1') { partStat = "监管"; }                  // bit4
                    else if (bitStat[10] == '1') { partStat = "启动"; }                  // bit5
                    else if (bitStat[9] == '1') { partStat = "反馈"; }                   // bit6
                    else if (bitStat[8] == '1') { partStat = "延时"; }                   // bit7
                    else if (bitStat[7] == '1') { partStat = "电源故障"; status = 0; }   // bit8
                    else if (bitStat[15] == '1') { partStat = "正常运行"; }              // bit0
                    else if (bitStat[0] == '0') { partStat = "测试运行"; }               // bit15=0

                    // 部件说明 - 31字节，GB18030编码
                    string partExplain = "";
                    try
                    {
                        byte[] descBytes = new byte[31];
                        Array.Copy(unit, 9, descBytes, 0, 31);
                        partExplain = Encoding.GetEncoding("GB18030").GetString(descBytes)
                            .TrimEnd('\0', '\xa5').Replace("\xa5\xa5", "").Trim();
                    }
                    catch { }

                    // 设备编号
                    string deviceCodeFull = deviceCode + systemAddr + "-" + loopNoStr + "-" + addressNo;

                    // 当前时间作为上传时间
                    string upTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    // 输出JSON
                    var result = new
                    {
                        logId = Guid.NewGuid().ToString(),
                        deviceCode = deviceCodeFull,
                        address = partExplain,
                        systemType = systemType,
                        fireMainCode = systemAddr,
                        partType = partType,
                        loopNo = loopNoStr,
                        addressNo = addressNo,
                        statusName = partStat,
                        partTypeExplain = "",
                        userCode = deviceCode,
                        upTime = upTime
                    };

                    string json = JsonConvert.SerializeObject(result);
                    Console.WriteLine($"[0x02 部件状态-国标] {json}");
                    System.Diagnostics.Debug.WriteLine(json);

                    // 触发事件（所有状态都输出，便于调试）
                    DeviceStatus eventStatus = DeviceStatus.Online;
                    if (status == 1) eventStatus = DeviceStatus.FireAlarm;
                    else if (status == 0) eventStatus = DeviceStatus.Error;

                    StatusChanged?.Invoke(this, new DeviceEventArgs
                    {
                        Status = eventStatus,
                        Message = json
                    });
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析部件状态异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 部件状态解析 - 兼容模式（使用2字节数目 + 92字节信息体，保持与旧版JAVA一致）
        /// </summary>
        private void ParsePartStatusLegacy(byte[] appData, string deviceCode)
        {
            try
            {
                if (appData.Length < 4) return;
                int messageNumber = (appData[2] << 8) | appData[3];
                if (messageNumber <= 0) return;
                int unitSize = 92;

                for (int i = 0; i < messageNumber && (4 + (i + 1) * unitSize) <= appData.Length; i++)
                {
                    int idx = 4 + i * unitSize;
                    string message = BitConverter.ToString(appData, idx, unitSize).Replace("-", "");

                    int systemType = Convert.ToInt32(message.Substring(0, 2), 16);
                    int fireMainCode = Convert.ToInt32(message.Substring(2, 4), 16);
                    int partType = Convert.ToInt32(message.Substring(4, 6), 16);
                    string addressNo = Convert.ToInt32(message.Substring(8, 2) + message.Substring(6, 2), 16).ToString();
                    string loopNo = Convert.ToInt32(message.Substring(12, 2) + message.Substring(10, 2), 16).ToString();
                    string bitStat = Convert.ToString(Convert.ToInt32(message.Substring(14, 4), 16), 2).PadLeft(16, '0');

                    string partStat = "正常";
                    int status = 2;

                    // 状态位解析（字符串索引=15-bit号）
                    if (bitStat[14] == '1') { partStat = "火警"; status = 1; }           // bit1
                    else if (bitStat[13] == '1') { partStat = "故障"; status = 0; }      // bit2
                    else if (bitStat[12] == '1') { partStat = "屏蔽"; }                  // bit3
                    else if (bitStat[11] == '1') { partStat = "监管"; }                  // bit4
                    else if (bitStat[10] == '1') { partStat = "启动"; }                  // bit5
                    else if (bitStat[9] == '1') { partStat = "反馈"; }                   // bit6
                    else if (bitStat[8] == '1') { partStat = "延时"; }                   // bit7
                    else if (bitStat[7] == '1') { partStat = "电源故障"; status = 0; }   // bit8
                    else if (bitStat[15] == '1') { partStat = "正常运行"; }              // bit0

                    string partExplain = "";
                    try
                    {
                        byte[] explainBytes = HexStringToByteArray(message.Substring(18, 62));
                        partExplain = Encoding.GetEncoding("GB18030").GetString(explainBytes)
                            .Replace("\0", "").Replace("\xE5\xE5", "").Trim();
                    }
                    catch { }

                    string upTime = "";
                    if (message.Length >= 92)
                    {
                        int sec = Convert.ToInt32(message.Substring(80, 2), 16);
                        int min = Convert.ToInt32(message.Substring(82, 2), 16);
                        int hour = Convert.ToInt32(message.Substring(84, 2), 16);
                        int day = Convert.ToInt32(message.Substring(86, 2), 16);
                        int month = Convert.ToInt32(message.Substring(88, 2), 16);
                        int year = Convert.ToInt32(message.Substring(90, 2), 16);
                        upTime = $"20{year:D2}-{month:D2}-{day:D2} {hour:D2}:{min:D2}:{sec:D2}";
                    }

                    string deviceCodeFull = deviceCode + fireMainCode + "-" + loopNo + "-" + addressNo;

                    var result = new
                    {
                        logId = Guid.NewGuid().ToString(),
                        deviceCode = deviceCodeFull,
                        address = partExplain,
                        systemType = systemType,
                        fireMainCode = fireMainCode,
                        partType = partType,
                        loopNo = loopNo,
                        addressNo = addressNo,
                        statusName = partStat,
                        partTypeExplain = "",
                        userCode = deviceCode,
                        upTime = upTime
                    };

                    string json = JsonConvert.SerializeObject(result);
                    Console.WriteLine($"[0x02 部件状态-兼容] {json}");
                    System.Diagnostics.Debug.WriteLine(json);

                    // 触发事件（所有状态都输出）
                    DeviceStatus eventStatus = DeviceStatus.Online;
                    if (status == 1) eventStatus = DeviceStatus.FireAlarm;
                    else if (status == 0) eventStatus = DeviceStatus.Error;

                    StatusChanged?.Invoke(this, new DeviceEventArgs { Status = eventStatus, Message = json });
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析部件状态异常(兼容模式): {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 解析模拟量 (0x03) - 按GB/T 26875.3-2011国标修正
        /// 国标定义：类型标志(1字节) + 信息对象数目(1字节) + 信息对象
        /// </summary>
        private void ParseAnalogQuantity(byte[] appData, string deviceCode)
        {
            try
            {
                // 【国标修正】应用数据格式：类型(1字节) + 数目(1字节) + 信息体
                if (appData.Length < 2) return;

                // 【国标修正】数目为1字节
                byte messageNumber = appData[1];

                // 如果数目为0，尝试兼容模式
                if (messageNumber == 0 && appData.Length >= 4)
                {
                    int altNumber = (appData[2] << 8) | appData[3];
                    if (altNumber > 0 && (4 + altNumber * 32) <= appData.Length)
                    {
                        ParseAnalogQuantityLegacy(appData, deviceCode);
                        return;
                    }
                }

                if (messageNumber <= 0) return;

                // 国标定义模拟量信息体结构（参照图8）
                const int unitSize = 20; // 估算值，具体需参照国标图8

                for (int i = 0; i < messageNumber && (2 + (i + 1) * unitSize) <= appData.Length; i++)
                {
                    int idx = 2 + i * unitSize;
                    byte[] unit = new byte[unitSize];
                    Array.Copy(appData, idx, unit, 0, unitSize);

                    int systemType = unit[0];
                    int systemAddr = unit[1];
                    int partType = unit[2];
                    int nodeNo = unit[3] | (unit[4] << 8);
                    int loopNo = unit[5] | (unit[6] << 8);
                    int analogType = unit[7];
                    int analogValue = unit[8] | (unit[9] << 8);

                    string upTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string deviceCodeFull = deviceCode + systemAddr + "-" + loopNo + "-" + nodeNo;

                    var result = new
                    {
                        logId = Guid.NewGuid().ToString(),
                        deviceCode = deviceCodeFull,
                        address = $"模拟量:{analogValue}",
                        addressNo = nodeNo.ToString(),
                        fireMainCode = systemAddr,
                        statusName = "模拟量上传",
                        loopNo = loopNo.ToString(),
                        partTypeExplain = "",
                        partType = partType,
                        systemType = systemType,
                        userCode = deviceCode,
                        analogType = analogType,
                        analogValue = analogValue,
                        upTime = upTime
                    };

                    string json = JsonConvert.SerializeObject(result);
                    Console.WriteLine($"[0x03 模拟量-国标] {json}");
                    System.Diagnostics.Debug.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析模拟量异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 模拟量解析 - 兼容模式（使用2字节数目 + 32字节信息体）
        /// </summary>
        private void ParseAnalogQuantityLegacy(byte[] appData, string deviceCode)
        {
            try
            {
                if (appData.Length < 4) return;
                int messageNumber = (appData[2] << 8) | appData[3];
                if (messageNumber <= 0) return;

                int unitSize = 32;

                for (int i = 0; i < messageNumber && (4 + (i + 1) * unitSize) <= appData.Length; i++)
                {
                    int idx = 4 + i * unitSize;
                    string message = BitConverter.ToString(appData, idx, unitSize).Replace("-", "");

                    int systemType = Convert.ToInt32(message.Substring(0, 2), 16);
                    int fireMainCode = Convert.ToInt32(message.Substring(2, 4), 16);
                    int partType = Convert.ToInt32(message.Substring(4, 6), 16);
                    string addressNo = Convert.ToInt32(message.Substring(8, 2) + message.Substring(6, 2), 16).ToString();
                    string loopNo = Convert.ToInt32(message.Substring(12, 2) + message.Substring(10, 2), 16).ToString();
                    int analogType = Convert.ToInt32(message.Substring(14, 16), 16);
                    int analogValue = Convert.ToInt32(message.Substring(16, 20), 16);

                    string upTime = "";
                    if (message.Length >= 32)
                    {
                        int sec = Convert.ToInt32(message.Substring(20, 2), 16);
                        int min = Convert.ToInt32(message.Substring(22, 2), 16);
                        int hour = Convert.ToInt32(message.Substring(24, 2), 16);
                        int day = Convert.ToInt32(message.Substring(26, 2), 16);
                        int month = Convert.ToInt32(message.Substring(28, 2), 16);
                        int year = Convert.ToInt32(message.Substring(30, 2), 16);
                        upTime = $"20{year:D2}-{month:D2}-{day:D2} {hour:D2}:{min:D2}:{sec:D2}";
                    }

                    string deviceCodeFull = deviceCode + fireMainCode + "-" + loopNo + "-" + addressNo;

                    var result = new
                    {
                        logId = Guid.NewGuid().ToString(),
                        deviceCode = deviceCodeFull,
                        address = $"模拟量:{analogValue}",
                        addressNo = addressNo,
                        fireMainCode = fireMainCode,
                        statusName = "模拟量上传",
                        loopNo = loopNo,
                        partTypeExplain = "",
                        partType = partType,
                        systemType = systemType,
                        userCode = deviceCode,
                        analogType = analogType,
                        analogValue = analogValue,
                        upTime = upTime
                    };

                    string json = JsonConvert.SerializeObject(result);
                    Console.WriteLine($"[0x03 模拟量-兼容] {json}");
                    System.Diagnostics.Debug.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析模拟量异常(兼容模式): {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 解析消防主机运行状态 (0x01) - 完全复用JAVA的FireMainStatus
        /// </summary>
        private void ParseFireMainStatus(byte[] appData, string deviceCode)
        {
            try
            {
                if (appData.Length < 12) return;

                string message = BitConverter.ToString(appData).Replace("-", "");

                // 完全按照JAVA的FireMainStatus解析
                // systemType: substring(4,6)
                int systemType = Convert.ToInt32(message.Substring(4, 2), 16);
                // systemCode: substring(6,8)
                int systemCode = Convert.ToInt32(message.Substring(6, 8), 16);
                // 状态位：substring(8,12) - 16位二进制
                string bit = Convert.ToString(Convert.ToInt32(message.Substring(8, 4), 16), 2).PadLeft(16, '0');

                // 解析状态（完全按照JAVA逻辑）
                string status = "无异常";
                if (bit[15] == '1') status = "正常状态";
                else if (bit[14] == '1') status = "火警";
                else if (bit[13] == '1') status = "故障";
                else if (bit[12] == '1') status = "屏蔽";
                else if (bit[11] == '1') status = "监管";
                else if (bit[10] == '1') status = "启动";
                else if (bit[9] == '1') status = "反馈";
                else if (bit[8] == '1') status = "延时状态";
                else if (bit[7] == '1') status = "主电故障";
                else if (bit[6] == '1') status = "备电故障";
                else if (bit[5] == '1') status = "总线故障";
                else if (bit[4] == '1') status = "手动状态";
                else if (bit[3] == '1') status = "配置改变";
                else if (bit[2] == '1') status = "复位";
                else if (bit[1] == '1') status = "预留";
                else if (bit[0] == '1') status = "预留";

                var result = new
                {
                    userCode = deviceCode,
                    systemType = systemType,
                    systemCode = systemCode,
                    statusName = status,
                    status = 2,
                    uploadTime = DateTime.Now.Ticks / 10000
                };

                string json = JsonConvert.SerializeObject(result);
                Console.WriteLine($"[0x01 消防主机状态] {json}");
                System.Diagnostics.Debug.WriteLine(json);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析消防主机状态异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 解析消防主机操作信息 (0x04) - 完全复用JAVA的FireMainStatusOperation
        /// </summary>
        private void ParseFireMainOperation(byte[] appData, string deviceCode)
        {
            try
            {
                if (appData.Length < 10) return;

                string message = BitConverter.ToString(appData).Replace("-", "");

                // 完全按照JAVA的FireMainStatusOperation解析
                int systemType = Convert.ToInt32(message.Substring(4, 2), 16);
                int systemCode = Convert.ToInt32(message.Substring(6, 2), 16);
                // 状态位：substring(8,10) - 8位二进制
                string bit = Convert.ToString(Convert.ToInt32(message.Substring(8, 2), 16), 2).PadLeft(8, '0');

                // 解析状态（完全按照JAVA逻辑）
                string statusName = "正常";
                if (bit[1] == '1') statusName = "测试";
                else if (bit[2] == '1') statusName = "确认";
                else if (bit[3] == '1') statusName = "自检";
                else if (bit[4] == '1') statusName = "警情消除";
                else if (bit[5] == '1') statusName = "手动报警";
                else if (bit[6] == '1') statusName = "消音";
                else if (bit[7] == '1') statusName = "复位";

                var result = new
                {
                    userCode = deviceCode,
                    systemType = systemType,
                    systemCode = systemCode,
                    statusName = statusName,
                    status = 2,
                    uploadTime = DateTime.Now.Ticks / 10000
                };

                string json = JsonConvert.SerializeObject(result);
                Console.WriteLine($"[0x04 消防主机操作] {json}");
                System.Diagnostics.Debug.WriteLine(json);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析消防主机操作异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 解析用户传输装置操作信息 (0x18) - 完全复用JAVA的OperatorStatus
        /// </summary>
        private void ParseUserDeviceOperation(byte[] appData, string deviceCode)
        {
            try
            {
                if (appData.Length < 8) return;

                string message = BitConverter.ToString(appData).Replace("-", "");

                // 完全按照JAVA的OperatorStatus解析
                // 状态位：substring(4,6) - 8位二进制
                string bit = Convert.ToString(Convert.ToInt32(message.Substring(4, 2), 16), 2).PadLeft(8, '0');

                // 解析操作类型（完全按照JAVA逻辑）
                string operationFlag = "正常";
                if (bit[7] == '1') operationFlag = "复位";
                else if (bit[6] == '1') operationFlag = "消音";
                else if (bit[5] == '1') operationFlag = "手动报警";
                else if (bit[4] == '1') operationFlag = "警情解除";
                else if (bit[3] == '1') operationFlag = "自检";
                else if (bit[2] == '1') operationFlag = "查岗应答";
                else if (bit[1] == '1') operationFlag = "测试";

                // 操作人员：substring(6,8)
                int person = Convert.ToInt32(message.Substring(6, 2), 16);

                var result = new
                {
                    userCode = deviceCode,
                    statusName = operationFlag,
                    person = person,
                    status = 2,
                    uploadTime = DateTime.Now.Ticks / 10000
                };

                string json = JsonConvert.SerializeObject(result);
                Console.WriteLine($"[0x18 用户装置操作] {json}");
                System.Diagnostics.Debug.WriteLine(json);

                // 触发事件
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Online,
                    Message = json
                });
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new DeviceEventArgs
                {
                    Status = DeviceStatus.Error,
                    Message = $"解析用户装置操作异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 十六进制字符串转字节数组
        /// </summary>
        private static byte[] HexStringToByteArray(string hex)
        {
            int length = hex.Length / 2;
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private static string ExtractPrintableString(byte[] buf, int start, int length)
        {
            try
            {
                int end = Math.Min(start + length, buf.Length);
                var sb = new StringBuilder();
                for (int i = start; i < end; i++)
                {
                    byte b = buf[i];
                    if (b >= 32 && b <= 126) sb.Append((char)b);
                }
                return sb.ToString();
            }
            catch { return string.Empty; }
        }
    }
}
