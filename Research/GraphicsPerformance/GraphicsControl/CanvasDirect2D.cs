using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using SlimDX.Direct2D;
using SlimDX.DirectWrite;
using System.Globalization;

namespace GraphicsControl
{
  public class CanvasDirect2D : Canvas
  {
    private DeviceContext2D context = null;
    private WindowRenderTarget g;
    private SlimDX.Direct2D.SolidColorBrush brush;
    private SlimDX.Direct2D.SolidColorBrush pen;

    public CanvasDirect2D()
    {
    }

    public override void Rectangle(Rectangle r, Color color)
    {
      brush.Color = color;
      pen.Color = color;
      g.FillRectangle(brush, r);
      g.DrawRectangle(pen, r);
    }

    public override void InitCanvas(Rectangle r, Graphics g, IntPtr handle)
    {
      if (context == null)
      {
        DeviceSettings2D settings = new DeviceSettings2D
        {
          Width = r.Width,
          Height = r.Height
        };
        context = new DeviceContext2D(handle, settings);
        this.g = context.RenderTarget;
        brush = new SolidColorBrush(this.g, Color.White);
        pen = new SolidColorBrush(this.g, Color.White);
        textFormat = TextFormat();
      }

      if (!this.g.IsOccluded)
      {
        this.g.BeginDraw();
        this.g.Clear(Color.White);
        if (AntiAlias)
        {
          this.g.TextAntialiasMode = TextAntialiasMode.ClearType;
          this.g.AntialiasMode = AntialiasMode.PerPrimitive;
        }
        else
        {
          this.g.TextAntialiasMode = TextAntialiasMode.Aliased;
          this.g.AntialiasMode = AntialiasMode.Aliased;
        }

        this.g.Transform = SlimDX.Matrix3x2.Identity;
      }
    }

    public override void ShowImage()
    {
      if (!g.IsOccluded)
      {
        g.EndDraw();
      }
    }

    public override void Dispose()
    {
      brush.Dispose();
      pen.Dispose();
      g.Dispose();
      context.Dispose();
      textFormat.Dispose();
    }


    private Ellipse ellipse = new Ellipse();

    public override void Ellipse(Rectangle r, Color color)
    {
      ellipse.Center = Utils.RectCenter(r);
      ellipse.RadiusX = r.Width / 2;
      ellipse.RadiusY = r.Height / 2;
      brush.Color = color;
      pen.Color = color;
      g.FillEllipse(brush, ellipse);
      g.DrawEllipse(pen, ellipse);
    }

    public override void Line(Point from, Point to, Color color)
    {
      pen.Color = color;
      g.DrawLine(pen, from, to, 1);
    }

    private Size textSize = new Size(200, 200);

    public override void Text(Point pos, string text, Color color)
    {
      brush.Color = color;
      g.DrawText(text, textFormat, new Rectangle(pos, textSize), brush);
    }

    private SlimDX.DirectWrite.Factory dwfactory;
    private SlimDX.DirectWrite.TextFormat textFormat;

    private SlimDX.DirectWrite.TextFormat TextFormat()
    {
      if (dwfactory == null)
      {
        dwfactory = new SlimDX.DirectWrite.Factory(SlimDX.DirectWrite.FactoryType.Isolated);
      }

      SlimDX.DirectWrite.TextFormat result = new SlimDX.DirectWrite.TextFormat(dwfactory, "Verdana", FontWeight.Normal, SlimDX.DirectWrite.FontStyle.Normal,
        FontStretch.Normal, 10 * 1.3f, CultureInfo.CurrentCulture.Name);

      result.ParagraphAlignment = SlimDX.DirectWrite.ParagraphAlignment.Near;
      result.TextAlignment = SlimDX.DirectWrite.TextAlignment.Leading;

      return result;
    }

    public override bool PaintBackground
    {
      get { return false; }
    }

    public override void DoResize(Resize resize, EventArgs e, Size newSize)
    {
      g.Resize(newSize);
    }

    public override void Polygon(Color color, params Point[] points)
    {
      brush.Color = color;
      pen.Color = color;

      using (Geometry geom = GetPathGeometry(points))
      {
        g.FillGeometry(geom, brush);
        g.DrawGeometry(geom, pen, 1);
      }
    }

    private PathGeometry GetPathGeometry(Point[] points)
    {
      PathGeometry result = null;
      if (points.Length > 1)
      {
        result = new PathGeometry(g.Factory);
        GeometrySink geom = result.Open();
        geom.SetFillMode(FillMode.Winding);
        geom.BeginFigure(points[0], FigureBegin.Filled);

        for (int i = 1; i < points.Length; i++)
        {
          geom.AddLine(points[i]);
        }

        geom.EndFigure(FigureEnd.Closed);
        geom.Close();
      }
      return result;
    }
  }
}
