using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace GraphicsControl
{
  public delegate void Resize(EventArgs e);

  public abstract class Canvas : IDisposable
  {
    public abstract void Rectangle(Rectangle r, Color color);
    public abstract void Ellipse(Rectangle r, Color color);
    public abstract void Line(Point from, Point to, Color color);
    public abstract void Text(Point pos, string text, Color color);
    public abstract void Polygon(Color color, params Point[] points);
    public abstract void InitCanvas(Rectangle r, Graphics g, IntPtr handle);
    public abstract bool PaintBackground { get; }
    public abstract void DoResize(Resize resize, EventArgs e, Size newSize);
    public abstract void ShowImage();
    public bool AntiAlias { get; set; }

    #region IDisposable Members
    public abstract void Dispose();
    #endregion
  }
}
