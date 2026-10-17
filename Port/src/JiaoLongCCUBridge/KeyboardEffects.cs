using System.Diagnostics;

namespace JiaoLongCCUBridge;

/// <summary>
/// 键盘动态灯效引擎：固件只有 关灯/流光/固定色 三种模式且"流光"模式在本机无可见效果，
/// 彩虹流光等动态效果全部用软件循环实现（旧第三方控制中心同款方案：
/// 固定色模式 + 按 HSB 色相步进写 fn17 颜色）。
/// </summary>
public sealed class KeyboardEffects
{
    private readonly Mifs _mifs;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly object _lock = new();
    private volatile string _currentEffect = "";

    public string CurrentEffect => _currentEffect;

    public KeyboardEffects(Mifs mifs)
    {
        _mifs = mifs;
    }

    public void StopAll()
    {
        _currentEffect = "";
        try { _cts?.Cancel(); } catch { }
        _loop = null;
    }

    /// <summary>启动彩虹流光。speed 为 UI 速度值（1-4，越大越快）。</summary>
    public void StartRainbowFlow(string speed)
    {
        StartLoop("rainbow", () =>
        {
            int step = speed switch { "4" => 8, "3" => 5, "2" => 4, _ => 3 };
            int interval = 50;
            return (step, interval);
        }, hueOnly: true);
    }

    /// <summary>呼吸：亮度 1↔3 渐变（静态色）。</summary>
    public void StartBreathing(string baseColorHex)
    {
        var (r, g, b) = HexToRgb(baseColorHex);
        StartLoop("breathing", () => (1, 300), hueOnly: false, fixedColor: (r, g, b));
    }

    /// <summary>静态效果（Single/固定色等）：停止循环。</summary>
    public void StopEffects()
    {
        StopAll();
    }

    private void StartLoop(string effect, Func<(int step, int interval)> paramsFn, bool hueOnly, (byte r, byte g, byte b)? fixedColor = null)
    {
        lock (_lock)
        {
            if (_currentEffect == effect && _loop != null && !_loop.IsCompleted)
            {
                _currentEffect = effect; // 已在运行
                return;
            }
            StopAll();
            _currentEffect = effect;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(async () =>
            {
                double hue = 0;
                byte bright = 3;
                try
                {
                    _mifs.SetKeyboardMode(2); // 固定色模式
                    while (!token.IsCancellationRequested)
                    {
                        var (step, interval) = paramsFn();
                        if (hueOnly)
                        {
                            hue = (hue + step) % 360;
                            var (r, g, b) = HsbToRgb(hue, 1.0, 1.0);
                            _mifs.SetKeyboardColor(r, g, b);
                        }
                        else
                        {
                            // 呼吸：亮度 3→1→3 三角波
                            bright = bright switch { 3 => 2, 2 => 1, 1 => 2, _ => 3 };
                            _mifs.SetKeyboardBrightness(bright);
                            if (fixedColor.HasValue)
                            {
                                var c = fixedColor.Value;
                                _mifs.SetKeyboardColor(c.r, c.g, c.b);
                            }
                        }
                        await Task.Delay(interval, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch { /* 采样失败下一轮重试 */ }
            }, token);
        }
    }

    /// <summary>HSB→RGB（旧程序 ColorConversionHelper 同款算法，饱和度/亮度=1）。</summary>
    public static (byte r, byte g, byte b) HsbToRgb(double h, double s, double v)
    {
        h = h % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (byte r, byte g, byte b) HexToRgb(string hex)
    {
        try { return (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)); }
        catch { return (255, 255, 255); }
    }
}
