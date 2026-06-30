using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotControlSystem.Models;
using RobotControlSystem.Services;
using RobotControlSystem.Interfaces;

namespace RobotControlSystem.Editors
{
    /// <summary>
    /// EventRuleDesignerWindow.xaml 的交互逻辑
    /// 事件规则设计器窗口，用于可视化配置消控事件规则
    /// </summary>
    public partial class EventRuleDesignerWindow : Window
    {
        /// <summary>
        /// 事件类型列表（静态资源供XAML绑定）
        /// </summary>
        public static List<EventType> EventTypes { get; } = new(Enum.GetValues<EventType>().ToList());

        /// <summary>
        /// 匹配条件列表（静态资源供XAML绑定）
        /// </summary>
        public static List<MatchCondition> MatchConditions { get; } = new(Enum.GetValues<MatchCondition>().ToList());

        /// <summary>
        /// 配方名称列表（从数据库加载）
        /// </summary>
        public static List<string> RecipeNames { get; private set; } = new();

        /// <summary>
        /// 当前编辑的规则列表
        /// </summary>
        private List<EventRule> _editingRules;

        /// <summary>
        /// 选中的规则索引
        /// </summary>
        private int _selectedIndex = -1;

        /// <summary>
        /// 是否有未保存的更改
        /// </summary>
        private bool _hasUnsavedChanges = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        public EventRuleDesignerWindow()
        {
            InitializeComponent();

            // 初始化编辑规则列表
            _editingRules = new List<EventRule>();

            // 加载配方列表
            LoadRecipeNames();

            // 加载规则
            LoadRules();

            // 注意：EventProcessor 不再是单例，由 MainWindow 创建和管理
            // 设计器窗口不直接订阅 EventProcessor 事件
        }

        /// <summary>
        /// 窗口关闭时触发
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // 无需取消订阅，设计器不直接订阅 EventProcessor
        }

        /// <summary>
        /// 从数据库加载配方名称列表
        /// </summary>
        private void LoadRecipeNames()
        {
            try
            {
                // 从数据库加载配方名称
                var db = new DatabaseHelper();
                var recipes = db.GetRecipes();
                RecipeNames = recipes.Select(r => r.Name).ToList();

                // 如果数据库为空，提供默认选项
                if (RecipeNames.Count == 0)
                {
                    RecipeNames = new List<string>
                    {
                        "火警应急",
                        "启动巡检",
                        "停止巡检",
                        "异常处理",
                        "设备复位",
                        "紧急疏散",
                        "日常巡检"
                    };
                }

                System.Diagnostics.Debug.WriteLine($"[EventRuleDesigner] 已加载 {RecipeNames.Count} 个配方");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventRuleDesigner] 加载配方列表失败: {ex.Message}");
                RecipeNames = new List<string> { "（加载失败）" };
            }
        }

        /// <summary>
        /// 加载规则到DataGrid
        /// </summary>
        private void LoadRules()
        {
            try
            {
                var rules = EventRuleStore.Instance.GetAllRules();
                _editingRules = rules.Select(r => CloneRule(r)).ToList();
                RulesDataGrid.ItemsSource = null;
                RulesDataGrid.ItemsSource = _editingRules;

                UpdateStatusText();
                _hasUnsavedChanges = false;

                System.Diagnostics.Debug.WriteLine($"[EventRuleDesigner] 已加载 {_editingRules.Count} 条规则");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载规则失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 克隆规则对象
        /// </summary>
        private EventRule CloneRule(EventRule source)
        {
            return new EventRule
            {
                Id = source.Id,
                EventName = source.EventName,
                EventType = source.EventType,
                SignalCode = source.SignalCode,
                MatchCondition = source.MatchCondition,
                RecipeName = source.RecipeName,
                IsEnabled = source.IsEnabled,
                Priority = source.Priority,
                Description = source.Description
            };
        }

        /// <summary>
        /// 添加规则按钮点击
        /// </summary>
        private void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newRule = new EventRule
                {
                    Id = 0, // 临时Id，保存时会自动分配
                    EventName = "新规则",
                    EventType = EventType.火警报警器,
                    SignalCode = "",
                    MatchCondition = MatchCondition.包含,
                    RecipeName = RecipeNames.FirstOrDefault() ?? "",
                    IsEnabled = true,
                    Priority = 0,
                    Description = ""
                };

                _editingRules.Add(newRule);
                RulesDataGrid.ItemsSource = null;
                RulesDataGrid.ItemsSource = _editingRules;

                // 选中新添加的行
                RulesDataGrid.SelectedIndex = _editingRules.Count - 1;

                _hasUnsavedChanges = true;
                UpdateStatusText();

                System.Diagnostics.Debug.WriteLine("[EventRuleDesigner] 已添加新规则");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加规则失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除规则按钮点击
        /// </summary>
        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _editingRules.Count)
            {
                MessageBox.Show("请先选择要删除的规则", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rule = _editingRules[_selectedIndex];
            var result = MessageBox.Show($"确定要删除规则 \"{rule.EventName}\" 吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _editingRules.RemoveAt(_selectedIndex);
                RulesDataGrid.ItemsSource = null;
                RulesDataGrid.ItemsSource = _editingRules;

                _selectedIndex = -1;
                _hasUnsavedChanges = true;
                UpdateStatusText();

                System.Diagnostics.Debug.WriteLine("[EventRuleDesigner] 已删除规则");
            }
        }

        /// <summary>
        /// 保存按钮点击
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 先清除所有现有规则
                var existingRules = EventRuleStore.Instance.GetAllRules();
                foreach (var rule in existingRules.ToList())
                {
                    EventRuleStore.Instance.DeleteRule(rule.Id);
                }

                // 添加所有编辑后的规则
                for (int i = 0; i < _editingRules.Count; i++)
                {
                    _editingRules[i].Id = i + 1;
                    EventRuleStore.Instance.AddRule(_editingRules[i]);
                }

                _hasUnsavedChanges = false;
                UpdateStatusText();

                MessageBox.Show("规则保存成功！", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Diagnostics.Debug.WriteLine("[EventRuleDesigner] 规则已保存");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存规则失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 刷新配方按钮点击
        /// </summary>
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadRecipeNames();
            // 刷新DataGrid以更新ComboBox
            RulesDataGrid.ItemsSource = null;
            RulesDataGrid.ItemsSource = _editingRules;
            MessageBox.Show("配方列表已刷新", "刷新成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 测试匹配按钮点击
        /// </summary>
        private void BtnTestMatch_Click(object sender, RoutedEventArgs e)
        {
            if (TestPanel.Visibility == Visibility.Visible)
            {
                TestPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                TestPanel.Visibility = Visibility.Visible;
                TestMessageBox.Focus();
            }
        }

        /// <summary>
        /// 执行测试按钮点击
        /// </summary>
        private void BtnExecuteTest_Click(object sender, RoutedEventArgs e)
        {
            var testMessage = TestMessageBox.Text;
            if (string.IsNullOrWhiteSpace(testMessage))
            {
                MessageBox.Show("请输入测试消息", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 使用匹配引擎测试
            var matchedRules = EventMatchEngine.Instance.MatchRules(
                DeviceStatus.FireAlarm, 
                testMessage, 
                "远端");

            if (matchedRules.Count > 0)
            {
                var ruleNames = string.Join(", ", matchedRules.Select(r => r.EventName));
                MessageBox.Show($"匹配到 {matchedRules.Count} 条规则:\n{ruleNames}", "测试结果", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("没有匹配到任何规则", "测试结果", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// DataGrid选中项改变
        /// </summary>
        private void RulesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedIndex = RulesDataGrid.SelectedIndex;
        }

        /// <summary>
        /// DataGrid单元格编辑结束
        /// </summary>
        private void RulesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            _hasUnsavedChanges = true;
            UpdateStatusText();
        }

        /// <summary>
        /// 规则匹配事件处理
        /// </summary>
        private void OnRuleMatched(EventRule rule, DeviceEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[EventRuleDesigner] 规则 {rule.EventName} 被触发，执行配方: {rule.RecipeName}");
        }

        /// <summary>
        /// 更新状态栏文本
        /// </summary>
        private void UpdateStatusText()
        {
            var status = $"共 {_editingRules.Count} 条规则";
            if (_hasUnsavedChanges)
            {
                status += "（有未保存的更改）";
            }
            StatusText.Text = status;
        }
    }
}
