using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;

// 协议联调驱动：
//   TestClient send <topic> <json>        发一条消息
//   TestClient batterytest               电池页三项修复的端到端自检（前半）
//   TestClient batterytest2              电池页三项修复的端到端自检（后半：还原状态）
//   TestClient hometest                  主页控制项（JiaoLong/Home*）协议自检
var mode = args.Length > 0 ? args[0] : "";

var factory = new MqttFactory();
var client = factory.CreateMqttClient();
var received = new List<(string Topic, string Payload)>();
var options = new MqttClientOptionsBuilder()
    .WithTcpServer("127.0.0.1", 13688)
    .WithCredentials("UWPClient_User_5", "UWPClient_Pwd888881772688_5")
    .WithCleanSession(true)
    .Build();

client.ApplicationMessageReceivedAsync += e =>
{
    received.Add((e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)));
    return Task.CompletedTask;
};

await client.ConnectAsync(options);
await client.SubscribeAsync(new MqttClientSubscribeOptions
{
    TopicFilters = { new MqttTopicFilter { Topic = "Setting/Status" }, new MqttTopicFilter { Topic = "System/BatteryProtection" }, new MqttTopicFilter { Topic = "JiaoLong/HomeStatus" } }
});

async Task Send(string topic, string json)
{
    await client.PublishAsync(new MqttApplicationMessage
    {
        Topic = topic,
        PayloadSegment = Encoding.UTF8.GetBytes(json),
        QualityOfServiceLevel = MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce
    });
    await Task.Delay(800);
}

if (mode == "send" && args.Length >= 3)
{
    await Send(args[1], args[2]);
    Console.WriteLine("已发送。收到：");
    foreach (var (t, p) in received) Console.WriteLine($"  {t} {p}");
}
else if (mode == "batterytest")
{
    // 1) 电池模式：切工作站模式 → GET 应返回 2（持久化，不再跳回 1）
    await Send("BatteryProtection/Control", """{"Action":"HEALTHYMODE"}""");
    await Send("BatteryProtection/Control", """{"Report":"GET"}""");
    var getStatus = received.LastOrDefault(r => r.Topic == "System/BatteryProtection").Payload;
    var health = JsonDocument.Parse(getStatus).RootElement.GetProperty("HealthProtectionStatus").GetInt32();
    Console.WriteLine($"[1] HEALTHYMODE 后 GET HealthProtectionStatus = {health} (期望 2) => {(health == 2 ? "PASS" : "FAIL")}");

    received.Clear();
    // 2) 高性能电源模式 OFF → 平衡计划
    await Send("Setting/Control", """{"Action":"HIGHPERFORMANCEPOWERMODE_OFF"}""");
    var ss = received.LastOrDefault(r => r.Topic == "Setting/Status").Payload;
    var hp = JsonDocument.Parse(ss).RootElement.GetProperty("HighPerformancePowerModeSwitch").GetString();
    Console.WriteLine($"[2] HIGHPERFORMANCEPOWERMODE_OFF 后 Setting/Status.HighPerformancePowerModeSwitch = {hp} (期望 OFF) => {(hp == "OFF" ? "PASS" : "FAIL")}");
}
else if (mode == "batterytest2")
{
    // 3) 高性能电源模式 ON → 高性能计划（用户机器原始状态）
    await Send("Setting/Control", """{"Action":"HIGHPERFORMANCEPOWERMODE_ON"}""");
    var ss = received.LastOrDefault(r => r.Topic == "Setting/Status").Payload;
    var hp = JsonDocument.Parse(ss).RootElement.GetProperty("HighPerformancePowerModeSwitch").GetString();
    Console.WriteLine($"[3] HIGHPERFORMANCEPOWERMODE_ON 后 Setting/Status.HighPerformancePowerModeSwitch = {hp} (期望 ON) => {(hp == "ON" ? "PASS" : "FAIL")}");
    // 4) 电池模式切回平衡（还原用户上次的状态）
    received.Clear();
    await Send("BatteryProtection/Control", """{"Action":"BALANCEDMODE"}""");
    await Send("BatteryProtection/Control", """{"Report":"GET"}""");
    var getStatus = received.LastOrDefault(r => r.Topic == "System/BatteryProtection").Payload;
    var health = JsonDocument.Parse(getStatus).RootElement.GetProperty("HealthProtectionStatus").GetInt32();
    Console.WriteLine($"[4] BALANCEDMODE 后 GET HealthProtectionStatus = {health} (期望 1，还原) => {(health == 1 ? "PASS" : "FAIL")}");
}
else if (mode == "hometest")
{
    // 主页控制项协议自检：GETSTATUS → 睿频关/开（还原）→ SPL 幂等写 → GPU OC 关（幂等）
    await Send("JiaoLong/HomeControl", """{"Action":"GETSTATUS"}""");
    var st = received.LastOrDefault(r => r.Topic == "JiaoLong/HomeStatus").Payload;
    Console.WriteLine($"[0] GETSTATUS => {st}");
    if (st == null) { Console.WriteLine("FAIL: 无 HomeStatus 回发（服务未更新？）"); return; }
    var doc = JsonDocument.Parse(st).RootElement;
    bool turboOff0 = doc.GetProperty("TurboOff").GetBoolean();
    Console.WriteLine($"    初始 TurboOff={turboOff0} Spl={doc.GetProperty("Spl")} Sppt={doc.GetProperty("Sppt")} GpuLockSupported={doc.GetProperty("GpuLockSupported")}");

    await Send("JiaoLong/HomeControl", """{"Action":"TURBO","On":false}""");
    var st2 = JsonDocument.Parse(received.Last(r => r.Topic == "JiaoLong/HomeStatus").Payload).RootElement;
    bool turboOff = st2.GetProperty("TurboOff").GetBoolean();
    Console.WriteLine($"[1] TURBO off => TurboOff={turboOff} (期望 True) => {(turboOff ? "PASS" : "FAIL")}");

    await Send("JiaoLong/HomeControl", """{"Action":"TURBO","On":true}""");
    var st3 = JsonDocument.Parse(received.Last(r => r.Topic == "JiaoLong/HomeStatus").Payload).RootElement;
    bool turboBack = st3.GetProperty("TurboOff").GetBoolean();
    Console.WriteLine($"[2] TURBO on(还原) => TurboOff={turboBack} (期望 False) => {(!turboBack ? "PASS" : "FAIL")}");

    int spl = doc.GetProperty("Spl").GetInt32();
    await Send("JiaoLong/HomeControl", $"{{\"Action\":\"SPL\",\"Watts\":{spl}}}");
    var st4 = JsonDocument.Parse(received.Last(r => r.Topic == "JiaoLong/HomeStatus").Payload).RootElement;
    Console.WriteLine($"[3] SPL 幂等写 {spl}W => 回显 {st4.GetProperty("Spl")} => {(st4.GetProperty("Spl").GetInt32() == spl ? "PASS" : "FAIL")}");

    await Send("JiaoLong/HomeControl", """{"Action":"GPU_OC","On":false}""");
    var st5 = JsonDocument.Parse(received.Last(r => r.Topic == "JiaoLong/HomeStatus").Payload).RootElement;
    Console.WriteLine($"[4] GPU_OC off => GpuOcOn={st5.GetProperty("GpuOcOn")} (期望 False) => {(!st5.GetProperty("GpuOcOn").GetBoolean() ? "PASS" : "FAIL")}");
}
else
{
    Console.WriteLine("用法: TestClient send <topic> <json> | batterytest | batterytest2 | hometest");
}

await client.DisconnectAsync();
