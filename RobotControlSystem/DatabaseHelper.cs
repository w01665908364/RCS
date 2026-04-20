using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Newtonsoft.Json;
using RobotControlSystem.Models;

namespace RobotControlSystem
{
    public class DatabaseHelper
    {
        private readonly string _dbPath;
        private readonly string _imageFolder;

        public DatabaseHelper()
        {
            // 数据库文件将出现在你的项目 bin\Debug 或 bin\Release 文件夹下
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            _dbPath = Path.Combine(appDir, "database.db");

            // 你原有的创建目录逻辑在这里其实不需要了，因为BaseDirectory一定存在
            // 但可以保留，如果你将来改成其他可能需要创建父目录的路径
            string directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();

            // 创建模板表（如果不存在）
            string createTemplates = @"
                CREATE TABLE IF NOT EXISTS templates (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    brand TEXT,
                    model TEXT,
                    image_path TEXT,
                    elements_json TEXT,
                    background_image_mode TEXT DEFAULT 'Fill',
                    background_color TEXT DEFAULT '#FF1E1E1E',
                    image_width REAL,
                    image_height REAL
                )";
            using var cmd1 = new SQLiteCommand(createTemplates, conn);
            cmd1.ExecuteNonQuery();

            // ========== 迁移：检查并添加 image_path 列（如果不存在） ==========
            try
            {
                // 尝试查询 image_path 列，如果抛出异常则说明列不存在，需要添加
                using var checkCol = new SQLiteCommand("SELECT image_path FROM templates LIMIT 1", conn);
                checkCol.ExecuteScalar();
            }
            catch (SQLiteException)
            {
                // 列不存在，添加 image_path 列
                using var alterCmd = new SQLiteCommand("ALTER TABLE templates ADD COLUMN image_path TEXT", conn);
                alterCmd.ExecuteNonQuery();
            }

            // 可选：删除旧的 imageBase64 列（如果存在），但为了安全保留，不删除
            // 如果存在 imageBase64 列，可以将其数据迁移到 image_path？这里不自动迁移，只确保新列存在

            // 检查并添加其他可能缺失的列（background_image_mode, background_color, image_width, image_height）
            try
            {
                using var checkMode = new SQLiteCommand("SELECT background_image_mode FROM templates LIMIT 1", conn);
                checkMode.ExecuteScalar();
            }
            catch (SQLiteException)
            {
                using var alterCmd = new SQLiteCommand("ALTER TABLE templates ADD COLUMN background_image_mode TEXT DEFAULT 'Fill'", conn);
                alterCmd.ExecuteNonQuery();
            }

            try
            {
                using var checkColor = new SQLiteCommand("SELECT background_color FROM templates LIMIT 1", conn);
                checkColor.ExecuteScalar();
            }
            catch (SQLiteException)
            {
                using var alterCmd = new SQLiteCommand("ALTER TABLE templates ADD COLUMN background_color TEXT DEFAULT '#FF1E1E1E'", conn);
                alterCmd.ExecuteNonQuery();
            }

            try
            {
                using var checkWidth = new SQLiteCommand("SELECT image_width FROM templates LIMIT 1", conn);
                checkWidth.ExecuteScalar();
            }
            catch (SQLiteException)
            {
                using var alterCmd = new SQLiteCommand("ALTER TABLE templates ADD COLUMN image_width REAL", conn);
                alterCmd.ExecuteNonQuery();
            }

            try
            {
                using var checkHeight = new SQLiteCommand("SELECT image_height FROM templates LIMIT 1", conn);
                checkHeight.ExecuteScalar();
            }
            catch (SQLiteException)
            {
                using var alterCmd = new SQLiteCommand("ALTER TABLE templates ADD COLUMN image_height REAL", conn);
                alterCmd.ExecuteNonQuery();
            }

            // 创建配方表（如果不存在）
            string createRecipes = @"
                CREATE TABLE IF NOT EXISTS recipes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    template_id INTEGER NOT NULL,
                    nodes_json TEXT,
                    connections_json TEXT,
                    FOREIGN KEY(template_id) REFERENCES templates(id) ON DELETE CASCADE
                )";
            using var cmd2 = new SQLiteCommand(createRecipes, conn);
            cmd2.ExecuteNonQuery();

            // 创建设备配置表（如果不存在）
            string createDeviceConfigs = @"
                CREATE TABLE IF NOT EXISTS device_configs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    deviceType TEXT NOT NULL,
                    name TEXT NOT NULL,
                    ipAddress TEXT NOT NULL,
                    port INTEGER NOT NULL,
                    protocol TEXT NOT NULL,
                    isEnabled INTEGER DEFAULT 1,
                    lastChecked TEXT,
                    connectionStatus TEXT DEFAULT '未知'
                )";
            using var cmd3 = new SQLiteCommand(createDeviceConfigs, conn);
            cmd3.ExecuteNonQuery();

            // 插入示例模板（如果表为空）
            var checkTemplates = new SQLiteCommand("SELECT COUNT(*) FROM templates", conn);
            if ((long)checkTemplates.ExecuteScalar() == 0)
            {
                var sampleElements = new List<PanelElement>
                {
                    new PanelElement { Id = "BTN_001", Type = "Button", X = 10, Y = 15, Width = 4, Height = 2, Pressure = 1.5, PressDuration = 500 },
                    new PanelElement { Id = "BTN_002", Type = "Button", X = 30, Y = 15, Width = 4, Height = 2, Pressure = 1.2, PressDuration = 500 },
                    new PanelElement { Id = "KNOB_001", Type = "Knob", X = 50, Y = 30, Width = 5, Height = 5, Angle = 90, Torque = 1.0 },
                    new PanelElement { Id = "LAMP_001", Type = "Lamp", X = 70, Y = 50, Width = 3, Height = 3, Color = "#ffff00" }
                };
                string elementsJson = JsonConvert.SerializeObject(sampleElements);
                using var insert = new SQLiteCommand(
                    "INSERT INTO templates (name, brand, model, image_path, elements_json, background_image_mode, background_color, image_width, image_height) VALUES (@name, @brand, @model, NULL, @json, 'Fill', '#FF1E1E1E', NULL, NULL)",
                    conn);
                insert.Parameters.AddWithValue("@name", "GST200-V1.0");
                insert.Parameters.AddWithValue("@brand", "海湾");
                insert.Parameters.AddWithValue("@model", "GST200");
                insert.Parameters.AddWithValue("@json", elementsJson);
                insert.ExecuteNonQuery();
            }

            // 插入示例配方（如果为空）
            var checkRecipes = new SQLiteCommand("SELECT COUNT(*) FROM recipes", conn);
            if ((long)checkRecipes.ExecuteScalar() == 0)
            {
                var sampleNodes = new List<FlowNode>
                {
                    new FlowNode { Id = "node_1001", Type = "AGV导航", X = 100, Y = 100,
                        Parameters = new Dictionary<string, object> { ["targetX"] = 50, ["targetY"] = 80, ["speed"] = 20, ["timeout"] = 30 } },
                    new FlowNode { Id = "node_1002", Type = "机械臂按压", X = 300, Y = 100,
                        Parameters = new Dictionary<string, object> { ["buttonId"] = "BTN_001", ["pressure"] = 1.5, ["duration"] = 500 } }
                };
                var sampleConnections = new List<Connection>
                {
                    new Connection { FromNodeId = "node_1001", FromOutput = 0, ToNodeId = "node_1002", ToInput = 0 }
                };
                string nodesJson = JsonConvert.SerializeObject(sampleNodes);
                string connectionsJson = JsonConvert.SerializeObject(sampleConnections);
                using var insertRecipe = new SQLiteCommand(
                    "INSERT INTO recipes (name, template_id, nodes_json, connections_json) VALUES (@name, @tid, @nodes, @conns)",
                    conn);
                insertRecipe.Parameters.AddWithValue("@name", "火警处置流程");
                insertRecipe.Parameters.AddWithValue("@tid", 1);
                insertRecipe.Parameters.AddWithValue("@nodes", nodesJson);
                insertRecipe.Parameters.AddWithValue("@conns", connectionsJson);
                insertRecipe.ExecuteNonQuery();
            }

            // 插入示例设备（如果为空）
            var checkDevices = new SQLiteCommand("SELECT COUNT(*) FROM device_configs", conn);
            if ((long)checkDevices.ExecuteScalar() == 0)
            {
                var sampleDevices = new[]
                {
                    new { DeviceType = "UserTransmitter", Name = "用户信息传输装置1", IPAddress = "192.168.1.100", Port = 8080, Protocol = "TCP" },
                    new { DeviceType = "AGV", Name = "AGV-01", IPAddress = "127.0.0.1", Port = 8088, Protocol = "HTTP" },
                    new { DeviceType = "RoboticArm", Name = "机械臂-01", IPAddress = "192.168.1.140", Port = 29999, Protocol = "TCP" }
                };
                foreach (var dev in sampleDevices)
                {
                    using var insert = new SQLiteCommand(
                        "INSERT INTO device_configs (deviceType, name, ipAddress, port, protocol, isEnabled) VALUES (@type, @name, @ip, @port, @proto, 1)",
                        conn);
                    insert.Parameters.AddWithValue("@type", dev.DeviceType);
                    insert.Parameters.AddWithValue("@name", dev.Name);
                    insert.Parameters.AddWithValue("@ip", dev.IPAddress);
                    insert.Parameters.AddWithValue("@port", dev.Port);
                    insert.Parameters.AddWithValue("@proto", dev.Protocol);
                    insert.ExecuteNonQuery();
                }
            }
        }

        // ========== 模板操作 ==========
        public List<Template> GetTemplates()
        {
            var list = new List<Template>();
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT id, name, brand, model, image_path, elements_json, background_image_mode, background_color, image_width, image_height FROM templates";
            using var cmd = new SQLiteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var t = new Template
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Brand = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Model = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                    BackgroundImageMode = reader.IsDBNull(6) ? "Fill" : reader.GetString(6),
                    BackgroundColor = reader.IsDBNull(7) ? "#FF1E1E1E" : reader.GetString(7),
                    ImageWidth = reader.IsDBNull(8) ? null : (double?)reader.GetDouble(8),
                    ImageHeight = reader.IsDBNull(9) ? null : (double?)reader.GetDouble(9)
                };
                string json = reader.IsDBNull(5) ? null : reader.GetString(5);
                if (!string.IsNullOrEmpty(json))
                    t.Elements = JsonConvert.DeserializeObject<List<PanelElement>>(json) ?? new List<PanelElement>();
                list.Add(t);
            }
            return list;
        }

        public Template GetTemplate(int id)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT id, name, brand, model, image_path, elements_json, background_image_mode, background_color, image_width, image_height FROM templates WHERE id=@id";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var t = new Template
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Brand = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Model = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                    BackgroundImageMode = reader.IsDBNull(6) ? "Fill" : reader.GetString(6),
                    BackgroundColor = reader.IsDBNull(7) ? "#FF1E1E1E" : reader.GetString(7),
                    ImageWidth = reader.IsDBNull(8) ? null : (double?)reader.GetDouble(8),
                    ImageHeight = reader.IsDBNull(9) ? null : (double?)reader.GetDouble(9)
                };
                string json = reader.IsDBNull(5) ? null : reader.GetString(5);
                if (!string.IsNullOrEmpty(json))
                    t.Elements = JsonConvert.DeserializeObject<List<PanelElement>>(json) ?? new List<PanelElement>();
                return t;
            }
            return null;
        }

        public int SaveTemplate(Template template)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string elementsJson = JsonConvert.SerializeObject(template.Elements);
            if (template.Id == 0)
            {
                string sql = @"INSERT INTO templates (name, brand, model, image_path, elements_json, background_image_mode, background_color, image_width, image_height)
                               VALUES (@name, @brand, @model, @imgPath, @json, @mode, @color, @width, @height);
                               SELECT last_insert_rowid();";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@name", template.Name);
                cmd.Parameters.AddWithValue("@brand", (object?)template.Brand ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@model", (object?)template.Model ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@imgPath", (object?)template.ImagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@json", elementsJson);
                cmd.Parameters.AddWithValue("@mode", template.BackgroundImageMode ?? "Fill");
                cmd.Parameters.AddWithValue("@color", template.BackgroundColor ?? "#FF1E1E1E");
                cmd.Parameters.AddWithValue("@width", template.ImageWidth.HasValue ? (object)template.ImageWidth.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@height", template.ImageHeight.HasValue ? (object)template.ImageHeight.Value : DBNull.Value);
                long id = (long)cmd.ExecuteScalar();
                return (int)id;
            }
            else
            {
                string sql = @"UPDATE templates SET name=@name, brand=@brand, model=@model,
                               image_path=@imgPath, elements_json=@json, background_image_mode=@mode, background_color=@color,
                               image_width=@width, image_height=@height
                               WHERE id=@id";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@name", template.Name);
                cmd.Parameters.AddWithValue("@brand", (object?)template.Brand ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@model", (object?)template.Model ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@imgPath", (object?)template.ImagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@json", elementsJson);
                cmd.Parameters.AddWithValue("@mode", template.BackgroundImageMode ?? "Fill");
                cmd.Parameters.AddWithValue("@color", template.BackgroundColor ?? "#FF1E1E1E");
                cmd.Parameters.AddWithValue("@width", template.ImageWidth.HasValue ? (object)template.ImageWidth.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@height", template.ImageHeight.HasValue ? (object)template.ImageHeight.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@id", template.Id);
                cmd.ExecuteNonQuery();
                return template.Id;
            }
        }

        public void DeleteTemplate(int id)
        {
            // 先获取图片路径以便删除文件
            var template = GetTemplate(id);
            if (template != null && !string.IsNullOrEmpty(template.ImagePath))
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, template.ImagePath);
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); } catch { }
                }
            }

            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "DELETE FROM templates WHERE id=@id";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ========== 配方操作 ==========
        public List<Recipe> GetRecipes()
        {
            var list = new List<Recipe>();
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT id, name, template_id FROM recipes";
            using var cmd = new SQLiteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Recipe
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    TemplateId = reader.GetInt32(2)
                });
            }
            return list;
        }

        public Recipe GetRecipe(int id)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT id, name, template_id, nodes_json, connections_json FROM recipes WHERE id=@id";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var r = new Recipe
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    TemplateId = reader.GetInt32(2)
                };
                string nodesJson = reader.IsDBNull(3) ? null : reader.GetString(3);
                string connsJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                if (!string.IsNullOrEmpty(nodesJson))
                    r.Nodes = JsonConvert.DeserializeObject<List<FlowNode>>(nodesJson) ?? new List<FlowNode>();
                if (!string.IsNullOrEmpty(connsJson))
                    r.Connections = JsonConvert.DeserializeObject<List<Connection>>(connsJson) ?? new List<Connection>();
                return r;
            }
            return null;
        }

        public int SaveRecipe(Recipe recipe)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string nodesJson = JsonConvert.SerializeObject(recipe.Nodes);
            string connsJson = JsonConvert.SerializeObject(recipe.Connections);
            if (recipe.Id == 0)
            {
                string sql = @"INSERT INTO recipes (name, template_id, nodes_json, connections_json)
                               VALUES (@name, @tid, @nodes, @conns);
                               SELECT last_insert_rowid();";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@name", recipe.Name);
                cmd.Parameters.AddWithValue("@tid", recipe.TemplateId);
                cmd.Parameters.AddWithValue("@nodes", nodesJson);
                cmd.Parameters.AddWithValue("@conns", connsJson);
                long id = (long)cmd.ExecuteScalar();
                return (int)id;
            }
            else
            {
                string sql = @"UPDATE recipes SET name=@name, template_id=@tid,
                               nodes_json=@nodes, connections_json=@conns WHERE id=@id";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@name", recipe.Name);
                cmd.Parameters.AddWithValue("@tid", recipe.TemplateId);
                cmd.Parameters.AddWithValue("@nodes", nodesJson);
                cmd.Parameters.AddWithValue("@conns", connsJson);
                cmd.Parameters.AddWithValue("@id", recipe.Id);
                cmd.ExecuteNonQuery();
                return recipe.Id;
            }
        }

        public void DeleteRecipe(int id)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "DELETE FROM recipes WHERE id=@id";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ========== 设备配置操作 ==========
        public List<DeviceConfig> GetDeviceConfigs()
        {
            var list = new List<DeviceConfig>();
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "SELECT id, deviceType, name, ipAddress, port, protocol, isEnabled, lastChecked, connectionStatus FROM device_configs";
            using var cmd = new SQLiteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new DeviceConfig
                {
                    Id = reader.GetInt32(0),
                    DeviceType = reader.GetString(1),
                    Name = reader.GetString(2),
                    IPAddress = reader.GetString(3),
                    Port = reader.GetInt32(4),
                    Protocol = reader.GetString(5),
                    IsEnabled = reader.GetInt32(6) == 1,
                    LastChecked = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
                    ConnectionStatus = reader.IsDBNull(8) ? "未知" : reader.GetString(8)
                });
            }
            return list;
        }

        public int SaveDeviceConfig(DeviceConfig config)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            if (config.Id == 0)
            {
                string sql = @"INSERT INTO device_configs (deviceType, name, ipAddress, port, protocol, isEnabled)
                               VALUES (@type, @name, @ip, @port, @proto, @enabled);
                               SELECT last_insert_rowid();";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@type", config.DeviceType);
                cmd.Parameters.AddWithValue("@name", config.Name);
                cmd.Parameters.AddWithValue("@ip", config.IPAddress);
                cmd.Parameters.AddWithValue("@port", config.Port);
                cmd.Parameters.AddWithValue("@proto", config.Protocol);
                cmd.Parameters.AddWithValue("@enabled", config.IsEnabled ? 1 : 0);
                long id = (long)cmd.ExecuteScalar();
                return (int)id;
            }
            else
            {
                string sql = @"UPDATE device_configs SET deviceType=@type, name=@name, ipAddress=@ip, port=@port,
                               protocol=@proto, isEnabled=@enabled WHERE id=@id";
                using var cmd = new SQLiteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@type", config.DeviceType);
                cmd.Parameters.AddWithValue("@name", config.Name);
                cmd.Parameters.AddWithValue("@ip", config.IPAddress);
                cmd.Parameters.AddWithValue("@port", config.Port);
                cmd.Parameters.AddWithValue("@proto", config.Protocol);
                cmd.Parameters.AddWithValue("@enabled", config.IsEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", config.Id);
                cmd.ExecuteNonQuery();
                return config.Id;
            }
        }

        public void DeleteDeviceConfig(int id)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "DELETE FROM device_configs WHERE id=@id";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDeviceStatus(int id, string status, DateTime lastChecked)
        {
            using var conn = new SQLiteConnection($"Data Source={_dbPath}");
            conn.Open();
            string sql = "UPDATE device_configs SET connectionStatus=@status, lastChecked=@checked WHERE id=@id";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@checked", lastChecked.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }
}