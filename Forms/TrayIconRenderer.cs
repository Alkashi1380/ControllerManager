using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ControllerManager
{
    // Dessine l'icône de la barre des tâches : une manette à la couleur du profil, grisée quand les raccourcis sont en pause
    public static class TrayIconRenderer
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon Create(Color accent, bool paused)
        {
            Color body = paused ? Color.FromArgb(120, 125, 135) : accent;
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Corps arrondi + deux poignées, remplis d'une seule couleur (pas de contour qui couperait la forme)
                using (var brush = new SolidBrush(body))
                {
                    using var bodyPath = new GraphicsPath();
                    const int r = 8;
                    bodyPath.AddArc(1, 8, r * 2, r * 2, 180, 90);
                    bodyPath.AddArc(31 - r * 2, 8, r * 2, r * 2, 270, 90);
                    bodyPath.AddArc(31 - r * 2, 24 - r * 2, r * 2, r * 2, 0, 90);
                    bodyPath.AddArc(1, 24 - r * 2, r * 2, r * 2, 90, 90);
                    bodyPath.CloseFigure();
                    g.FillPath(brush, bodyPath);
                    g.FillEllipse(brush, 1, 14, 11, 16);
                    g.FillEllipse(brush, 20, 14, 11, 16);
                }

                // Croix directionnelle et boutons
                using var white = new SolidBrush(Color.White);
                g.FillRectangle(white, 6, 15, 6, 2);
                g.FillRectangle(white, 8, 13, 2, 6);
                g.FillEllipse(white, 21, 12, 3, 3);
                g.FillEllipse(white, 24, 15, 3, 3);
                g.FillEllipse(white, 18, 15, 3, 3);
                g.FillEllipse(white, 21, 18, 3, 3);

                if (paused)
                {
                    // Barre de pause
                    using var pauseBrush = new SolidBrush(Color.FromArgb(239, 68, 68));
                    g.FillRectangle(pauseBrush, 12, 0, 3, 8);
                    g.FillRectangle(pauseBrush, 17, 0, 3, 8);
                }
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                // Clone géré : le handle natif peut alors être libéré tout de suite
                using var temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }
}
