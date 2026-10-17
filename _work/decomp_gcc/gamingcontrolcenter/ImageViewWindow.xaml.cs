using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace GamingControlCenter;

public partial class ImageViewWindow : Window, IComponentConnector
{
	public ImageViewWindow()
	{
		InitializeComponent();
	}

	public void ShowImage(string imagePath, string img2)
	{
		BitmapImage source = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
		BitmapImage source2 = new BitmapImage(new Uri(img2, UriKind.RelativeOrAbsolute));
		ImageDisplay.Source = source;
		ImageDisplay2.Source = source2;
		Show();
	}
}
