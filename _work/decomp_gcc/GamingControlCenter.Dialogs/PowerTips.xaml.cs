using System.Windows;
using System.Windows.Markup;

namespace GamingControlCenter.Dialogs;

public partial class PowerTips : Window, IComponentConnector
{
	public static bool IsUserChoiceSure;

	public PowerTips()
	{
		InitializeComponent();
	}

	public PowerTips(string tipstr)
	{
		InitializeComponent();
		powertipsTextBlock.Text = tipstr;
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
