using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace GamingControlCenter;

public partial class UserControl1 : UserControl, IComponentConnector
{
	private UserControl1_Para userControl1_Para = new UserControl1_Para();

	private List<CollectPoint> collectPoints = new List<CollectPoint>();

	private double x;

	public UserControl1()
	{
		InitializeComponent();
	}

	public void myCanvasInit(UserControl1_Para para)
	{
		userControl1_Para.xGrap = para.xGrap;
		userControl1_Para.xGrapLable = para.xGrapLable;
		userControl1_Para.xGrapLablePoint = para.xGrapLablePoint;
		userControl1_Para.yGrap = para.yGrap;
		userControl1_Para.yGrapLable = para.yGrapLable;
		userControl1_Para.yLables = para.yLables;
		userControl1_Para.yStartValue = para.yStartValue;
		userControl1_Para.xPointRight = para.xPointRight;
		userControl1_Para.xWidth = para.xWidth;
	}

	public void AddPoint1(CollectPoint collectPoint)
	{
		x += userControl1_Para.xGrap;
		collectPoint.point.X = x;
		collectPoint.point.Y += 5.0;
		double num = collectPoint.point.X * -1.0 + userControl1_Para.xWidth;
		_myPolyline.Points.Add(collectPoint.point);
		_myCanvas.Margin = new Thickness(num, 0.0, 0.0, 0.0);
		TextBlock textBlock = new TextBlock();
		textBlock.Visibility = Visibility.Hidden;
		textBlock.SetValue(Grid.RowProperty, 1);
		xGrid.Children.Add(textBlock);
		textBlock.Margin = new Thickness(collectPoint.point.X, 0.0, 0.0, 0.0);
		collectPoints.Add(new CollectPoint
		{
			point = collectPoint.point,
			curTime = collectPoint.curTime
		});
		xLableCanvas.Margin = new Thickness(num, 0.0, 0.0, 0.0);
		if (num % (double)userControl1_Para.xGrapLable == 0.0)
		{
			textBlock.Visibility = Visibility.Visible;
		}
		if (collectPoint.point.X > userControl1_Para.xWidth)
		{
			_myPolyline.Points.RemoveAt(0);
			collectPoints.RemoveAt(0);
			xGrid.Children.RemoveAt(0);
		}
	}
}
