using System.Text;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace JiaoLongCCUBridge;

/// <summary>
/// 内嵌 MQTT broker：127.0.0.1:13688，与官方 GCUBridge 行为一致。
/// UI（UWPClient_5）发布 QoS2、订阅 QoS0，MQTTnet 自动处理 QoS 协商。
/// </summary>
public sealed class MqttHost
{
    private MqttServer? _server;
    private readonly ProtocolHandlers _handlers;

    public MqttHost(ProtocolHandlers handlers)
    {
        _handlers = handlers;
        _handlers.PublishRequested += OnPublishRequested;
    }

    private Task OnPublishRequested(string topic, string json, bool retain)
    {
        Inject(topic, json, retain);
        return Task.CompletedTask;
    }

    public void Inject(string topic, string json, bool retain = false)
    {
        var server = _server;
        if (server == null) return;
        var msg = new MqttApplicationMessage
        {
            Topic = topic,
            PayloadSegment = Encoding.UTF8.GetBytes(json),
            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
            Retain = retain,
        };
        _ = server.InjectApplicationMessage(new InjectedMqttApplicationMessage(msg));
    }

    public async Task StartAsync()
    {
        var factory = new MqttFactory();
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Loopback)
            .WithDefaultEndpointPort(ProtocolHandlers.BrokerPort)
            .Build();
        _server = factory.CreateMqttServer(options);

        _server.InterceptingPublishAsync += args =>
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = args.ApplicationMessage.PayloadSegment.Array != null
                ? args.ApplicationMessage.PayloadSegment.ToArray()
                : Array.Empty<byte>();
            if (topic != null && payload.Length > 0)
            {
                _handlers.HandleMessage(topic, payload);
            }
            return Task.CompletedTask;
        };

        await _server.StartAsync();
        Log.Info($"MQTT broker listening on {ProtocolHandlers.BrokerHost}:{ProtocolHandlers.BrokerPort}");
    }

    public async Task StopAsync()
    {
        if (_server != null)
        {
            await _server.StopAsync();
        }
    }
}
