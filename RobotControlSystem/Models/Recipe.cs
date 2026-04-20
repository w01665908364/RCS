using System.Collections.Generic;

namespace RobotControlSystem.Models
{
    public class Recipe
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TemplateId { get; set; }
        public List<FlowNode> Nodes { get; set; } = new List<FlowNode>();
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }

    public class FlowNode
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public class Connection
    {
        public string FromNodeId { get; set; }
        public int FromOutput { get; set; }
        public string ToNodeId { get; set; }
        public int ToInput { get; set; }
    }
}