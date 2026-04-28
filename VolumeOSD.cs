using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace HTPCAVRVolume
{
    /// <summary>
    /// Compact volume overlay — top-right corner.
    /// Layout (top to bottom): [vertical bar | number] then [speaker icon].
    /// Denon's native range (e.g. 10–70) is mapped to a 0–100 display scale:
    ///   below display-10 → half-step resolution (0, 0.5, 1.0 … 9.5, 10)
    ///   10 and above    → integer steps (10, 11, 12 … 100)
    /// </summary>
    internal class VolumeOSD : Form
    {
        private float _displayLevel = 0f;

        private readonly System.Windows.Forms.Timer _holdTimer;
        private readonly System.Windows.Forms.Timer _fadeTimer;

        private const int    CornerRadius  = 8;
        private const double TargetOpacity = 0.92;

        // Layout — all in pixels
        private const int PadTop   = 11;
        private const int PadSides = 12;
        private const int BarW     = 7;
        private const int BarH     = 24;   // ≈ cap-height of the 14 pt number
        private const int BarNumGap = 5;
        private const int RowGap   = 7;
        private const int IconH    = 13;
        private const int PadBot   = 10;

        public VolumeOSD()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost         = true;
            ShowInTaskbar   = false;
            BackColor       = Color.FromArgb(28, 28, 28);
            Opacity         = 0;
            Size            = new Size(82, PadTop + BarH + RowGap + IconH + PadBot);
            StartPosition   = FormStartPosition.Manual;

            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.UserPaint, true);

            _holdTimer = new System.Windows.Forms.Timer { Interval = 1400 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); _fadeTimer.Start(); };

            _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _fadeTimer.Tick += (s, e) =>
            {
                double next = Opacity - 0.048;
                if (next <= 0) { _fadeTimer.Stop(); Hide(); Opacity = 0; }
                else           { Opacity = next; }
            };

            ApplyRoundedRegion();

            // Force handle creation on the UI thread now, before any background
            // thread calls ShowVolume.  Without this, InvokeRequired returns false
            // when the handle doesn't exist yet, causing the form to be created on
            // the wrong thread and OnPaint to never be called (blank white window).
            var _ = Handle;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ShowVolume(float level, float volumeMin, float volumeMax)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => ShowVolume(level, volumeMin, volumeMax)));
                return;
            }

            _displayLevel = DenonToDisplay(level, volumeMin, volumeMax);

            PositionWindow();
            Invalidate();

            _fadeTimer.Stop();
            _holdTimer.Stop();
            Opacity = TargetOpacity;
            if (!Visible) Show();
            _holdTimer.Start();
        }

        // ── Positioning ───────────────────────────────────────────────────────

        private void PositionWindow()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 20, area.Top + 20);
        }

        // ── Volume conversion ─────────────────────────────────────────────────
        // Piecewise mapping from AVR native range (e.g. 10.0–70.0) onto 0–100.
        //
        // Lower zone  — first 10 AVR-units above min (e.g. 10.0–20.0):
        //   mapped 1-to-1 so each Denon 0.5 step = exactly 0.5 display units.
        //   Display values: 0.0, 0.5, 1.0, 1.5 … 9.5, 10.0  (consistent halves)
        //
        // Upper zone  — remainder (e.g. 20.0–70.0):
        //   linearly mapped onto display 10–100 and rounded to the nearest integer.
        private static float DenonToDisplay(float level, float min, float max)
        {
            if (max <= min) return 0f;

            float lowerEdge = min + 10f;   // e.g. 20.0 for a standard Denon

            float display;
            if (level <= lowerEdge)
            {
                // 1:1 — each Denon 0.5 step = 0.5 display unit, no rounding error
                display = level - min;
            }
            else
            {
                float upperSpan = max - lowerEdge;
                display = upperSpan > 0
                    ? 10f + (level - lowerEdge) / upperSpan * 90f
                    : 100f;
            }

            display = Math.Max(0f, Math.Min(100f, display));

            return display < 10f
                ? (float)(Math.Round(display * 2.0) / 2.0)  // nearest 0.5
                : (float)Math.Round(display);                 // nearest integer
        }

        // Below 10 always render as "X.X" (e.g. "3.0", "7.5") so the OSD
        // width stays constant and doesn't jump when a whole number is hit.
        private static string FormatDisplay(float d)
        {
            if (d >= 10f) return ((int)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("F1", CultureInfo.InvariantCulture);
        }

        // ── Painting ──────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = Width;

            // Background (must be explicit with UserPaint = true)
            g.Clear(BackColor);

            string volText = FormatDisplay(_displayLevel);

            // Measure text so we can centre the [bar | number] group
            float barX, textX, textY;
            using (var font = new Font("Segoe UI", 14f, FontStyle.Bold))
            using (var white = new SolidBrush(Color.White))
            {
                SizeF sz     = g.MeasureString(volText, font);
                float groupW = BarW + BarNumGap + sz.Width;
                barX  = (w - groupW) / 2f;
                textX = barX + BarW + BarNumGap;
                textY = PadTop + (BarH - sz.Height) / 2f;

                g.DrawString(volText, font, white, textX, textY);
            }

            // ── Vertical bar (fills bottom-up) ────────────────────────────────
            float barY  = PadTop;
            float pct   = Math.Max(0f, Math.Min(1f, _displayLevel / 100f));
            int   fillH = (int)(BarH * pct);

            // Track
            using (var b = new SolidBrush(Color.FromArgb(65, 255, 255, 255)))
                g.FillRectangle(b, barX, barY, BarW, BarH);

            // Fill
            if (fillH > 0)
                using (var b = new SolidBrush(Color.FromArgb(255, 110, 165, 255)))
                    g.FillRectangle(b, barX, barY + BarH - fillH, BarW, fillH);

            // ── Speaker icon (centred below the bar+number row) ───────────────
            float iconCY = PadTop + BarH + RowGap + IconH / 2f;
            DrawSpeakerIcon(g, w / 2f, iconCY);
        }

        private static void DrawSpeakerIcon(Graphics g, float cx, float cy)
        {
            // Same proportions as the original horizontal-bar OSD icon.
            const int bw   = 5;   // body rectangle width
            const int bh   = 8;   // body rectangle height
            const int cone = 5;   // how far the cone flares out on each side

            // Shift left so the whole glyph (body + cone + arcs) is centred on cx.
            // Total visual span ≈ bw + cone + 2 + 10 + 4 = 21 px; centre ≈ bw + cone/2.
            float sx = cx - bw - cone / 2f;
            float sy = cy - bh / 2f;

            // Body + cone
            using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            {
                g.FillRectangle(b, sx, sy, bw, bh);

                var pts = new[]
                {
                    new PointF(sx + bw,        sy),
                    new PointF(sx + bw + cone, cy - bh / 2f - cone),
                    new PointF(sx + bw + cone, cy + bh / 2f + cone),
                    new PointF(sx + bw,        sy + bh),
                };
                g.FillPolygon(b, pts);
            }

            // Sound-wave arcs
            using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                float x0 = sx + bw + cone + 2;
                g.DrawArc(p, x0,     cy - 4,  6,  8,  -50, 100);
                g.DrawArc(p, x0 + 4, cy - 7, 10, 14, -50, 100);
            }
        }

        // ── Window shape & style ──────────────────────────────────────────────

        private void ApplyRoundedRegion()
        {
            var path = new GraphicsPath();
            int r = CornerRadius * 2;
            path.AddArc(0,           0,            r, r, 180, 90);
            path.AddArc(Width - r,   0,            r, r, 270, 90);
            path.AddArc(Width - r,   Height - r,   r, r,   0, 90);
            path.AddArc(0,           Height - r,   r, r,  90, 90);
            path.CloseAllFigures();
            Region = new Region(path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedRegion();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE  (no focus steal)
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW  (off Alt+Tab)
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _holdTimer?.Dispose(); _fadeTimer?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
