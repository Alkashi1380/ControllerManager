namespace ControllerManager
{
    // « Lancer X avec quel profil manette ? » — pilotable à la souris, au clavier et à la manette
    // (croix haut/bas, A/Croix = lancer, B/Rond = annuler, X/Carré = mémoriser). Sans réponse : profil par défaut.
    public sealed class ProfileChooserForm : Form
    {
        public const int TimeoutSeconds = 20;

        // null = ne pas changer de profil ; Choice.Cancelled = ne pas lancer
        public sealed record Choice(string? ProfileId, bool Remember, bool Cancelled);

        private readonly ListBox _list = new();
        private readonly CheckBox _remember = new();
        private readonly Label _countdown = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
        private readonly List<string?> _ids = new();
        private readonly Action<Choice> _onChosen;
        private int _secondsLeft = TimeoutSeconds;
        private bool _done;

        public ProfileChooserForm(string appName, IReadOnlyList<GamepadProfile> profiles, string? preselectedId, Action<Choice> onChosen)
        {
            _onChosen = onChosen;

            Text = "Profil manette";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.FromArgb(17, 17, 23);
            ForeColor = Color.White;
            Size = new Size(460, 400);
            Padding = new Padding(1);
            Font = new Font("Segoe UI", 10f);

            var title = new Label
            {
                Text = $"🎮 Lancer « {appName} »",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(16, 12, 16, 0)
            };
            var subtitle = new Label
            {
                Text = "Avec quel profil manette ?",
                ForeColor = Color.FromArgb(148, 163, 184),
                Dock = DockStyle.Top,
                Height = 26,
                Padding = new Padding(16, 2, 16, 0)
            };

            _list.Dock = DockStyle.Fill;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Color.FromArgb(26, 26, 34);
            _list.ForeColor = Color.White;
            _list.Font = new Font("Segoe UI", 11f);
            _list.ItemHeight = 30;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.DrawItem += DrawItem;
            _list.DoubleClick += (_, _) => Confirm();

            _ids.Add(null);
            _list.Items.Add("↩️  Garder le profil actuel");
            foreach (var p in profiles)
            {
                _ids.Add(p.id);
                _list.Items.Add($"🎮  {p.name}");
            }
            int pre = preselectedId == null ? 0 : _ids.IndexOf(preselectedId);
            _list.SelectedIndex = pre >= 0 ? pre : 0;

            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 16, 6) };
            listHost.Controls.Add(_list);

            _remember.Text = "Mémoriser ce choix pour ce raccourci  (X / Carré)";
            _remember.ForeColor = Color.FromArgb(203, 213, 225);
            _remember.Dock = DockStyle.Top;
            _remember.Height = 28;
            _remember.Padding = new Padding(16, 0, 0, 0);

            _countdown.ForeColor = Color.FromArgb(100, 116, 139);
            _countdown.Dock = DockStyle.Fill;
            _countdown.TextAlign = ContentAlignment.MiddleLeft;
            _countdown.Font = new Font("Segoe UI", 8.5f);

            var launch = MakeButton("Lancer  (A / ✕)", Color.FromArgb(59, 130, 246));
            launch.Click += (_, _) => Confirm();
            var cancel = MakeButton("Annuler  (B / ○)", Color.FromArgb(55, 55, 66));
            cancel.Click += (_, _) => Finish(new Choice(null, false, Cancelled: true));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
            buttons.Controls.Add(launch);
            buttons.Controls.Add(cancel);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(16, 8, 16, 10) };
            footer.Controls.Add(_countdown);
            footer.Controls.Add(buttons);

            // Ordre d'ajout : Fill en premier, puis les éléments ancrés (WinForms les empile en sens inverse)
            Controls.Add(listHost);
            Controls.Add(_remember);
            Controls.Add(subtitle);
            Controls.Add(title);
            Controls.Add(footer);
            _remember.BringToFront();
            listHost.BringToFront();

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Confirm(); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) { Finish(new Choice(null, false, Cancelled: true)); e.Handled = true; }
            };

            _timer.Tick += (_, _) =>
            {
                _secondsLeft--;
                UpdateCountdown();
                // Sans réponse : lancement avec le profil par défaut du raccourci (repli), pas d'annulation
                if (_secondsLeft <= 0) Finish(new Choice(preselectedId, false, Cancelled: false));
            };
            UpdateCountdown();
            _timer.Start();
            Shown += (_, _) => { Activate(); _list.Focus(); };
        }

        // --- Pilotage depuis la manette (appelé sur le thread UI) ---
        public void MoveSelection(int delta)
        {
            int next = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
            _list.SelectedIndex = next;
            ResetCountdown();
        }

        public void ToggleRemember()
        {
            _remember.Checked = !_remember.Checked;
            ResetCountdown();
        }

        public void Confirm() => Finish(new Choice(_ids[Math.Max(0, _list.SelectedIndex)], _remember.Checked, Cancelled: false));

        public void CancelChoice() => Finish(new Choice(null, false, Cancelled: true));

        private void ResetCountdown()
        {
            _secondsLeft = TimeoutSeconds;
            UpdateCountdown();
        }

        private void UpdateCountdown() =>
            _countdown.Text = $"Lancement automatique dans {_secondsLeft} s";

        private void Finish(Choice choice)
        {
            if (_done) return;
            _done = true;
            _timer.Stop();
            Close();
            _onChosen(choice);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Dispose();
            // Fermeture par Alt+F4 : équivaut à « Annuler »
            if (!_done) { _done = true; _onChosen(new Choice(null, false, Cancelled: true)); }
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var border = new Pen(Color.FromArgb(59, 130, 246), 1);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        private void DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var bg = new SolidBrush(selected ? Color.FromArgb(37, 64, 120) : Color.FromArgb(26, 26, 34));
            e.Graphics.FillRectangle(bg, e.Bounds);
            if (selected)
            {
                using var bar = new SolidBrush(Color.FromArgb(59, 130, 246));
                e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
            }
            TextRenderer.DrawText(e.Graphics, _list.Items[e.Index]?.ToString(), _list.Font,
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private static Button MakeButton(string text, Color color) => new()
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(10, 3, 10, 3),
            FlatAppearance = { BorderSize = 0 }
        };
    }
}
