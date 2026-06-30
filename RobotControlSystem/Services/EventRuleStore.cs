using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RobotControlSystem.Models;

namespace RobotControlSystem.Services
{
    /// <summary>
    /// 事件规则持久化服务
    /// 使用 JSON 文件存储规则数据，支持线程安全的读写操作
    /// </summary>
    public class EventRuleStore
    {
        private static readonly Lazy<EventRuleStore> _instance = new(() => new EventRuleStore());

        /// <summary>
        /// 单例实例
        /// </summary>
        public static EventRuleStore Instance => _instance.Value;

        /// <summary>
        /// JSON 文件存储路径
        /// </summary>
        private readonly string _filePath;

        /// <summary>
        /// 线程安全锁对象
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// 内存中的规则列表
        /// </summary>
        private List<EventRule> _rules;

        /// <summary>
        /// 下一个可用的规则Id
        /// </summary>
        private int _nextId = 1;

        /// <summary>
        /// 私有构造函数，实现单例模式
        /// </summary>
        private EventRuleStore()
        {
            // 文件存储在应用程序根目录
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventRules.json");
            _rules = LoadFromFile();
        }

        /// <summary>
        /// 获取所有规则的副本（线程安全）
        /// </summary>
        /// <returns>规则列表的副本</returns>
        public List<EventRule> GetAllRules()
        {
            lock (_lock)
            {
                return _rules.OrderBy(r => r.Priority).ToList();
            }
        }

        /// <summary>
        /// 添加新规则
        /// </summary>
        /// <param name="rule">要添加的规则</param>
        public void AddRule(EventRule rule)
        {
            lock (_lock)
            {
                // 自动分配Id
                if (rule.Id <= 0)
                {
                    rule.Id = _nextId;
                }
                if (rule.Id >= _nextId)
                {
                    _nextId = rule.Id + 1;
                }
                _rules.Add(rule);
                SaveToFile();
            }
        }

        /// <summary>
        /// 更新现有规则
        /// </summary>
        /// <param name="rule">要更新的规则</param>
        /// <returns>是否更新成功</returns>
        public bool UpdateRule(EventRule rule)
        {
            lock (_lock)
            {
                var existingRule = _rules.FirstOrDefault(r => r.Id == rule.Id);
                if (existingRule == null)
                {
                    return false;
                }

                // 更新规则属性
                var index = _rules.IndexOf(existingRule);
                _rules[index] = rule;
                SaveToFile();
                return true;
            }
        }

        /// <summary>
        /// 删除指定Id的规则
        /// </summary>
        /// <param name="id">要删除的规则Id</param>
        /// <returns>是否删除成功</returns>
        public bool DeleteRule(int id)
        {
            lock (_lock)
            {
                var rule = _rules.FirstOrDefault(r => r.Id == id);
                if (rule == null)
                {
                    return false;
                }

                _rules.Remove(rule);
                SaveToFile();
                return true;
            }
        }

        /// <summary>
        /// 根据Id查找规则
        /// </summary>
        /// <param name="id">规则Id</param>
        /// <returns>找到的规则，未找到返回null</returns>
        public EventRule? FindRule(int id)
        {
            lock (_lock)
            {
                return _rules.FirstOrDefault(r => r.Id == id);
            }
        }

        /// <summary>
        /// 从文件加载规则列表
        /// </summary>
        /// <returns>规则列表</returns>
        private List<EventRule> LoadFromFile()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var rules = JsonConvert.DeserializeObject<List<EventRule>>(json);
                    if (rules != null && rules.Count > 0)
                    {
                        // 更新下一个可用的Id
                        _nextId = rules.Max(r => r.Id) + 1;
                        System.Diagnostics.Debug.WriteLine($"[EventRuleStore] 从文件加载了 {rules.Count} 条规则");
                        return rules;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventRuleStore] 加载规则文件失败: {ex.Message}");
            }

            // 文件不存在或加载失败，返回默认规则
            System.Diagnostics.Debug.WriteLine("[EventRuleStore] 使用默认规则");
            return GetDefaultRules();
        }

        /// <summary>
        /// 保存规则列表到文件
        /// </summary>
        private void SaveToFile()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_rules, Formatting.Indented);
                File.WriteAllText(_filePath, json);
                System.Diagnostics.Debug.WriteLine($"[EventRuleStore] 已保存 {_rules.Count} 条规则到文件");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventRuleStore] 保存规则文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取默认规则列表
        /// </summary>
        /// <returns>默认规则列表</returns>
        private List<EventRule> GetDefaultRules()
        {
            _nextId = 4; // 默认规则使用1、2、3，后续从4开始
            return new List<EventRule>
            {
                new EventRule
                {
                    Id = 1,
                    EventName = "火警触发",
                    EventType = EventType.火警报警器,
                    SignalCode = "GST200-001",
                    MatchCondition = MatchCondition.完全等于,
                    RecipeName = "火警应急",
                    IsEnabled = true,
                    Priority = 1,
                    Description = "当信号代码等于GST200-001时执行"
                },
                new EventRule
                {
                    Id = 2,
                    EventName = "远端启动",
                    EventType = EventType.远端命令,
                    SignalCode = "CMD_START",
                    MatchCondition = MatchCondition.完全等于,
                    RecipeName = "启动巡检",
                    IsEnabled = true,
                    Priority = 2,
                    Description = "当信号代码等于CMD_START时执行"
                },
                new EventRule
                {
                    Id = 3,
                    EventName = "设备异常",
                    EventType = EventType.设备状态,
                    SignalCode = "C800000000001-1-98",
                    MatchCondition = MatchCondition.开头是,
                    RecipeName = "异常处理",
                    IsEnabled = false,
                    Priority = 3,
                    Description = "当信号代码以C800000000001-1-98开头时执行（示例，未启用）"
                }
            };
        }
    }
}
