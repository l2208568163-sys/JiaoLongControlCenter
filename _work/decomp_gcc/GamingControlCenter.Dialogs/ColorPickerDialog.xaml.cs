using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GamingControlCenter.Dialogs;

public partial class ColorPickerDialog : Window, IComponentConnector
{
	public static byte pickRed;

	public static byte pickGreen;

	public static byte pickBlue;

	public static bool IsUserChoiceSure;

	public ColorPickerDialog()
	{
		InitializeComponent();
	}

	private void ColorPicker_OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		PickColor(sender as Image);
	}

	private void PickColor(Image colorPicker)
	{
		Point position = Mouse.GetPosition(colorPicker);
		BitmapSource bitmapSource = colorPicker.Source as BitmapSource;
		try
		{
			CroppedBitmap croppedBitmap = new CroppedBitmap(bitmapSource, new Int32Rect((int)((double)bitmapSource.PixelWidth * position.X / colorPicker.ActualWidth), (int)((double)bitmapSource.PixelHeight * position.Y / colorPicker.ActualHeight), 1, 1));
			byte[] array = new byte[bitmapSource.Format.BitsPerPixel / 8];
			croppedBitmap.CopyPixels(array, bitmapSource.Format.BitsPerPixel / 8, 0);
			setColor(array[2], array[1], array[0]);
		}
		catch (ArgumentException)
		{
		}
	}

	private void setColor(byte r, byte g, byte b)
	{
		pickRed = r;
		pickGreen = g;
		pickBlue = b;
		ColorR.Text = r.ToString();
		ColorG.Text = g.ToString();
		ColorB.Text = b.ToString();
		Color color = Color.FromRgb(r, g, b);
		SettingColor.Background = new SolidColorBrush(color);
	}

	private void DefaultRed_Click(object sender, RoutedEventArgs e)
	{
		setColor(byte.MaxValue, 0, 0);
	}

	private void DefaultGreen_Click(object sender, RoutedEventArgs e)
	{
		setColor(0, 128, 0);
	}

	private void DefaultBlue_Click(object sender, RoutedEventArgs e)
	{
		setColor(0, 0, byte.MaxValue);
	}

	private void DefaultYellow_Click(object sender, RoutedEventArgs e)
	{
		setColor(byte.MaxValue, byte.MaxValue, 0);
	}

	private void DefaultPurple_Click(object sender, RoutedEventArgs e)
	{
		setColor(128, 0, 128);
	}

	private void DefaultIndigo_Click(object sender, RoutedEventArgs e)
	{
		setColor(0, byte.MaxValue, byte.MaxValue);
	}

	private void SureButton_OnClick(object sender, RoutedEventArgs e)
	{
		IsUserChoiceSure = true;
		Close();
	}

	private void CancelButton_OnClick(object sender, RoutedEventArgs e)
	{
		IsUserChoiceSure = false;
		Close();
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		setColor(pickRed, pickGreen, pickBlue);
	}

	private void PreviewTextInput1(object sender, TextCompositionEventArgs e)
	{
		if (new Regex("^\\d{0,3}$").IsMatch(e.Text))
		{
			string text = e.Text;
			if (text.Length <= 3)
			{
				try
				{
					if (int.Parse(text) <= 255)
					{
						e.Handled = false;
						return;
					}
				}
				catch
				{
					e.Handled = true;
					return;
				}
			}
		}
		e.Handled = true;
	}

	private void Color_TextChanged(object sender, TextChangedEventArgs e)
	{
		lock (this)
		{
			TextBox textBox = sender as TextBox;
			if (textBox.Text.Length < 1)
			{
				textBox.Text = "0";
				return;
			}
			if (textBox.Text.Length > 3)
			{
				textBox.Text = textBox.Text.Substring(0, 3);
				return;
			}
			try
			{
				byte b = byte.Parse(textBox.Text);
				if (b > byte.MaxValue)
				{
					textBox.Text = "255";
					return;
				}
				switch (textBox.Tag as string)
				{
				case "R":
					setColor(b, pickGreen, pickBlue);
					break;
				case "G":
					setColor(pickRed, b, pickBlue);
					break;
				case "B":
					setColor(pickRed, pickGreen, b);
					break;
				}
			}
			catch
			{
				if (textBox.Text.Length <= 1)
				{
					textBox.Text = "0";
					return;
				}
				try
				{
					if (textBox.Text.Length <= 1)
					{
						textBox.Text = "0";
					}
					else
					{
						textBox.Text = textBox.Text.Substring(0, textBox.Text.Length - 1);
					}
				}
				catch
				{
					textBox.Text = "0";
				}
			}
		}
	}
}
