using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace GraphicsControl
{
  public class CanvasGDIplus : Canvas
  {
    private Graphics g;

    private SolidBrush brush = new SolidBrush(Color.White);
    private Pen pen = new Pen(Color.White);

    public override void Rectangle(System.Drawing.Rectangle r, Color color)
    {
      brush.Color = color;
      pen.Color = color;
      g.DrawRectangle(pen, r);
      g.FillRectangle(brush, r);
    }

    public override void InitCanvas(Rectangle r, Graphics g, IntPtr handle)
    {
      this.g = g;

      if (AntiAlias)
      {
        this.g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        this.g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
      }
      else
      {
        this.g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        this.g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
      }
    }

    public override void Dispose()
    {
      g.Dispose();
      brush.Dispose();
      pen.Dispose();
      font.Dispose();
    }

    public override void ShowImage()
    {
     
    }

    public override void Ellipse(Rectangle r, Color color)
    {
      brush.Color = color;
      pen.Color = color;
      g.FillEllipse(brush, r);
    }

    public override void Line(Point from, Point to, Color color)
    {
      pen.Color = color;
      //g.DrawLine(pen, from, to);
      g.DrawLine(pen, from.X, from.Y, to.X, to.Y);
    }

    public override void Text(Point pos, string text, Color color)
    {
      brush.Color = color;
      g.DrawString(text, font, brush, pos);
    }

    private Font font = new Font("Verdana", 10);

    public override bool PaintBackground
    {
      get { return true; }
    }

    public override void DoResize(Resize resize, EventArgs e, Size newSize)
    {
      resize.Invoke(e);
    }

    public override void Polygon(Color color, params Point[] points)
    {
      brush.Color = color;
      pen.Color = color;
      g.FillPolygon(brush, points);
      g.DrawPolygon(pen, points);
    }
  }
}
