using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RobotControlSystem.Models;
using RobotControlSystem.Interfaces;

namespace RobotControlSystem.Services
{
    public class EventMatchEngine
    {
        private static readonly Lazy<EventMatchEngine> _instance = new(() => new EventMatchEngine());
        public static EventMatchEngine Instance => _instance.Value;
        private EventMatchEngine() { }

        /// <summary>
        /// 匹配规则
        /// </summary>
        public List<EventRule> MatchRules(DeviceStatus status, string rawMessage, string? eventTypeHint = null)
        {
            var matchedRules = new List<EventRule>();
            var rules = EventRuleStore.Instance.GetAllRules();

            // 从消息中提取 deviceCode
            string deviceCode = ExtractDeviceCode(rawMessage);
            System.Diagnostics.Debug.WriteLine($"[EventMatchEngine] 提取deviceCode: {deviceCode}");

            foreach (var rule in rules)
            {
                if (!rule.IsEnabled) continue;

                // 事件类型匹配（宽松模式：有SignalCode时不强制过滤）
                bool typeMatch = IsEventTypeCompatible(rule.EventType, status, eventTypeHint, rule.SignalCode);
                if (!typeMatch) continue;

                // 信号编码匹配
                if (IsSignalCodeMatch(deviceCode, rule.SignalCode, rule.MatchCondition))
                {
                    matchedRules.Add(rule);
                    System.Diagnostics.Debug.WriteLine($"[EventMatchEngine] 规则匹配成功: {rule.EventName}, SignalCode={rule.SignalCode}");
                }
            }

            if (matchedRules.Count > 0)
                System.Diagnostics.Debug.WriteLine($"[EventMatchEngine] 共匹配到 {matchedRules.Count} 条规则");

            return matchedRules;
        }

        /// <summary>
        /// 从消息中提取 deviceCode
        /// </summary>
        private string ExtractDeviceCode(string rawMessage)
        {
            if (string.IsNullOrEmpty(rawMessage)) return "";

            try
            {
                // 如果是 JSON 格式，解析 deviceCode
                if (rawMessage.TrimStart().StartsWith("{"))
                {
                    var json = JObject.Parse(rawMessage);
                    return json["deviceCode"]?.ToString() 
                        ?? json["userCode"]?.ToString() 
                        ?? rawMessage;
                }
            }
            catch
            {
                // JSON 解析失败，用原文
            }

            // 非 JSON 直接返回原文
            return rawMessage;
        }

        /// <summary>
        /// 事件类型兼容匹配（宽松模式）
        /// 有 SignalCode 时不过强制过滤，只做参考
        /// </summary>
        private bool IsEventTypeCompatible(EventType ruleEventType, DeviceStatus status, string? eventTypeHint, string signalCode)
        {
            // 有 SignalCode 的规则，事件类型不强制过滤
            if (!string.IsNullOrEmpty(signalCode))
            {
                return true; // 直接通过，靠 SignalCode 精确匹配
            }

            // 没有 SignalCode 的规则，必须靠事件类型匹配
            switch (ruleEventType)
            {
                case EventType.远端命令:
                    return eventTypeHint?.Contains("远端") == true;
                case EventType.火警报警器:
                    return status == DeviceStatus.FireAlarm;
                case EventType.设备状态:
                    return status != DeviceStatus.FireAlarm;
                case EventType.自定义:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 信号编码匹配
        /// </summary>
        private bool IsSignalCodeMatch(string deviceCode, string signalCodePattern, MatchCondition condition)
        {
            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(signalCodePattern))
                return false;

            try
            {
                switch (condition)
                {
                    case MatchCondition.包含:
                        return deviceCode.Contains(signalCodePattern, StringComparison.OrdinalIgnoreCase);
                    case MatchCondition.完全等于:
                        return string.Equals(deviceCode, signalCodePattern, StringComparison.OrdinalIgnoreCase);
                    case MatchCondition.开头是:
                        return deviceCode.StartsWith(signalCodePattern, StringComparison.OrdinalIgnoreCase);
                    case MatchCondition.结尾是:
                        return deviceCode.EndsWith(signalCodePattern, StringComparison.OrdinalIgnoreCase);
                    case MatchCondition.正则匹配:
                        return Regex.IsMatch(deviceCode, signalCodePattern, RegexOptions.IgnoreCase);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventMatchEngine] 信号匹配异常: {ex.Message}");
                return false;
            }
        }

        public bool TestMatch(string message, string pattern, MatchCondition condition)
        {
            string deviceCode = ExtractDeviceCode(message);
            return IsSignalCodeMatch(deviceCode, pattern, condition);
        }
    }
}
