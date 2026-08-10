using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RelationshipGraphNative
{
    internal static class NativeTheme
    {
        public static readonly Color DarkBackground = Color.FromArgb(18, 22, 27);
        public static readonly Color DarkSurface = Color.FromArgb(28, 34, 41);
        public static readonly Color DarkSurfaceRaised = Color.FromArgb(36, 43, 52);
        public static readonly Color DarkInput = Color.FromArgb(22, 27, 33);
        public static readonly Color DarkText = Color.FromArgb(229, 234, 240);
        public static readonly Color DarkMuted = Color.FromArgb(157, 169, 181);
        public static readonly Color DarkBorder = Color.FromArgb(64, 75, 87);
        public static readonly Color LightBackground = Color.FromArgb(244, 247, 250);
        public static readonly Color LightSurface = Color.White;
        public static readonly Color LightText = Color.FromArgb(25, 35, 48);

        public static bool SystemUsesDarkTheme()
        {
            if (SystemInformation.HighContrast) return SystemColors.Window.GetBrightness() < .5f;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value != null) return Convert.ToInt32(value) == 0;
                }
            }
            catch { }
            return false;
        }

        public static void ApplyWindowDarkMode(Form form, bool dark)
        {
            if (form == null || !form.IsHandleCreated) return;
            try
            {
                int enabled = dark ? 1 : 0;
                int result = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
                if (result != 0) DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
            }
            catch { }
        }

        public static void ApplyControlTree(Control root, bool dark)
        {
            if (root == null) return;
            Color surface = dark ? DarkSurface : LightSurface;
            Color text = dark ? DarkText : LightText;
            Color input = dark ? DarkInput : Color.White;

            if (!(root is GraphCanvas))
            {
                root.ForeColor = text;
                if (root is TextBoxBase || root is ComboBox || root is ListBox)
                    root.BackColor = input;
                else if (root is Button)
                {
                    Button button = (Button)root;
                    bool primary = button.FlatStyle == FlatStyle.Flat && button.FlatAppearance.BorderSize == 0 && button.BackColor.B > 150;
                    if (!primary) button.BackColor = dark ? DarkSurfaceRaised : Color.FromArgb(242, 245, 248);
                }
                else root.BackColor = surface;

                Label label = root as Label;
                if (label != null && label.Font.Size < 9f) label.ForeColor = dark ? DarkMuted : Color.FromArgb(105, 118, 132);
            }
            foreach (Control child in root.Controls) ApplyControlTree(child, dark);
        }

        public static void ApplyToolStrip(ToolStrip strip, bool dark)
        {
            if (strip == null) return;
            strip.BackColor = dark ? DarkSurface : LightSurface;
            strip.ForeColor = dark ? DarkText : LightText;
            strip.Renderer = new ToolStripProfessionalRenderer(new NativeColorTable(dark));
            foreach (ToolStripItem item in strip.Items) ApplyToolStripItem(item, dark);
        }

        private static void ApplyToolStripItem(ToolStripItem item, bool dark)
        {
            item.BackColor = dark ? DarkSurface : LightSurface;
            item.ForeColor = dark ? DarkText : LightText;
            ToolStripControlHost host = item as ToolStripControlHost;
            if (host != null)
            {
                host.Control.BackColor = dark ? DarkInput : Color.White;
                host.Control.ForeColor = dark ? DarkText : LightText;
            }
            ToolStripDropDownItem dropDownItem = item as ToolStripDropDownItem;
            if (dropDownItem == null) return;
            dropDownItem.DropDown.BackColor = dark ? DarkSurface : LightSurface;
            dropDownItem.DropDown.ForeColor = dark ? DarkText : LightText;
            dropDownItem.DropDown.Renderer = new ToolStripProfessionalRenderer(new NativeColorTable(dark));
            foreach (ToolStripItem child in dropDownItem.DropDownItems) ApplyToolStripItem(child, dark);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    internal sealed class NativeColorTable : ProfessionalColorTable
    {
        private readonly bool _dark;
        public NativeColorTable(bool dark) { _dark = dark; UseSystemColors = false; }
        private Color Surface { get { return _dark ? NativeTheme.DarkSurface : NativeTheme.LightSurface; } }
        private Color Raised { get { return _dark ? NativeTheme.DarkSurfaceRaised : Color.FromArgb(232, 238, 245); } }
        private Color Border { get { return _dark ? NativeTheme.DarkBorder : Color.FromArgb(207, 216, 225); } }
        public override Color ToolStripGradientBegin { get { return Surface; } }
        public override Color ToolStripGradientMiddle { get { return Surface; } }
        public override Color ToolStripGradientEnd { get { return Surface; } }
        public override Color MenuStripGradientBegin { get { return Surface; } }
        public override Color MenuStripGradientEnd { get { return Surface; } }
        public override Color StatusStripGradientBegin { get { return Surface; } }
        public override Color StatusStripGradientEnd { get { return Surface; } }
        public override Color ToolStripDropDownBackground { get { return Surface; } }
        public override Color ImageMarginGradientBegin { get { return Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Surface; } }
        public override Color ImageMarginGradientEnd { get { return Surface; } }
        public override Color ToolStripBorder { get { return Border; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return Color.FromArgb(43, 108, 245); } }
        public override Color MenuItemSelected { get { return Raised; } }
        public override Color MenuItemSelectedGradientBegin { get { return Raised; } }
        public override Color MenuItemSelectedGradientEnd { get { return Raised; } }
        public override Color MenuItemPressedGradientBegin { get { return Raised; } }
        public override Color MenuItemPressedGradientMiddle { get { return Raised; } }
        public override Color MenuItemPressedGradientEnd { get { return Raised; } }
        public override Color ButtonSelectedGradientBegin { get { return Raised; } }
        public override Color ButtonSelectedGradientMiddle { get { return Raised; } }
        public override Color ButtonSelectedGradientEnd { get { return Raised; } }
        public override Color ButtonPressedGradientBegin { get { return Raised; } }
        public override Color ButtonPressedGradientMiddle { get { return Raised; } }
        public override Color ButtonPressedGradientEnd { get { return Raised; } }
        public override Color ButtonCheckedGradientBegin { get { return Raised; } }
        public override Color ButtonCheckedGradientMiddle { get { return Raised; } }
        public override Color ButtonCheckedGradientEnd { get { return Raised; } }
        public override Color ButtonSelectedBorder { get { return Color.FromArgb(71, 132, 246); } }
        public override Color ButtonPressedBorder { get { return Color.FromArgb(71, 132, 246); } }
        public override Color SeparatorDark { get { return Border; } }
        public override Color SeparatorLight { get { return _dark ? Color.FromArgb(48, 57, 67) : Color.White; } }
    }
}
