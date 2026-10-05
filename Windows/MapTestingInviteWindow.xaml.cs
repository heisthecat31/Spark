using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Spark
{
	/// <summary>
	/// A one-time invite to the Echo VR map testing Discord, shown once when Spark opens
	/// (SparkSettings.mapTestingInviteShown).
	/// </summary>
	public partial class MapTestingInviteWindow : Window
	{
		public const string DiscordUrl = "https://discord.gg/4VrpA95fMg";

		public MapTestingInviteWindow()
		{
			InitializeComponent();
		}

		/// <summary>Fade in and slide up.</summary>
		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };
			BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
			slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
				new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
		}

		/// <summary>Keeps the accent glow clipped to the card's rounded rectangle.</summary>
		private void CardSizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (((FrameworkElement)sender).Parent is FrameworkElement card)
			{
				cardClip.Rect = new Rect(0, 0, card.ActualWidth, card.ActualHeight);
			}
		}

		private void DragWindow(object sender, MouseButtonEventArgs e)
		{
			if (e.ButtonState == MouseButtonState.Pressed) DragMove();
		}

		private void Join_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				Process.Start(new ProcessStartInfo(DiscordUrl) { UseShellExecute = true });
			}
			catch (Exception ex)
			{
				Logger.LogRow(Logger.LogType.Error, $"Couldn't open the map testing Discord link\n{ex}");
			}
			Close();
		}

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}
	}
}
