using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using GamingControlCenter.Provider;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace GamingControlCenter;

/// <summary>
/// 与 JiaoLongCCUBridge 桥接服务（127.0.0.1:13688，与官方移植版 UI 同一后端）的 MQTT 客户端。
/// 控制指令发 JiaoLong/HomeControl，状态回读 JiaoLong/HomeStatus（retained，连接即收）。
/// 本机固件无效的通道（温度墙 / 风扇锁速 / CPU·GPU 限频）照常发送——桥接侧仅 acknowledge（no-op），
/// UI 选项保留但实际不生效（与移植技术报告 §14.6 实测结论一致）。
/// </summary>
public static class JiaoLongBridge
{
	private static MqttClient? _client;
	private static Timer? _reconnect;
	private static bool _started;

	/// <summary>桥接回发 HomeStatus 时触发（MQTT 线程回调，参数为 JSON 根元素）。</summary>
	public static event Action<JsonElement>? HomeStatusChanged;

	public static bool Connected => _client?.IsConnected == true;

	public static void Start()
	{
		if (_started) return;
		_started = true;
		_reconnect = new Timer(delegate
		{
			EnsureConnected();
		}, null, 500, 5000);
		EnsureConnected();
	}

	private static void EnsureConnected()
	{
		try
		{
			if (_client?.IsConnected == true) return;
			MqttClient mqttClient = new MqttClient("127.0.0.1", 13688, false, null, null, MqttSslProtocols.None);
			_client = mqttClient;
			mqttClient.MqttMsgPublishReceived += delegate(object sender, MqttMsgPublishEventArgs e)
			{
				try
				{
					if (e.Topic == "JiaoLong/HomeStatus")
					{
						HomeStatusChanged?.Invoke(JsonDocument.Parse(Encoding.UTF8.GetString(e.Message)).RootElement.Clone());
					}
				}
				catch
				{
				}
			};
			mqttClient.Connect("GCC_" + Guid.NewGuid().ToString("N").Substring(0, 8), "UWPClient_User_5", "UWPClient_Pwd888881772688_5", cleanSession: false, keepAlivePeriod: 60);
			mqttClient.Subscribe(new string[1] { "JiaoLong/HomeStatus" }, new byte[1] { 1 });
			mqttClient.Publish("JiaoLong/HomeControl", Encoding.UTF8.GetBytes("{\"Action\":\"GETSTATUS\"}"), 1, retain: false);
			LogHelper.WriteLog("JiaoLongBridge", "connected + subscribed");
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("JiaoLongBridge", "connect: " + ex.Message);
		}
	}

	/// <summary>发布控制指令。extra 以逗号开头（如 ",\"Watts\":85"），无 extra 时仅 Action。</summary>
	public static void Send(string action, string extra = "")
	{
		try
		{
			if (_client?.IsConnected != true) EnsureConnected();
			_client?.Publish("JiaoLong/HomeControl", Encoding.UTF8.GetBytes("{\"Action\":\"" + action + "\"" + extra + "}"), 1, retain: false);
			LogHelper.WriteLog("JiaoLongBridge.Send", action + extra);
		}
		catch (Exception ex)
		{
			LogHelper.WriteLog("JiaoLongBridge.Send", action + ": " + ex.Message);
		}
	}
}
