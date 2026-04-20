using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RobotControlSystem.Services
{
    public class AgvHttpService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _defaultVehicleName;

        public AgvHttpService(string baseUrl, string? vehicleName = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _defaultVehicleName = vehicleName;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<(bool success, string msg)> LockAsync(string? vehicleName = null)
        {
            var body = new JObject { ["vehicles"] = new JArray(ResolveVehicleName(vehicleName)) };
            return await PostAsync("/lock", body);
        }

        public async Task<(bool success, string msg)> UnlockAsync(string? vehicleName = null)
        {
            var body = new JObject { ["vehicles"] = new JArray(ResolveVehicleName(vehicleName)) };
            return await PostAsync("/unlock", body);
        }

        public async Task<(bool success, string msg)> ResumeAsync(string? vehicleName = null)
        {
            var body = new JObject { ["vehicles"] = new JArray(ResolveVehicleName(vehicleName)) };
            return await PostAsync("/gotoSiteResume", body);
        }

        public async Task<(bool success, string msg)> PauseAsync(string? vehicleName = null)
        {
            var body = new JObject { ["vehicles"] = new JArray(ResolveVehicleName(vehicleName)) };
            return await PostAsync("/gotoSitePause", body);
        }

        public async Task<(bool success, string msg)> SetSoftEmergencyStopAsync(bool enable, string? vehicleName = null)
        {
            var body = new JObject { ["vehicle"] = ResolveVehicleName(vehicleName), ["status"] = enable };
            return await PostAsync("/setSoftIOEMC", body);
        }

        public async Task<(bool success, string msg)> ClearCacheAsync(long? timestamp = null)
        {
            var body = new JObject
            {
                ["type"] = "orders",
                ["timestamp"] = timestamp ?? (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds
            };
            return await PostAsync("/clearCache", body);
        }

        public async Task<(bool success, string msg)> DeleteAllOrdersAsync()
        {
            return await PostAsync("/deleteAllOrders", new JObject());
        }

        public async Task<(bool success, string msg)> CreateOrderAsync(string targetSite, string? vehicleName = null, string? orderId = null, string? blockId = null, bool complete = true)
        {
            var body = new JObject
            {
                ["id"] = string.IsNullOrWhiteSpace(orderId) ? Guid.NewGuid().ToString() : orderId,
                ["vehicle"] = ResolveVehicleName(vehicleName),
                ["complete"] = complete,
                ["blocks"] = new JArray
                {
                    new JObject
                    {
                        ["blockId"] = string.IsNullOrWhiteSpace(blockId) ? "block_1" : blockId,
                        ["location"] = targetSite,
                        ["actionType"] = "GOTO_SITE"
                    }
                }
            };

            return await PostAsync("/setOrder", body);
        }

        public async Task<(bool success, string content, string msg)> GetRobotsStatusAsync(string? vehicleName = null)
        {
            try
            {
                var url = $"{_baseUrl}/robotsStatus";
                var vehicle = ResolveVehicleName(vehicleName);
                if (!string.IsNullOrWhiteSpace(vehicle))
                {
                    url += $"?vehicles={Uri.EscapeDataString(vehicle)}";
                }

                var response = await _httpClient.GetAsync(url);//获取了小车的状态，不全
                var content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return (true, content, "ok");

                return (false, content, $"HTTP {(int)response.StatusCode}: {content}");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        private string ResolveVehicleName(string? vehicleName)
        {
            var resolved = string.IsNullOrWhiteSpace(vehicleName) ? _defaultVehicleName : vehicleName;
            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException("未指定车辆名称。");

            return resolved;
        }

        private async Task<(bool success, string msg)> PostAsync(string endpoint, JObject body)
        {
            try
            {
                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_baseUrl + endpoint, content);
                var responseText = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return (false, $"HTTP {(int)response.StatusCode}: {responseText}");

                var json = JObject.Parse(responseText);
                int code = json["code"]?.Value<int>() ?? -1;
                if (code == 0)
                    return (true, json["msg"]?.Value<string>() ?? "ok");

                return (false, json["msg"]?.Value<string>() ?? $"错误码 {code}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
