using System.Windows;
using System.Windows.Media;

namespace TimeTracker
{
    public static class ThemeHelper
    {
        // 亮色方案
        private static readonly Color LightBg = Color.FromRgb(0xf5, 0xf6, 0xfa);
        private static readonly Color LightCard = Color.FromRgb(0xff, 0xff, 0xff);
        private static readonly Color LightText = Color.FromRgb(0x1a, 0x1d, 0x2e);
        private static readonly Color LightText2 = Color.FromRgb(0x6b, 0x72, 0x80);
        private static readonly Color LightAccent = Color.FromRgb(0x6c, 0x5c, 0xe7);

        // 暗色方案
        private static readonly Color DarkBg = Color.FromRgb(0x0f, 0x12, 0x1a);
        private static readonly Color DarkCard = Color.FromRgb(0x1a, 0x1f, 0x2e);
        private static readonly Color DarkText = Color.FromRgb(0xe2, 0xe8, 0xf0);
        private static readonly Color DarkText2 = Color.FromRgb(0x94, 0xa3, 0xb8);
        private static readonly Color DarkAccent = Color.FromRgb(0xa7, 0x8b, 0xfa);

        public static void Apply(bool dark)
        {
            var app = Application.Current;
            if (app == null) return;

            if (dark)
            {
                UpdateResource(app, "Bg", DarkBg);
                UpdateResource(app, "CardBg", DarkCard);
                UpdateResource(app, "TextPrimary", DarkText);
                UpdateResource(app, "TextSecondary", DarkText2);
                UpdateResource(app, "Accent", DarkAccent);
                UpdateResource(app, "Border", Color.FromRgb(0x33, 0x3d, 0x55));
                UpdateResource(app, "SidebarBg", Color.FromRgb(0x08, 0x0b, 0x12));
            }
            else
            {
                UpdateResource(app, "Bg", LightBg);
                UpdateResource(app, "CardBg", LightCard);
                UpdateResource(app, "TextPrimary", LightText);
                UpdateResource(app, "TextSecondary", LightText2);
                UpdateResource(app, "Accent", LightAccent);
                UpdateResource(app, "Border", Color.FromRgb(0xe5, 0xe7, 0xeb));
                UpdateResource(app, "SidebarBg", Color.FromRgb(0x1a, 0x1d, 0x2e));
            }
        }

        private static void UpdateResource(Application app, string key, Color color)
        {
            var brush = app.Resources[key] as SolidColorBrush;
            if (brush != null)
            {
                brush.Color = color;
            }
            else
            {
                app.Resources[key] = new SolidColorBrush(color);
            }
        }
    }
}
