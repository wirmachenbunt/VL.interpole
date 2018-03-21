using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GraphicsControl
{
  public delegate void Resize(EventArgs e);

  public abstract class Canvas
  {
    public abstract void Rectangle(Rect r, Color color);
    public abstract void Ellipse(Rect r, Color color);
    public abstract void Line(Point from, Point to, Color color);
    public abstract void Text(Point pos, string text, Color color);
    public abstract void Polygon(Color color, params Point[] points);
    public abstract void InitCanvas(Rect r, DrawingContext g);
    public abstract void ShowImage();
  }
}
