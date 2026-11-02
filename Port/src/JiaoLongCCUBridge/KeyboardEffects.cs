using System.Diagnostics;

namespace JiaoLongCCUBridge;

/// <summary>
/// 键盘灯效引擎（软件动画）。
///
/// 硬件事实（2026-11-02 实测，见 docs\键盘灯逆向分析.md）：
/// - 固件只有 fn16 模式（1=流光 2=固定色，0=关被忽略）、fn17 单色 RGB（3 字节，12 字节四分区无效）、
///   fn18 亮度 0-3（0 才是有效关灯）；四分区独立配色不可用。
/// - 因此全部动态效果都由软件按帧写 fn17 实现：把键盘固定为"固定色"模式，
///   以 ~25fps 改写颜色/明暗，模拟呼吸、波浪、彩虹、混色、闪烁。
///
/// 参数（来自 UI 的 SetEffectALL）：
/// - effect：Single/Breathing/Wave/Rainbow/Mix/Flash（厂商 UI 对 FourZone 机型只开这 6 个）
/// - speed：0-4（越大越快）
/// - light：0-3（硬件亮度，直接写 fn18）
/// - direction：None/LeftRight/RightLeft/UpDown/DownUp（影响相位方向）
/// - color.ColorBuffer[]：调色板，首位为主色
/// </summary>
public sealed class KeyboardEffects
{
    private readonly Mifs _mifs;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    // 当前参数（volatile：调参时原地生效，不重启线程）
    private volatile Params _params = Params.Default;
    private volatile string _currentEffect = "";
    private bool _modePrimed;          // 是否已切到 fn16=2 并完成 500ms 时序

    public string CurrentEffect => _currentEffect;

    /// <summary>当前是否为静态效果（无循环在跑），用于状态回读时决定要不要同步颜色。</summary>
    public bool IsStatic => _loop == null || _loop.IsCompleted;

    public KeyboardEffects(Mifs mifs) => _mifs = mifs;

    public sealed record Params(
        string Effect,
        (byte r, byte g, byte b) Primary,
        (byte r, byte g, byte b)[] Palette,
        int Speed,          // 0-4
        int Brightness,     // 0-3
        string Direction)
    {
        public static readonly Params Default = new("Single", (255, 0, 114), new[] { ((byte)255, (byte)0, (byte)114) }, 2, 3, "None");
    }

    /// <summary>每帧间隔（ms）：速度档 0..4 → 60/50/40/33/25ms。</summary>
    private static int FrameMs(int speed) => speed switch
    {
        <= 0 => 60,
        1 => 50,
        2 => 40,
        3 => 33,
        _ => 25,
    };

    /// <summary>速度档 → 周期倍率（1=最慢）。用于呼吸/闪烁/波浪的整周期时长。</summary>
    private static double PeriodScale(int speed) => speed switch
    {
        <= 0 => 2.2,
        1 => 1.6,
        2 => 1.15,
        3 => 0.85,
        _ => 0.6,
    };

    /// <summary>支持的动态效果名（大小写不敏感）；不在表内的按静态单色处理，避免空转循环。</summary>
    private static readonly HashSet<string> DynamicEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "Breathing", "Wave", "Rainbow", "Mix", "Flash", "Twinkling",
        "Marquee", "Aurora", "Ripple", "Raindrop", "Spark", "Impact",
        "Reactive", "Music", "Thinking", "Devour", "Gaming", "Sine",
        "Interlace", "Diagonal", "ColorfulWave", "ColorMarquee", "Dawn",
    };

    /// <summary>应用/更新灯效。相同参数重复调用无副作用。</summary>
    public void Apply(Params p)
    {
        lock (_lock)
        {
            bool dynamic = DynamicEffects.Contains(p.Effect);
            bool same = _currentEffect == p.Effect && _loop != null && !_loop.IsCompleted;
            _params = p;
            _currentEffect = p.Effect;

            // 静态模式（Single/Manual/UserMode/未知）：直接写一次颜色，不跑循环
            if (!dynamic)
            {
                StopLoopInternal();
                SafeWrite(() =>
                {
                    EnsureFixedMode();
                    _mifs.SetKeyboardColor(p.Primary.r, p.Primary.g, p.Primary.b);
                });
                return;
            }

            if (same) return;   // 动态效果：参数已被循环下一帧读取，无需重启

            StopLoopInternal();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token));
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            _currentEffect = "";
            StopLoopInternal();
        }
    }

    private void StopLoopInternal()
    {
        try { _cts?.Cancel(); } catch { }
        _loop = null;
    }

    /// <summary>切到固件固定色模式（fn16=2）。首次或从别的模式切换时等 500ms，避免颜色被吞。</summary>
    private void EnsureFixedMode()
    {
        if (_modePrimed) return;
        _mifs.SetKeyboardMode(2);
        Thread.Sleep(500);
        _modePrimed = true;
    }

    /// <summary>外部改了亮度/模式后通知引擎重新对齐固件模式。</summary>
    public void InvalidateMode() => _modePrimed = false;

    private void SafeWrite(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Info("kbd fx write failed: " + ex.Message); }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            SafeWrite(EnsureFixedMode);
            while (!token.IsCancellationRequested)
            {
                var p = _params;
                var (r, g, b) = ComputeFrame(p, sw.Elapsed.TotalSeconds);
                SafeWrite(() => _mifs.SetKeyboardColor(r, g, b));
                try { await Task.Delay(FrameMs(p.Speed), token); }
                catch (TaskCanceledException) { break; }
            }
        }
        catch (Exception ex)
        {
            Log.Info("kbd fx loop stopped: " + ex.Message);
        }
    }

    /// <summary>按效果计算当前帧颜色（纯函数，便于单测/复核）。</summary>
    public static (byte r, byte g, byte b) ComputeFrame(Params p, double t)
    {
        double scale = PeriodScale(p.Speed);
        bool backward = p.Direction is "RightLeft" or "UpDown";
        double dir = backward ? -1 : 1;
        var prim = p.Primary;

        switch (p.Effect.ToLowerInvariant())
        {
            case "breathing":
            {
                // 连续正弦呼吸：0.12 ↔ 1.0，周期 3.0s × 速度倍率
                double period = 3.0 * scale;
                double k = 0.12 + 0.88 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period));
                return Scale(prim, k);
            }

            case "wave":
            {
                // 波浪：色相在基色 ±75° 之间往复扫动（单分区键盘用整体色相移动表达"波"），
                // 同时叠加轻微亮度起伏，观感更接近"涌动"。
                var (h, s, v) = RgbToHsb(prim);
                double period = 4.0 * scale;
                double phase = Math.Sin(2 * Math.PI * t / period * dir);
                double hue = (h + 75 * phase + 360) % 360;
                double amp = 0.80 + 0.20 * Math.Sin(2 * Math.PI * t / period * 2 * dir);
                return HsbToRgb(hue, s, v * amp);
            }

            case "rainbow":
            {
                // 彩虹：色相 360° 连续流转，3.6s 一圈 × 速度倍率
                double period = 3.6 * scale;
                double hue = (t / period * 360 * dir) % 360;
                return HsbToRgb((hue + 360) % 360, 1.0, 1.0);
            }

            case "mix":
            {
                // 混色：在调色板各色间平滑往返混合；单色时与补色混合
                var pal = p.Palette is { Length: >= 2 }
                    ? p.Palette
                    : new[] { prim, HsbToRgb((RgbToHsb(prim).h + 150) % 360, 1.0, 1.0) };
                double period = 5.0 * scale;
                double u = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period * dir); // 0..1..0
                double pos = u * (pal.Length - 1);
                int i0 = (int)Math.Floor(pos);
                int i1 = Math.Min(i0 + 1, pal.Length - 1);
                double f = pos - i0;
                return Blend(pal[i0], pal[i1], f);
            }

            case "flash":
            {
                // 闪烁：硬开关方波，亮 0.45 周期（周期 1.6s × 速度倍率）
                double period = 1.6 * scale;
                double phase = (t % period) / period;
                return phase < 0.45 ? prim : ((byte)0, (byte)0, (byte)0);
            }

            case "twinkling":
            {
                // 闪烁星光：伪随机亮度跳变（4 档）
                var rnd = new Random((int)(t / (0.12 * scale)));
                double k = 0.25 + 0.75 * rnd.NextDouble();
                return Scale(prim, k);
            }

            default:
                return prim;
        }
    }

    private static (byte, byte, byte) Scale((byte r, byte g, byte b) c, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return ((byte)Math.Round(c.r * k), (byte)Math.Round(c.g * k), (byte)Math.Round(c.b * k));
    }

    private static (byte, byte, byte) Blend((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return ((byte)Math.Round(a.r + (b.r - a.r) * f),
                (byte)Math.Round(a.g + (b.g - a.g) * f),
                (byte)Math.Round(a.b + (b.b - a.b) * f));
    }

    /// <summary>HSB→RGB（h:0-360, s/v:0-1）。</summary>
    public static (byte r, byte g, byte b) HsbToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
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

    /// <summary>RGB→HSB（h:0-360, s/v:0-1）。</summary>
    public static (double h, double s, double v) RgbToHsb((byte r, byte g, byte b) c)
    {
        double r = c.r / 255.0, g = c.g / 255.0, b = c.b / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 1e-6)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        double s = max <= 0 ? 0 : d / max;
        return ((h + 360) % 360, s, max);
    }

    public static (byte r, byte g, byte b) HexToRgb(string hex)
    {
        try { return (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)); }
        catch { return (255, 255, 255); }
    }

    public static string RgbToHex((byte r, byte g, byte b) c) => $"{c.r:X2}{c.g:X2}{c.b:X2}";
}
