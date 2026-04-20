RCS 机器人控制系统 - HTTP API 使用说明

概述

本项目已集成 HTTP API 功能，允许外部系统通过 HTTP 请求控制 AGV 小车和机械臂。
API 服务端口: 5000  
服务启动: 随程序自动启动

快速测试

程序启动后，打开浏览器访问：
http://localhost:5000 - API 文档页面
http://localhost:5000/api/system/health - 健康检查


 API 端点列表
 AGV 小车控制

| 方法 | 端点 | 请求体 | 说明 |
|------|------|--------|------|
| POST | /api/agv/navigate | `{"targetSite":"AP3","vehicle":"AMB-01"}` | 导航到站点 |
| POST | /api/agv/lock | `{"vehicle":"AMB-01"}` | 锁定 AGV |
| POST | /api/agv/unlock | `{"vehicle":"AMB-01"}` | 解锁 AGV |
| POST | /api/agv/pause | `{"vehicle":"AMB-01"}` | 暂停导航 |
| POST | /api/agv/resume | `{"vehicle":"AMB-01"}` | 继续导航 |
| POST | /api/agv/estop | `{"vehicle":"AMB-01","enable":true}` | 软急停 |
| GET | /api/agv/status | | 获取 AGV 状态 |

机械臂控制

| 方法 | 端点 | 请求体 | 说明 |
| POST | /api/robot/connect | `{"robotIp":"192.168.1.60","robotPort":29999}` | 连接机械臂 |
| POST | /api/robot/power-on | `{"robotIp":"192.168.1.60"}` | 上电 |
| POST | /api/robot/brake | `{}` | 释放抱闸 |
| POST | /api/robot/run-task | `{"taskName":"动作3.task"}` | 运行任务 |
| GET | /api/robot/status | - | 获取连接状态 |

系统

| 方法 | 端点 | 说明 |
| GET | /api/system/health | 健康检查 |
| GET | / | API 文档页面 |



使用示例

cURL 示例

bash
健康检查
curl http://localhost:5000/api/system/health

AGV 导航到 AP3 站点
curl -X POST http://localhost:5000/api/agv/navigate \
  -H "Content-Type: application/json" \
  -d '{"targetSite":"AP3","vehicle":"AMB-01"}'

AGV 锁定
curl -X POST http://localhost:5000/api/agv/lock \
  -H "Content-Type: application/json" \
  -d '{"vehicle":"AMB-01"}'

 AGV 软急停（启用）
curl -X POST http://localhost:5000/api/agv/estop \
  -H "Content-Type: application/json" \
  -d '{"vehicle":"AMB-01","enable":true}'

机械臂连接
curl -X POST http://localhost:5000/api/robot/connect \
  -H "Content-Type: application/json" \
  -d '{"robotIp":"192.168.1.60","robotPort":29999}'

机械臂上电
curl -X POST http://localhost:5000/api/robot/power-on

 运行任务
curl -X POST http://localhost:5000/api/robot/run-task \
  -H "Content-Type: application/json" \
  -d '{"taskName":"动作3.task"}'

Postman 使用

1. 创建新请求，设置方法（GET/POST）
2. URL 输入：`http://localhost:5000/api/xxx`
3. 如果是 POST，在 Body 选择 raw -> JSON，填入请求体

响应格式

所有 API 返回 JSON 格式：

成功响应:
json
{
  "success": true,
  "message": "操作成功"
}

失败响应:
json
{
  "success": false,
  "error": "错误信息"
}

注意事项
1. 防火墙: 远程访问需在 Windows 防火墙放行 5000 端口
2. 安全性: 当前无认证，建议生产环境添加 API Key 或 JWT
3. 端口冲突: 如 5000 被占用，修改 `WebApiService.cs` 中的 `.UseUrls()`

版本信息
RCS 版本: 1.0
.NET 版本: 8.0
API 框架: ASP.NET Core Minimal API
修改日期: 2026-04-14