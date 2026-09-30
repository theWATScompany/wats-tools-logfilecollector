using System.Drawing;
using System.Windows.Forms;

namespace LogFileCollector.Tray
{
    /// <summary>
    /// WATS colours for the tray menu.
    ///
    /// A default ContextMenuStrip renders in the grey-on-grey style Windows has
    /// shipped since the nineties, which looks nothing like the dashboard it sits
    /// next to. These are the same token values as assets/tokens.css, so the menu and
    /// the page agree: near-black surfaces, a yellow accent, and the semantic
    /// pass/fail colours.
    /// </summary>
    internal static class WatsColors
    {
        // Dark theme — matches :root in tokens.css.
        public static readonly Color Background = ColorTranslator.FromHtml("#1c1c1c"); // --cb
        public static readonly Color Surface = ColorTranslator.FromHtml("#141414");    // --ch
        public static readonly Color Border = ColorTranslator.FromHtml("#2e2e2e");     // --bd
        public static readonly Color Foreground = ColorTranslator.FromHtml("#eeeeee"); // --f
        public static readonly Color Muted = ColorTranslator.FromHtml("#9a9a9a");      // --fd
        public static readonly Color Accent = ColorTranslator.FromHtml("#fdc400");     // --y
        public static readonly Color Hover = ColorTranslator.FromHtml("#262626");
        public static readonly Color Green = ColorTranslator.FromHtml("#66c866");      // --g
        public static readonly Color Red = ColorTranslator.FromHtml("#ee5757");        // --r
        public static readonly Color Orange = ColorTranslator.FromHtml("#ec9831");     // --o
    }

    /// <summary>
    /// Colour table behind <see cref="WatsMenuRenderer"/>.
    /// </summary>
    internal class WatsColorTable : ProfessionalColorTable
    {
        public WatsColorTable() { UseSystemColors = false; }

        public override Color ToolStripDropDownBackground => WatsColors.Background;
        public override Color ImageMarginGradientBegin => WatsColors.Background;
        public override Color ImageMarginGradientMiddle => WatsColors.Background;
        public override Color ImageMarginGradientEnd => WatsColors.Background;
        public override Color MenuBorder => WatsColors.Border;
        public override Color MenuItemBorder => WatsColors.Border;
        public override Color MenuItemSelected => WatsColors.Hover;
        public override Color MenuItemSelectedGradientBegin => WatsColors.Hover;
        public override Color MenuItemSelectedGradientEnd => WatsColors.Hover;
        public override Color MenuItemPressedGradientBegin => WatsColors.Surface;
        public override Color MenuItemPressedGradientEnd => WatsColors.Surface;
        public override Color SeparatorDark => WatsColors.Border;
        public override Color SeparatorLight => WatsColors.Border;
        public override Color CheckBackground => WatsColors.Surface;
        public override Color CheckSelectedBackground => WatsColors.Surface;
        public override Color CheckPressedBackground => WatsColors.Surface;
    }

    /// <summary>
    /// Renderer that finishes what the colour table cannot reach: text colour,
    /// the disabled state, and the yellow bar that marks the selected item — the
    /// same 2px accent the sidebar uses on the dashboard, rather than a fill.
    /// </summary>
    internal class WatsMenuRenderer : ToolStripProfessionalRenderer
    {
        public WatsMenuRenderer() : base(new WatsColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled)
            {
                // The status line is a disabled item, so it must stay readable —
                // the default disabled grey on near-black is not.
                e.TextColor = WatsColors.Muted;
            }
            else if (e.Item.Selected)
            {
                e.TextColor = WatsColors.Accent;
            }
            else
            {
                e.TextColor = WatsColors.Foreground;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            base.OnRenderMenuItemBackground(e);
            if (!e.Item.Selected || !e.Item.Enabled) return;

            using (var brush = new SolidBrush(WatsColors.Accent))
            {
                e.Graphics.FillRectangle(brush, 0, e.Item.ContentRectangle.Top, 2, e.Item.ContentRectangle.Height);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(WatsColors.Background))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(WatsColors.Border))
            {
                Rectangle r = e.AffectedBounds;
                e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
        }
    }
}
