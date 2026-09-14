using System.Drawing;
using System.Drawing.Drawing2D;

namespace RelationshipGraphNative
{
    internal static class ArrangementIcon
    {
        public static Bitmap Create(SelectionArrangement mode, bool dark)
        {
            Bitmap bitmap = new Bitmap(48, 48);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen guide = new Pen(dark ? Color.FromArgb(112, 191, 255) : Color.FromArgb(0, 105, 180), 1.5f))
            using (Brush shape = new SolidBrush(dark ? Color.FromArgb(225, 232, 241) : Color.FromArgb(58, 72, 89)))
            {
                g.ScaleTransform(2, 2);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                switch (mode)
                {
                    case SelectionArrangement.Left:
                        g.FillRectangle(shape, 6, 4, 14, 5); g.FillRectangle(shape, 6, 14, 9, 5);
                        g.DrawLine(guide, 4, 2, 4, 22); break;
                    case SelectionArrangement.Right:
                        g.FillRectangle(shape, 4, 4, 14, 5); g.FillRectangle(shape, 9, 14, 9, 5);
                        g.DrawLine(guide, 20, 2, 20, 22); break;
                    case SelectionArrangement.VerticalCenter:
                        g.FillRectangle(shape, 3, 4, 18, 5); g.FillRectangle(shape, 7, 15, 10, 5);
                        g.DrawLine(guide, 12, 2, 12, 22); break;
                    case SelectionArrangement.Bottom:
                        g.FillRectangle(shape, 4, 4, 5, 14); g.FillRectangle(shape, 15, 9, 5, 9);
                        g.DrawLine(guide, 2, 20, 22, 20); break;
                    case SelectionArrangement.HorizontalCenter:
                        g.FillRectangle(shape, 4, 3, 5, 18); g.FillRectangle(shape, 15, 7, 5, 10);
                        g.DrawLine(guide, 2, 12, 22, 12); break;
                    case SelectionArrangement.Horizontal:
                        g.FillRectangle(shape, 3, 8, 3, 8); g.FillRectangle(shape, 10, 5, 4, 14); g.FillRectangle(shape, 18, 8, 3, 8);
                        g.DrawLine(guide, 2, 2, 2, 22); g.DrawLine(guide, 22, 2, 22, 22);
                        g.DrawLine(guide, 6, 12, 10, 12); g.DrawLine(guide, 14, 12, 18, 12); break;
                    case SelectionArrangement.Vertical:
                        g.FillRectangle(shape, 8, 3, 8, 3); g.FillRectangle(shape, 5, 10, 14, 4); g.FillRectangle(shape, 8, 18, 8, 3);
                        g.DrawLine(guide, 2, 2, 22, 2); g.DrawLine(guide, 2, 22, 22, 22);
                        g.DrawLine(guide, 12, 6, 12, 10); g.DrawLine(guide, 12, 14, 12, 18); break;
                }
            }
            return bitmap;
        }
    }
}
