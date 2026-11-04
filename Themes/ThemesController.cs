using System;
using System.Windows;
using System.Windows.Media;

namespace Spark
{
	public static class ThemesController
	{
		public enum ThemeTypes
		{
			YellowDark,
			OrangeDark,
			RedDark
		}

		public static ThemeTypes CurrentTheme { get; set; }

		/// <summary>
		/// Raised after every <see cref="ApplyCustomTheme"/>, live previews included, for things
		/// outside WPF's resource system that copy the colours (the EchoVRMusic window in the Music tab).
		/// </summary>
		public static event Action ThemeApplied;

		/// <summary>
		/// The three colours actually applied right now — not necessarily what's saved. A preset
		/// click or a slider drag calls <see cref="ApplyCustomTheme"/> to live-preview instantly
		/// without touching <see cref="SparkSettings"/>; only a separate Apply/Save action persists.
		/// Anything that needs to track the theme the user is currently looking at (rather than what
		/// they've committed to) should read these instead of SparkSettings.instance.customThemeDark
		/// etc, which lags behind every live preview until Save is clicked.
		/// </summary>
		public static string CurrentDarkHex { get; private set; } = "#151515";
		public static string CurrentMidHex { get; private set; } = "#363636";
		public static string CurrentLightHex { get; private set; } = "#3E3E3E";

		// -----------------------------------------------------------------------
		// Runtime colour injection
		// -----------------------------------------------------------------------

		/// <summary>
		/// Applies a custom 3-colour theme by mutating all SolidColorBrush
		/// resources in Application.Current.Resources directly.
		/// dark  → backgrounds, window fill, selected states
		/// mid   → buttons, default borders, interactive controls
		/// light → hover highlights, carets, bright borders
		/// </summary>
		public static void ApplyCustomTheme(Color dark, Color mid, Color light)
		{
			CurrentDarkHex = ColorToHex(dark);
			CurrentMidHex = ColorToHex(mid);
			CurrentLightHex = ColorToHex(light);

			// Derive a slightly darker variant for the window title bar
			Color dark2 = Darken(dark, 0.12f);

			// Semi-transparent container (preserves slight transparency like the original #f5bb2961)
			Color containerColor = Color.FromArgb(0xF5, dark.R, dark.G, dark.B);

			// A "disabled" mid — slightly desaturated/darker
			Color midDark = Darken(mid, 0.15f);

			SetBrush("BackgroundColour",                          dark);
			SetBrush("WindowBorderColour",                        dark2);
			SetBrush("WindowTitleColour",                         dark2);
			SetBrush("ContainerBackground",                       containerColor);
			SetBrush("ContainerBorder",                           containerColor);

			SetBrush("ControlDarkerBackground",                   dark);
			SetBrush("ControlDarkerBorderBrush",                  dark);
			SetBrush("ControlDefaultBackground",                  mid);
			SetBrush("ControlDefaultBorderBrush",                 mid);
			SetBrush("ControlBrightDefaultBackground",            light);
			SetBrush("ControlBrightDefaultBorderBrush",           light);
			SetBrush("ControlDisabledBackground",                 midDark);
			SetBrush("ControlDisabledBorderBrush",                midDark);
			SetBrush("ControlMouseOverBackground",                light);
			SetBrush("ControlMouseOverBorderBrush",               mid);
			SetBrush("ControlSelectedBackground",                 dark);
			SetBrush("ControlSelectedBorderBrush",                dark);
			SetBrush("ControlSelectedMouseOverBackground",        mid);
			SetBrush("ControlSelectedMouseOverBorderBrush",       light);

			// Primary palette (from the colour variant .xaml files)
			SetBrush("ControlPrimaryDarkerBackground",            dark);
			SetBrush("ControlPrimaryDarkerBorderBrush",           dark);
			SetBrush("ControlPrimaryDefaultBackground",           mid);
			SetBrush("ControlPrimaryDefaultBorderBrush",          mid);
			SetBrush("ControlPrimaryBrightDefaultBackground",     light);
			SetBrush("ControlPrimaryBrightDefaultBorderBrush",    light);
			SetBrush("ControlPrimaryDisabledBackground",          midDark);
			SetBrush("ControlPrimaryDisabledBorderBrush",         midDark);
			SetBrush("ControlPrimaryMouseOverBackground",         light);
			SetBrush("ControlPrimaryMouseOverBorderBrush",        mid);
			SetBrush("ControlPrimarySelectedBackground",          dark);
			SetBrush("ControlPrimarySelectedBorderBrush",         dark);
			SetBrush("ControlPrimarySelectedMouseOverBackground", mid);
			SetBrush("ControlPrimarySelectedMouseOverBorderBrush",light);
			SetBrush("ControlPrimaryCaretSelectionBackground",    mid);
			SetBrush("ControlPrimaryCaretBackground",             light);
			SetBrush("ControlPrimaryGlythColour",                 light);
			SetBrush("ControlPrimaryMouseOverGlythColour",        mid);
			SetBrush("ControlPrimarySelectedGlythColour",         light);
			SetBrush("ControlPrimarySelectedMouseOverGlythColour",light);
			SetBrush("ControlPrimaryDisabledGlythColour",         midDark);

			// Alternating Row Backgrounds (Dynamic Greys)
			SetBrush("ControlRowBackground1",                     dark);
			SetBrush("ControlRowBackground2",                     Darken(dark, 0.05f));
			SetBrush("ControlRowBorder1",                         midDark);
			SetBrush("ControlRowBorder2",                         midDark);

			ApplyDerivedPalette(dark, mid, light);
			ThemeApplied?.Invoke();
		}

		/// <summary>
		/// The three user colours only describe backgrounds and controls, so everything layered on
		/// top — surface steps, readable text, team and status colours — is derived from them here
		/// instead of being hard-coded. Keeps a single palette working across the whole preset range,
		/// from #000000 through to Hot Pink.
		/// </summary>
		private static void ApplyDerivedPalette(Color dark, Color mid, Color light)
		{
			// Text ink is chosen first, then everything else follows it: if the window colour needs
			// dark text it's a bright theme, so surfaces darken and team colours take their deep
			// shades. Deriving both from one decision keeps them from disagreeing.
			Color ink = InkOn(dark);
			bool brightWindow = Luminance(ink) < 0.5f;

			// Surfaces step *away* from the window colour — lighter on a dark theme, darker on a
			// bright one — so panel depth doesn't collapse at either end of the range.
			Color away = brightWindow ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255);

			SetBrush("SurfaceGround",     dark);
			SetBrush("SurfaceChrome",     Mix(dark, away, 0.045f));
			SetBrush("SurfaceCard",       Mix(dark, away, 0.075f));
			SetBrush("SurfaceRaised",     Mix(dark, away, 0.115f));
			SetBrush("SurfaceBorderSoft", Mix(dark, away, 0.10f));
			SetBrush("SurfaceBorder",     Mix(dark, away, 0.17f));
			SetBrush("SurfaceTrack",      Mix(dark, away, 0.20f));

			SetBrush("ControlDefaultForeground", ink);
			SetBrush("TextPrimary",              ink);
			SetBrush("TextDim",                  Mix(ink, dark, 0.34f));
			SetBrush("TextFaint",                Mix(ink, dark, 0.55f));
			SetBrush("ControlPrimaryForeground", InkOn(mid));

			// The accent carries the active tab, links, sparklines and the app mark, so it has to
			// stay legible against the window. "light" only promises to be the hover/bright end of
			// the user's three colours, and on a near-black theme that can still be a dark grey —
			// #242424 on #080808 is 1.27:1, which makes every accent vanish. Lift it until it reads.
			Color accent = light;
			for (int attempt = 0; attempt < 8 && Contrast(accent, dark) < 3.5f; attempt++)
			{
				accent = Mix(accent, ink, 0.3f);
			}

			SetBrush("ControlAccent",           accent);
			SetBrush("ControlAccentForeground", InkOn(accent));

			// Team identity keeps its hue but takes the shade that stays legible on this background.
			Color teamBlue   = brightWindow ? Color.FromRgb(49, 80, 122)  : Color.FromRgb(110, 147, 214);
			Color teamOrange = brightWindow ? Color.FromRgb(140, 92, 42)  : Color.FromRgb(212, 148, 30);
			Color good       = brightWindow ? Color.FromRgb(58, 96, 66)   : Color.FromRgb(126, 158, 94);
			Color warn       = brightWindow ? Color.FromRgb(122, 84, 24)  : Color.FromRgb(201, 162, 39);
			Color bad        = brightWindow ? Color.FromRgb(138, 46, 46)  : Color.FromRgb(192, 122, 74);

			SetBrush("TeamBlue",             teamBlue);
			SetBrush("TeamBlueTint",         Mix(teamBlue, dark, 0.84f));
			SetBrush("TeamBlueEdge",         Mix(teamBlue, dark, 0.62f));
			SetBrush("TeamBlueForeground",   InkOn(teamBlue));
			SetBrush("TeamOrange",           teamOrange);
			SetBrush("TeamOrangeTint",       Mix(teamOrange, dark, 0.84f));
			SetBrush("TeamOrangeEdge",       Mix(teamOrange, dark, 0.62f));
			SetBrush("TeamOrangeForeground", InkOn(teamOrange));

			SetBrush("StatusGood",     good);
			SetBrush("StatusGoodTint", Mix(good, dark, 0.82f));
			SetBrush("StatusGoodEdge", Mix(good, dark, 0.60f));
			SetBrush("StatusWarn",     warn);
			SetBrush("StatusWarnTint", Mix(warn, dark, 0.82f));
			SetBrush("StatusWarnEdge", Mix(warn, dark, 0.60f));
			SetBrush("StatusBad",      bad);
			SetBrush("StatusBadTint",  Mix(bad, dark, 0.84f));
			SetBrush("StatusBadEdge",  Mix(bad, dark, 0.62f));
		}

		/// <summary>
		/// Applies the custom theme stored in SparkSettings.
		/// ALWAYS loads the base XAML first (needed for all WPF control templates),
		/// then overrides colour resources on top.
		/// Safe to call before SparkSettings is loaded (will use defaults).
		/// </summary>
		public static void ApplyFromSettings()
		{
			try
			{
				// Step 1: Load the base theme XAML so all WPF control templates are present.
				// This is what the original ThemesController.SetTheme() did.
				ChangeTheme(new Uri("/Themes/ColourfulDarkTheme_Neutral.xaml", UriKind.Relative));
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"ThemesController: {e}");
			}

			try
			{
				// Step 2: Override colour resources with user's saved colours.
				Color dark  = ParseHex(SparkSettings.instance?.customThemeDark  ?? "#151515");
				Color mid   = ParseHex(SparkSettings.instance?.customThemeMid   ?? "#363636");
				Color light = ParseHex(SparkSettings.instance?.customThemeLight ?? "#3E3E3E");
				ApplyCustomTheme(dark, mid, light);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"ThemesController colour apply: {e}");
			}
		}

		/// <summary>
		/// Saves the 3 colours to settings and immediately applies them.
		/// </summary>
		public static void SaveAndApply(Color dark, Color mid, Color light)
		{
			if (SparkSettings.instance != null)
			{
				SparkSettings.instance.customThemeDark  = ColorToHex(dark);
				SparkSettings.instance.customThemeMid   = ColorToHex(mid);
				SparkSettings.instance.customThemeLight = ColorToHex(light);
				SparkSettings.instance.Save();
			}
			ApplyCustomTheme(dark, mid, light);
		}

		// -----------------------------------------------------------------------
		// Legacy file-swap API (no longer needed but kept for compat)
		// -----------------------------------------------------------------------

		public static void SetTheme(ThemeTypes theme)
		{
			CurrentTheme = theme;
			// Legacy themes all mapped to the same pink palette — apply custom theme from settings instead
			ApplyFromSettings();
		}

		private static ResourceDictionary ThemeDictionary
		{
			get => Application.Current.Resources.MergedDictionaries[0];
			set => Application.Current.Resources.MergedDictionaries[0] = value;
		}

		private static void ChangeTheme(Uri uri)
		{
			ThemeDictionary = new ResourceDictionary { Source = uri };
		}

		// -----------------------------------------------------------------------
		// Helpers
		// -----------------------------------------------------------------------

		private static void SetBrush(string key, Color color)
		{
			try
			{
				// Now that ColourfulDarkTheme_base.xaml uses {DynamicResource} for all colour brushes,
				// setting the key in Application.Current.Resources (top-level direct dict) is enough:
				// WPF's DynamicResource lookup checks the app-level direct dict BEFORE merged dicts,
				// so this immediately overrides the base.xaml values across the entire visual tree.
				Application.Current.Resources[key] = new SolidColorBrush(color);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"ThemesController SetBrush '{key}': {e}");
			}
		}

		public static Color ParseHex(string hex)
		{
			hex = hex.TrimStart('#');
			if (hex.Length == 6)
				hex = "FF" + hex;
			byte a = Convert.ToByte(hex.Substring(0, 2), 16);
			byte r = Convert.ToByte(hex.Substring(2, 2), 16);
			byte g = Convert.ToByte(hex.Substring(4, 2), 16);
			byte b = Convert.ToByte(hex.Substring(6, 2), 16);
			return Color.FromArgb(a, r, g, b);
		}

		public static string ColorToHex(Color c)
		{
			return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
		}

		private static Color Darken(Color c, float amount)
		{
			return Color.FromArgb(c.A,
				(byte)Math.Max(0, c.R - (int)(255 * amount)),
				(byte)Math.Max(0, c.G - (int)(255 * amount)),
				(byte)Math.Max(0, c.B - (int)(255 * amount)));
		}

		private static readonly Color inkLight = Color.FromRgb(0xEC, 0xEC, 0xEC);
		private static readonly Color inkDark = Color.FromRgb(0x14, 0x16, 0x1A);

		/// <summary>
		/// sRGB relative luminance — how bright a colour actually looks, rather than its raw average.
		/// </summary>
		private static float Luminance(Color c)
		{
			return 0.2126f * LinearChannel(c.R) + 0.7152f * LinearChannel(c.G) + 0.0722f * LinearChannel(c.B);
		}

		private static float LinearChannel(byte value)
		{
			float s = value / 255f;
			return s <= 0.03928f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
		}

		/// <summary>WCAG contrast ratio between two colours, from 1:1 to 21:1.</summary>
		private static float Contrast(Color a, Color b)
		{
			float la = Luminance(a);
			float lb = Luminance(b);
			return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
		}

		/// <summary>
		/// Picks whichever text colour has more contrast against <paramref name="background"/>,
		/// dropping to pure black or white when neither soft ink clears WCAG AA (4.5:1). A plain
		/// luminance threshold isn't enough: mid-brightness presets like Hot Pink and Cool Purple
		/// sit below it yet still need dark text.
		/// </summary>
		private static Color InkOn(Color background)
		{
			float onDark = Contrast(inkDark, background);
			float onLight = Contrast(inkLight, background);
			bool preferDark = onDark >= onLight;

			if (MathF.Max(onDark, onLight) >= 4.5f)
			{
				return preferDark ? inkDark : inkLight;
			}

			return preferDark ? Colors.Black : Colors.White;
		}

		/// <summary>Blends <paramref name="a"/> toward <paramref name="b"/> by <paramref name="t"/> (0..1).</summary>
		private static Color Mix(Color a, Color b, float t)
		{
			return Color.FromArgb(a.A,
				(byte)Math.Round(a.R + (b.R - a.R) * t),
				(byte)Math.Round(a.G + (b.G - a.G) * t),
				(byte)Math.Round(a.B + (b.B - a.B) * t));
		}
	}
}