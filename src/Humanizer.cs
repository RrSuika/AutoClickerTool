using System;

namespace AutoClickerTool
{
    /// <summary>
    /// 拟人化引擎: 让模拟输入更接近真人操作, 降低被"脚本行为统计检测"识别的概率。
    /// 参考当前主流防检测技术实现:
    ///   - 高斯分布间隔: 真人时间间隔近似正态分布, 而非均匀随机
    ///   - 偶发犹豫停顿: 低概率插入额外停顿, 打破节拍感
    ///   - 落点漂移: 固定坐标在抖动基础上缓慢随机游走, 消除落点聚簇
    ///   - 贝塞尔移动轨迹: 平滑曲线 + 加减速曲线 + 时长符合 Fitts 定律(距离越长耗时越长), 取代瞬移
    ///   - 点击微拖: 按下与抬起之间 1~2 像素微移, 模拟真人点击的微小滑动
    /// Enabled 为总开关, 关闭后全部返回固定值, 行为与原版完全一致。
    /// </summary>
    internal static class Humanizer
    {
        // 开关由 UI 线程写、引擎线程读, 用 volatile 保证可见性(布尔读写本身原子)
        public static volatile bool Enabled = true;              // 总开关
        public static volatile bool TimingEnabled = true;        // 间隔随机化开关
        public static int TimingJitterPct = 15;         // 间隔 ±N%
        public static volatile bool PositionEnabled = true;      // 固定坐标抖动开关
        public static int PositionJitterPx = 2;         // ±N 像素
        public static volatile bool PressDurationEnabled = true; // 按键时长随机化开关
        public static volatile bool TrajectoryEnabled = true;    // 移动轨迹开关(关闭则瞬移)

        private static readonly Random Rnd = new Random();
        private static readonly object Lock = new object();

        // 落点漂移: 每次点击在抖动基础上再游走 ±1px, 缓慢累积(钳制在抖动范围 2 倍内)
        private static int _driftX;
        private static int _driftY;

        /// <summary>返回带随机抖动的间隔(毫秒), 最小 1ms。高斯分布 + 偶发犹豫停顿。</summary>
        public static int NextInterval(int baseMs)
        {
            if (baseMs <= 0) return 1;
            if (!Enabled || !TimingEnabled) return baseMs;
            lock (Lock)
            {
                int pct = Math.Max(0, Math.Min(90, TimingJitterPct));
                int min = Math.Max(1, baseMs - baseMs * pct / 100);
                int max = Math.Max(min, baseMs + baseMs * pct / 100);
                // 高斯分布: 均值 baseMs, 3σ = baseMs*pct/100, 钳制到 [min, max]
                double sd = (max - min) / 6.0;
                int v = ClampInt((int)Math.Round(Gaussian(baseMs, sd)), min, max);
                // 偶发犹豫停顿(3%), 打破节拍感
                if (Rnd.NextDouble() < 0.03) v += 60 + Rnd.Next(200);
                return v;
            }
        }

        /// <summary>按下到抬起之间的随机时长: 高斯分布 40~180ms(关闭时为固定 20ms)。真人点击几乎不会短于 40ms。</summary>
        public static int NextPressDuration()
        {
            if (!Enabled || !PressDurationEnabled) return 20;
            lock (Lock)
            {
                return ClampInt((int)Math.Round(Gaussian(85, 25)), 40, 180);
            }
        }

        /// <summary>固定坐标 X 抖动 + 缓慢落点漂移。</summary>
        public static int JitterX(int x)
        {
            if (!Enabled || !PositionEnabled || PositionJitterPx <= 0) return x;
            lock (Lock)
            {
                _driftX = ClampInt(_driftX + Rnd.Next(-1, 2), -PositionJitterPx * 2, PositionJitterPx * 2);
                return x + Rnd.Next(-PositionJitterPx, PositionJitterPx + 1) + _driftX;
            }
        }

        /// <summary>固定坐标 Y 抖动 + 缓慢落点漂移。</summary>
        public static int JitterY(int y)
        {
            if (!Enabled || !PositionEnabled || PositionJitterPx <= 0) return y;
            lock (Lock)
            {
                _driftY = ClampInt(_driftY + Rnd.Next(-1, 2), -PositionJitterPx * 2, PositionJitterPx * 2);
                return y + Rnd.Next(-PositionJitterPx, PositionJitterPx + 1) + _driftY;
            }
        }

        /// <summary>点击微拖偏移: 按下与抬起之间 1~2 像素的微小位移。</summary>
        public static void MicroDrag(out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            if (!Enabled || !TrajectoryEnabled) return;
            lock (Lock)
            {
                dx = Rnd.Next(-2, 3);
                dy = Rnd.Next(-2, 3);
            }
        }

        /// <summary>按 Fitts 定律估算两点间移动的拟人时长(毫秒), 无噪声。</summary>
        public static int EstimateTrajectoryMs(double dist)
        {
            if (dist < 3) return 0;
            double fitts = 150 + 90 * Math.Log(1 + dist / 60, 2); // 距离越长, 每像素耗时越短但总时长更长
            return ClampInt((int)Math.Round(fitts), 120, 1200);
        }

        /// <summary>
        /// 沿贝塞尔曲线分步移动 (x0,y0) → (x1,y1)。
        /// 控制点取中点并加随机偏移(曲率噪声); 速度曲线用 smoothstep 加减速; 步进间隔带微抖动。
        /// budgetMs &gt; 0 时按预算分配总时长(宏回放), 否则按 Fitts 定律自适应; 预算过小或距离过短则直接一步到位。
        /// </summary>
        public static void Trajectory(int x0, int y0, int x1, int y1, int budgetMs, Action<int, int> step)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (!Enabled || !TrajectoryEnabled || dist < 3 || (budgetMs > 0 && budgetMs <= 30))
            {
                step(x1, y1);
                return;
            }

            int total = EstimateTrajectoryMs(dist);
            if (budgetMs > 0) total = Math.Min(total, budgetMs);
            if (total <= 30) { step(x1, y1); return; }

            int steps = ClampInt((int)(dist / 30), 4, 45);
            double off;
            double cx;
            double cy;
            lock (Lock)
            {
                off = dist / 4;
                total = (int)(total * (0.85 + Rnd.NextDouble() * 0.3));
                total = Math.Max(30, total);
                // 贝塞尔控制点: 中点 + 随机偏移(曲率噪声, 避免每次轨迹形状一致)
                cx = (x0 + x1) / 2.0 + (off > 0 ? (Rnd.NextDouble() - 0.5) * off : 0);
                cy = (y0 + y1) / 2.0 + (off > 0 ? (Rnd.NextDouble() - 0.5) * off : 0);
            }

            int per = total / steps;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double e = t * t * (3 - 2 * t); // smoothstep 加减速曲线
                double it = 1 - e;
                double bx = it * it * x0 + 2 * it * e * cx + e * e * x1;
                double by = it * it * y0 + 2 * it * e * cy + e * e * y1;
                step((int)Math.Round(bx), (int)Math.Round(by));
                if (i < steps) System.Threading.Thread.Sleep(Math.Max(1, per));
            }
        }

        // ---------- 工具 ----------

        private static double Gaussian(double mean, double sd)
        {
            double u1 = 1.0 - Rnd.NextDouble(); // (0,1]
            double u2 = Rnd.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + z * sd;
        }

        private static int ClampInt(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(max, v));
        }
    }
}
