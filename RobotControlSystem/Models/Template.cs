using System.Collections.Generic;

namespace RobotControlSystem.Models
{
    public class Template
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Brand { get; set; }
        public string Model { get; set; }

        // 背景图片文件路径（相对路径，如 "Images\\guid-xxx.jpg"）
        public string ImagePath { get; set; }

        // 背景图片显示模式 (Fill, Uniform)
        public string BackgroundImageMode { get; set; } = "Fill";

        // 背景色 (HEX格式，例如 "#FF1E1E1E")
        public string BackgroundColor { get; set; } = "#FF1E1E1E";

        // 自定义图片尺寸（像素），null 表示未自定义，使用模式控制
        public double? ImageWidth { get; set; }
        public double? ImageHeight { get; set; }

        public List<PanelElement> Elements { get; set; } = new List<PanelElement>();
    }

    public class PanelElement
    {
        public string Id { get; set; }
        public string Type { get; set; }   // "Button", "Knob", "Lamp"
        public double X { get; set; }       // 毫米单位
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        // Button 特有
        public double? Pressure { get; set; }
        public int? PressDuration { get; set; }

        // Knob 特有
        public double? Angle { get; set; }
        public double? Torque { get; set; }

        // Lamp 特有
        public string Color { get; set; }

        /// <summary>
        /// 扩展参数字典，用于存储机器人任务名、IP 等动态属性
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }
}