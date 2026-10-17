using System.Windows;
using System.Windows.Markup;

namespace GamingControlCenter.Dialogs;

public partial class FastestTipsDialog : Window, IComponentConnector
{
	public static bool IsUserChoiceSure;

	public static bool IsNeverTipsChecked;

	public FastestTipsDialog()
	{
		InitializeComponent();
	}

	private void SureButton_OnClick(object sender, RoutedEventArgs e)
	{
		IsNeverTipsChecked = NeverTipsEnabledCheckBox.IsChecked.Value;
		IsUserChoiceSure = true;
		Close();
	}

	private void CancelButton_OnClick(object sender, RoutedEventArgs e)
	{
		IsUserChoiceSure = false;
		Close();
	}
}
