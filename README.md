# RCS - 消防机器人上位机控制系统

> V3.2 | C# WPF | 事件规则引擎

消防机器人上位机控制系统，用于统一监控和管理消防设备、AGV（无人搬运车）和机械臂，实现火警事件的自动匹配与处置流程执行。

## 功能概述

- **消防监控** - 实时接收用传装置火警信号（GB/T 26875.3-2011 协议），展示火警状态与位置
- **事件规则引擎** - 可视化配置事件-响应规则，支持多种匹配条件（包含/完全等于/正则匹配等）
- **AGV 调度** - HTTP API 控制 AGV 导航至指定位置
- **机械臂控制** - TCP 协议控制机械臂执行按压、旋转等操作
- **配方管理** - 流程化编排处置步骤（导航→延时→按压→旋转），一键执行
- **操作面板编辑器** - 可视化配置消防主机操作面板布局
- **Web API** - 暴露 HTTP 接口，支持外部系统集成调用

## 系统架构

```
┌──────────────────────────────────────────────────────────┐
│                     表现层 (WPF/XAML)                     │
│  MainWindow │ FireMonitorWindow │ EventRuleDesigner      │
├──────────────────────────────────────────────────────────┤
│                  ViewModel 层 (MVVM)                      │
├──────────────────────────────────────────────────────────┤
│                     服务层 (Services)                     │
│  AgvHttpService │ ElibotRobotService │ EventProcessor    │
│  EventMatchEngine │ EventRuleStore │ WebApiService        │
├──────────────────────────────────────────────────────────┤
│                     设备层 (Plugins)                      │
│  IUserDevice │ IAgv │ IRobot │ PluginManager (.NET反射)  │
├──────────────────────────────────────────────────────────┤
│                     数据层 (Data)                         │
│  SQLite (配置/配方/模板) │ JSON (事件规则)                │
└──────────────────────────────────────────────────────────┘
```

## 技术栈

| 类别 | 技术 |
|------|------|
| 界面框架 | WPF (XAML) + MVVM |
| 开发语言 | C# / .NET |
| 通讯协议 | HTTP API / TCP Socket / WebSocket |
| 数据存储 | SQLite + JSON |
| 插件机制 | .NET Reflection 动态加载 |

## V3.2 核心升级

从 **"火警 → 同名配方"** 升级为 **"事件 → 规则匹配 → 配方"**：

```
用传装置 → EventProcessor(后台线程) → EventMatchEngine(规则匹配) → 执行配方
                                        ↑
                                 EventRules.json
```

- 支持 5 种匹配条件：包含、完全等于、开头是、结尾是、正则匹配
- 支持多种事件类型：火警报警器、远端命令、设备状态、自定义
- 可视化规则设计器，支持启用/禁用/测试匹配
- 后台线程处理，不阻塞 UI

## 项目结构

```
RobotControlSystem/
├── MainWindow.xaml.cs          # 主控制界面
├── FireMonitorWindow.xaml.cs   # 消防监控窗口
├── Models/
│   ├── EventRule.cs            # 事件规则模型
│   ├── FireMonitorData.cs      # 消防监控数据
│   ├── Recipe.cs               # 配方数据模型
│   ├── Template.cs             # 操作面板模板
│   └── DeviceConfig.cs         # 设备配置
├── Services/
│   ├── EventProcessor.cs       # 事件处理器
│   ├── EventMatchEngine.cs     # 规则匹配引擎
│   ├── EventRuleStore.cs       # 规则存储
│   ├── AgvHttpService.cs       # AGV HTTP 控制
│   ├── ElibotRobotService.cs   # 机械臂 TCP 控制
│   ├── FireDataWebSocketService.cs
│   └── WebApiService.cs        # HTTP API 暴露
├── Editors/
│   ├── RecipeFlowEditorWindow  # 配方流程编辑器
│   ├── OperationPanelEditorWindow # 操作面板编辑器
│   └── EventRuleDesignerWindow # 事件规则设计器
└── ViewModels/
    └── FireMonitorViewModel.cs

Plugin.UserDevice/
└── UserDevicePlugin.cs         # 用传装置插件 (GB/T 26875.3)
```

## 通讯方式

| 协议 | 场景 | 说明 |
|------|------|------|
| HTTP API | AGV 控制 | 请求-响应模式（/lock, /unlock, /navigate） |
| TCP Socket | 机械臂控制 | Dashboard 协议，持续连接 |
| WebSocket | 消防数据推送 | 双向实时传输 |
| TCP (7799) | 用传装置 | GB/T 26875.3-2011 协议解析 |

## 开发环境

- **IDE**: Visual Studio 2022
- **框架**: .NET 8.0
- **OS**: Windows 11
