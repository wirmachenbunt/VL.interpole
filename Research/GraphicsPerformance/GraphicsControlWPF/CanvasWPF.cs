using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GraphicsControl;
using System.Windows;
using System.Windows.Media;
using System.Globalization;

namespace GraphicsControlWPF
{
  public class CanvasWPF : Canvas
  {
    private DrawingContext g;
    private Typeface font = new Typeface("Verdana");

    private SolidColorBrush GetBrush(Color color)
    {
      SolidColorBrush result = new SolidColorBrush(color);
      return result;
    }

    private Pen GetPen(Color color)
    {
      Pen result = new Pen(new SolidColorBrush(color), 1);
      return result;
    }

    private StreamGeometry GetPathGeometry(Point[] points, bool isStroked)
    {
      StreamGeometry result = null;
      if (points.Length > 1)
      {
        result = new StreamGeometry();
        StreamGeometryContext context = result.Open();
        context.BeginFigure(points[0], true, true);
        context.PolyLineTo(points, isStroked, true);
        context.Close();
        result.Freeze();
      }
      return result;
    }

    public FormattedText GetDrawingText(string text, Color color)
    {
      FormattedText result = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
      font, 10 * 1.4, GetBrush(color));

      return result;
    }

    public override void Rectangle(Rect r, Color color)
    {
      //System.Diagnostics.Debug.WriteLine(brush.Color.R.ToString() + " " + brush.Color.G.ToString() + " " + brush.Color.B.ToString());
      g.DrawRectangle(GetBrush(color), GetPen(color), r);
    }

    public override void Ellipse(Rect r, Color color)
    {
      g.DrawEllipse(GetBrush(color), GetPen(color), Utils.RectCenter(r), r.Width / 2.0, r.Height / 2.0); 
    }

    public override void Line(Point from, Point to, Color color)
    {
      g.DrawLine(GetPen(color), from, to);
    }

    public override void Text(Point pos, string text, Color color)
    {
      g.DrawText(GetDrawingText(text, color), pos);
    }

    public override void Polygon(Color color, params Point[] points)
    {
      StreamGeometry geom = GetPathGeometry(points, true);
      if (geom != null)
      {
        g.DrawGeometry(GetBrush(color), GetPen(color), geom);
      }
    }

    public override void InitCanvas(Rect r, DrawingContext g)
    {
      this.g = g;
    }

    public override void ShowImage()
    {
      g = null;
    }
  }
}
