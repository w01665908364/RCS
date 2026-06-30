using System;

namespace RobotControlSystem.Models
{
    /// <summary>
    /// 事件类型枚举
    /// </summary>
    public enum EventType
    {
        /// <summary>远端发来的命令</summary>
        远端命令,

        /// <summary>消防主机火警信号</summary>
        火警报警器,

        /// <summary>设备状态变化</summary>
        设备状态,

        /// <summary>其他自定义事件</summary>
        自定义
    }

    /// <summary>
    /// 消息匹配条件枚举
    /// </summary>
    public enum MatchCondition
    {
        /// <summary>Message 包含指定字符串</summary>
        包含,

        /// <summary>Message 完全等于指定字符串</summary>
        完全等于,

        /// <summary>Message 以指定字符串开头</summary>
        开头是,

        /// <summary>Message 以指定字符串结尾</summary>
        结尾是,

        /// <summary>Message 匹配正则表达式</summary>
        正则匹配
    }

    /// <summary>
    /// 事件规则数据模型
    /// 用于配置消控事件的匹配规则和对应的执行配方
    /// </summary>
    public class EventRule
    {
        /// <summary>
        /// 规则唯一标识
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 事件名称，用于标识和显示
        /// </summary>
        public string EventName { get; set; } = string.Empty;

        /// <summary>
        /// 事件类型
        /// </summary>
        public EventType EventType { get; set; }

        /// <summary>
        /// 信号的唯一编码（deviceCode），如 GST200-001
        /// </summary>
        public string SignalCode { get; set; } = string.Empty;

        /// <summary>
        /// 匹配条件
        /// </summary>
        public MatchCondition MatchCondition { get; set; }

        /// <summary>
        /// 触发后执行的配方名称
        /// </summary>
        public string RecipeName { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用该规则
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 规则优先级（用于排序）
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 备注说明
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}
