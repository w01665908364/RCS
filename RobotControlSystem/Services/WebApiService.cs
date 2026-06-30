using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using RobotControlSystem.Interfaces;
using RobotControlSystem.Models;
using RobotControlSystem.Services;

namespace RobotControlSystem.Services
{
    /// <summary>
    /// Web API 服务 - 暴露 AGV 和机械臂控制接口
    /// 使用 ASP.NET Core Minimal API 实现
    /// 监听端口: 5000
    /// </summary>
    public class WebApiService : IHostedService
    {
        private IHost _host;
        private readonly AgvHttpService _agvService;
        private readonly ElibotRobotService _robotService;

        public WebApiService(AgvHttpService agvService, ElibotRobotService robotService)
        {
            _agvService = agvService;
            _robotService = robotService;
        }
        private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddCors(options =>
                        {
                            options.AddDefaultPolicy(builder =>
                            {
                                builder.AllowAnyOrigin()
                                       .AllowAnyMethod()
                                       .AllowAnyHeader();
                            });
                        }).AddOptions<JsonSerializerOptions>().Configure(o => o.PropertyNameCaseInsensitive = true);
                    })
                    .Configure(app =>
                    {
                        app.UseCors();
                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            // ==================== AGV API ====================
                            
                            // POST /api/agv/navigate - 导航到指定站点
                            endpoints.MapPost("/api/agv/navigate", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<NavigateRequest>(context.Request.Body, _jsonOpts);
                                    if (body == null || string.IsNullOrEmpty(body.TargetSite))
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "缺少 targetSite 参数" }));
                                        return;
                                    }

                                    var (success, msg) = await _agvService.CreateOrderAsync(body.TargetSite, body.Vehicle);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = msg }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/agv/lock - 锁定 AGV
                            endpoints.MapPost("/api/agv/lock", async context =>
                            {
                                try
                                {
                                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                    var body = await JsonSerializer.DeserializeAsync<NavigateRequest>(context.Request.Body, options);
                                    var (success, msg) = await _agvService.LockAsync(body?.Vehicle);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = msg }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/agv/unlock - 解锁 AGV
                            endpoints.MapPost("/api/agv/unlock", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<VehicleRequest>(context.Request.Body, _jsonOpts);
                                    var (success, msg) = await _agvService.UnlockAsync(body?.Vehicle);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = msg }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/agv/pause - 暂停 AGV
                            endpoints.MapPost("/api/agv/pause", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<VehicleRequest>(context.Request.Body, _jsonOpts);
                                    var (success, msg) = await _agvService.PauseAsync(body?.Vehicle);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = msg }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/agv/resume - 继续 AGV
                            endpoints.MapPost("/api/agv/resume", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<VehicleRequest>(context.Request.Body, _jsonOpts);
                                    var (success, msg) = await _agvService.ResumeAsync(body?.Vehicle);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = msg }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/agv/estop - 软急停
                            endpoints.MapPost("/api/agv/estop", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<EStopRequest>(context.Request.Body, _jsonOpts);
                                    var (success, msg) = await _agvService.SetSoftEmergencyStopAsync(body?.Enable ?? true, body?.Vehicle);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = msg }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // GET /api/agv/status - 获取 AGV 状态
                            endpoints.MapGet("/api/agv/status", async context =>
                            {
                                try
                                {
                                    var (success, content, msg) = await _agvService.GetRobotsStatusAsync();
                                    if (success)
                                    {
                                        context.Response.StatusCode = 200;
                                        await context.Response.WriteAsync(content);
                                    }
                                    else
                                    {
                                        context.Response.StatusCode = 500;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = msg }));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // ==================== 机械臂 API ====================

                            // POST /api/robot/connect - 连接机械臂
                            endpoints.MapPost("/api/robot/connect", async context =>
                            {
                                try
                                {
                                    // 【重点修复 20行】：使用 StreamReader 彻底读取 Body，防止由于请求头缺失导致反序列化为空
                                    using var reader = new System.IO.StreamReader(context.Request.Body);
                                    var bodyText = await reader.ReadToEndAsync();

                                    RobotRequest body = null;
                                    if (!string.IsNullOrWhiteSpace(bodyText))
                                    {
                                        try { body = JsonSerializer.Deserialize<RobotRequest>(bodyText, _jsonOpts); }
                                        catch { /* 忽略格式错误，由下方统一拦截 */ }
                                    }

                                    if (body == null || string.IsNullOrEmpty(body.RobotIp))
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "缺少 robotIp 参数" }));
                                        return;
                                    }

                                    _robotService.RobotIP = body.RobotIp;
                                    _robotService.RobotPort = body.RobotPort > 0 ? body.RobotPort : 29999;

                                    var success = await _robotService.ConnectAsync();
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                                    {
                                        success,
                                        connected = _robotService.IsConnected,
                                        message = success ? "连接成功" : "连接失败"
                                    }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/robot/power-on - 机械臂上电
                            endpoints.MapPost("/api/robot/power-on", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<RobotRequest>(context.Request.Body, _jsonOpts);
                                    
                                    // 如果指定了新的IP，先连接
                                    if (body != null && !string.IsNullOrEmpty(body.RobotIp))
                                    {
                                        _robotService.RobotIP = body.RobotIp;
                                        _robotService.RobotPort = body.RobotPort > 0 ? body.RobotPort : 29999;
                                        if (!_robotService.IsConnected)
                                        {
                                            await _robotService.ConnectAsync();
                                        }
                                    }

                                    if (!_robotService.IsConnected)
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = "机械臂未连接" }));
                                        return;
                                    }

                                    var success = await _robotService.PowerOnAsync();
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = success ? "上电成功" : "上电失败" }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/robot/brake - 释放抱闸
                            endpoints.MapPost("/api/robot/brake", async context =>
                            {
                                try
                                {
                                    var body = await JsonSerializer.DeserializeAsync<RobotRequest>(context.Request.Body, _jsonOpts);
                                    
                                    if (body != null && !string.IsNullOrEmpty(body.RobotIp))
                                    {
                                        _robotService.RobotIP = body.RobotIp;
                                        _robotService.RobotPort = body.RobotPort > 0 ? body.RobotPort : 29999;
                                        if (!_robotService.IsConnected)
                                        {
                                            await _robotService.ConnectAsync();
                                        }
                                    }

                                    if (!_robotService.IsConnected)
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = "机械臂未连接" }));
                                        return;
                                    }

                                    var success = await _robotService.ReleaseBrakeAsync();
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = success ? "抱闸释放成功" : "抱闸释放失败" }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // POST /api/robot/run-task - 运行任务
                            endpoints.MapPost("/api/robot/run-task", async context =>
                            {
                                try
                                {
                                    // 同样使用 StreamReader 增强鲁棒性
                                    using var reader = new System.IO.StreamReader(context.Request.Body);
                                    var bodyText = await reader.ReadToEndAsync();

                                    RunTaskRequest body = null;
                                    if (!string.IsNullOrWhiteSpace(bodyText))
                                    {
                                        try { body = JsonSerializer.Deserialize<RunTaskRequest>(bodyText, _jsonOpts); }
                                        catch { }
                                    }

                                    if (body != null && !string.IsNullOrEmpty(body.RobotIp))
                                    {
                                        _robotService.RobotIP = body.RobotIp;
                                        _robotService.RobotPort = body.RobotPort > 0 ? body.RobotPort : 29999;
                                        if (!_robotService.IsConnected)
                                        {
                                            await _robotService.ConnectAsync();
                                        }
                                    }

                                    if (!_robotService.IsConnected)
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = "机械臂未连接" }));
                                        return;
                                    }

                                    var taskName = string.IsNullOrEmpty(body?.TaskName) ? "动作3.task" : body.TaskName;

                                    // 【重点修复：安全注入漏洞 (SEC-INJECT-002)】：过滤 Linux Shell 命令截断符
                                    if (taskName.Contains(";") || taskName.Contains("&") || taskName.Contains("|") || taskName.Contains("`") || taskName.Contains("$"))
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = "非法的任务名称(包含危险字符)" }));
                                        return;
                                    }

                                    var success = await _robotService.RunTaskAsync(taskName);
                                    context.Response.StatusCode = success ? 200 : 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success, message = success ? "任务启动成功" : "任务启动失败", taskName }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // GET /api/robot/status - 获取机械臂状态
                            endpoints.MapGet("/api/robot/status", async context =>
                            {
                                try
                                {
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                                    {
                                        connected = _robotService.IsConnected,
                                        ip = _robotService.RobotIP,
                                        port = _robotService.RobotPort
                                    }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // ==================== 事件规则 API ====================

                            // POST /api/event/trigger - 远程触发事件命令
                            endpoints.MapPost("/api/event/trigger", async context =>
                            {
                                try
                                {
                                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                    var body = await JsonSerializer.DeserializeAsync<RemoteCommandRequest>(context.Request.Body, options);
                                    if (body == null || string.IsNullOrEmpty(body.Command))
                                    {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = "缺少 command 参数" }));
                                        return;
                                    }

                                    // 用匹配引擎匹配规则
                                    var matchedRules = EventMatchEngine.Instance.MatchRules(
                                        DeviceStatus.Custom, body.Command, "远端");

                                    var executedRecipes = new List<string>();

                                    if (matchedRules.Count > 0)
                                    {
                                        // 获取 MainWindow 实例执行配方
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                                            if (mainWindow != null)
                                            {
                                                foreach (var rule in matchedRules)
                                                {
                                                    mainWindow.TryExecuteRecipeByName(rule.RecipeName);
                                                    executedRecipes.Add(rule.RecipeName);
                                                }
                                            }
                                        });
                                    }

                                    context.Response.StatusCode = 200;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                                    {
                                        success = true,
                                        matchedCount = matchedRules.Count,
                                        executedRecipes
                                    }));
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                                }
                            });

                            // ==================== 系统 API ====================

                            // GET /api/system/health - 健康检查
                            endpoints.MapGet("/api/system/health", async context =>
                            {
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                                {
                                    status = "ok",
                                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                    robotConnected = _robotService.IsConnected
                                }));
                            });

                            // GET / - 根路径，显示可用 API
                            endpoints.MapGet("/", async context =>
                            {
                                context.Response.ContentType = "text/html; charset=utf-8";
                                var html = @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<title>RCS API</title>
<style>
body { font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; }
h1 { color: #333; border-bottom: 2px solid #4CAF50; padding-bottom: 10px; }
h2 { color: #4CAF50; margin-top: 30px; }
ul { list-style-type: none; padding: 0; }
li { background: #fff; margin: 8px 0; padding: 12px 15px; border-radius: 5px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
li:hover { background: #e8f5e9; }
code { background: #e0e0e0; padding: 2px 6px; border-radius: 3px; }
</style>
</head>
<body>
<h1>🤖 机器人控制系统 API</h1>

<h2>🚗 AGV 控制</h2>
<ul>
<li><code>POST</code> /api/agv/navigate - 导航到站点 <span style=""color:#666"">{targetSite, vehicle?}</span></li>
<li><code>POST</code> /api/agv/lock - 锁定 <span style=""color:#666"">{vehicle?}</span></li>
<li><code>POST</code> /api/agv/unlock - 解锁 <span style=""color:#666"">{vehicle?}</span></li>
<li><code>POST</code> /api/agv/pause - 暂停 <span style=""color:#666"">{vehicle?}</span></li>
<li><code>POST</code> /api/agv/resume - 继续 <span style=""color:#666"">{vehicle?}</span></li>
<li><code>POST</code> /api/agv/estop - 急停 <span style=""color:#666"">{vehicle?, enable}</span></li>
<li><code>GET</code> /api/agv/status - 获取状态</li>
</ul>

<h2>🦾 机械臂控制</h2>
<ul>
<li><code>POST</code> /api/robot/connect - 连接 <span style=""color:#666"">{robotIp, robotPort?}</span></li>
<li><code>POST</code> /api/robot/power-on - 上电 <span style=""color:#666"">{robotIp?, robotPort?}</span></li>
<li><code>POST</code> /api/robot/brake - 释放抱闸</li>
<li><code>POST</code> /api/robot/run-task - 运行任务 <span style=""color:#666"">{robotIp?, robotPort?, taskName?}</span></li>
<li><code>GET</code> /api/robot/status - 获取状态</li>
</ul>

<h2>📋 事件规则</h2>
<ul>
<li><code>POST</code> /api/event/trigger - 远程触发事件 <span style=""color:#666"">{command}</span></li>
</ul>

<h2>⚙️ 系统</h2>
<ul>
<li><code>GET</code> /api/system/health - 健康检查</li>
</ul>

</body>
</html>";
                                await context.Response.WriteAsync(html);
                            });
                        });
                    })
                    .UseUrls("http://0.0.0.0:5000");
                })
                .Build();

            await _host.StartAsync(cancellationToken);
            System.Diagnostics.Debug.WriteLine("✅ Web API 服务已启动，监听 http://0.0.0.0:5000");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_host != null)
            {
                await _host.StopAsync(cancellationToken);
                _host.Dispose();
                System.Diagnostics.Debug.WriteLine("🛑 Web API 服务已停止");
            }
        }
    }

    // ==================== 请求模型 ====================

    public class NavigateRequest
    {
        public string TargetSite { get; set; }
        public string Vehicle { get; set; } = "AMB-01";
    }

    public class VehicleRequest
    {
        public string Vehicle { get; set; }
    }

    public class EStopRequest
    {
        public string Vehicle { get; set; }
        public bool Enable { get; set; } = true;
    }

    public class RobotRequest
    {
        public string RobotIp { get; set; }
        public int RobotPort { get; set; } = 29999;
    }

    public class RunTaskRequest
    {
        public string RobotIp { get; set; }
        public int RobotPort { get; set; } = 29999;
        public string TaskName { get; set; }
    }

    public record RemoteCommandRequest(string Command);
}
