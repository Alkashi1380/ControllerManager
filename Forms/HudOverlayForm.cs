namespace ControllerManager
{
    public class HudOverlayForm : Form
    {
        private string _profileText = "Jeux / Natif";
        private string _batteryText = "100%";
        private string _modeText = "PS5 Direct";
        private Color _accentColor = Color.FromArgb(59, 130, 246);

        public HudOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(280, 54);
            BackColor = Color.FromArgb(14, 14, 18);
            Opacity = 0.88;
            DoubleBuffered = true;

            var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(screen.Right - Width - 20, 20);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020 | 0x00000008 | 0x00000080 | 0x08000000;
                return cp;
            }
        }

        public void UpdateHud(string profile, string battery, string mode, Color accent)
        {
            _profileText = profile;
            _batteryText = battery;
            _modeText = mode;
            _accentColor = accent;
            if (IsHandleCreated && Visible)
            {
                BeginInvoke(new Action(Invalidate));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var borderPen = new Pen(_accentColor, 2f);
            g.DrawRectangle(borderPen, 1, 1, Width - 2, Height - 2);

            using var barBrush = new SolidBrush(_accentColor);
            g.FillRectangle(barBrush, 0, 0, 6, Height);

            using var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var subFont = new Font("Consolas", 8.5f, FontStyle.Regular);
            using var whiteBrush = new SolidBrush(Color.White);
            using var mutedBrush = new SolidBrush(Color.FromArgb(165, 175, 190));

            g.DrawString($"🎮 {_profileText}", titleFont, whiteBrush, new PointF(14, 7));
            g.DrawString($"🔋 {_batteryText}  •  {_modeText}", subFont, mutedBrush, new PointF(14, 29));
        }
    }
}
