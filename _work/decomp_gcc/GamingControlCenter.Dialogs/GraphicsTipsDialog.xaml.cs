using System.Windows;
using System.Windows.Markup;

namespace GamingControlCenter.Dialogs;

public partial class GraphicsTipsDialog : Window, IComponentConnector
{
	public static bool IsUserChoiceSure;

	public GraphicsTipsDialog()
	{
		InitializeComponent();
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
}
