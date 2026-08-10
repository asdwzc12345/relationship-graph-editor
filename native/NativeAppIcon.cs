using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RelationshipGraphNative
{
    internal static class NativeAppIcon
    {
        private const string ResourceName = "RelationshipGraphNative.Assets.app-icon.ico";

        public static Icon Create()
        {
            try
            {
                Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (executableIcon != null) return executableIcon;
            }
            catch { }
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                    {
                        using (Icon embedded = new Icon(stream)) return (Icon)embedded.Clone();
                    }
                }
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
