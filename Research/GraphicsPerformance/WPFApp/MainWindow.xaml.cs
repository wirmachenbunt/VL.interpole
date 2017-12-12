using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using GraphicsControl;

namespace WPFApp
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    private bool setting;
    private long ticks;
    private DispatcherTimer dispatcherTimer;
    private List<float> frames = new List<float>();

    public MainWindow()
    {
      setting = true;
      InitializeComponent();
      dispatcherTimer = new DispatcherTimer();
      dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
      dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
      setting = false;
    }

    private void cbElements_Checked(object sender, RoutedEventArgs e)
    {
      if (!setting)
      {
        OnChangeParam();
        CheckBox c = sender as CheckBox;
        switch (c.Name)
        {
          case "cbLine":
            wpfFormControl.PaintLines = c.IsChecked.Value;
            break;
          case "cbEllipse":
            wpfFormControl.PaintEllipses = c.IsChecked.Value;
            break;
          case "cbText":
            wpfFormControl.PaintText = c.IsChecked.Value;
            break;
          case "cbRectangle":
            wpfFormControl.PaintRectangles = c.IsChecked.Value;
            break;
          case "cbPolygon":
            wpfFormControl.PaintPolygon = c.IsChecked.Value;
            break;
          default:
            break;
        }
      }
    }


    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      OnChangeParam();
      dispatcherTimer.Start();
    }


    private void dispatcherTimer_Tick(object sender, EventArgs e)
    {
      if(Utils.FramesPerSecond > 0) frames.Add(Utils.FramesPerSecond);
      string fps = frames.Count > 0 ? frames.Average().ToString("F2") : "0";
      string seconds = TimeSpan.FromTicks(DateTime.Now.Ticks - ticks).Seconds.ToString();
      this.Title = "FPS: " + fps + " | Elements rendered per frame: " + wpfFormControl.ElementCount.ToString()
        + " | Elapsed seconds: " + seconds;
      wpfFormControl.InvalidateVisual();
    }

    private void slider1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      OnChangeParam();
      wpfFormControl.ElementDensity = (int)Math.Round(slider1.Value);
    }

    private void OnChangeParam()
    {
      frames.Clear();
      ticks = DateTime.Now.Ticks;
    }
  }
}
