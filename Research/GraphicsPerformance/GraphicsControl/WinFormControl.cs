using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GraphicsControl
{
  public class WinFormControl : Control
  {
    public WinFormControl()
    {
      InitializeControl();
    }

    private void InitializeControl()
    {
      Canvas = new CanvasGDIplus();
      UseBuffer = true;
      Utils.Clock.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      Draw(e.Graphics, ClientRectangle);
    }

    protected override void OnResize(EventArgs e)
    {
      Canvas.DoResize(base.OnResize, e, ClientSize);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        Canvas.Dispose();
      }
      base.Dispose(disposing);
    }

    public bool UseBuffer
    {
      get { return DoubleBuffered; }
      set { DoubleBuffered = value; }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
      if(Canvas.PaintBackground) base.OnPaintBackground(e);
    }

    public Canvas Canvas { get; set; }

    private Random rnd = new Random();

    private Color RandomColor()
    {
      int red = rnd.Next(0, 255);
      int green = rnd.Next(0, 255);
      int blue = rnd.Next(0, 255);
      return Color.FromArgb(red, green, blue);
    }

    private string RandomText()
    {
      string result = "";
      for (int i = 0; i < 2; i++)
      {
        result += Convert.ToChar(rnd.Next(65, 90)).ToString() + Convert.ToChar(rnd.Next(65, 90)).ToString()
          + Environment.NewLine;
      }
      return result;
    }

    private int elements = 5;
    private int[] starts = new int[] {1, 1, 1, 1, 1};

    private bool rectangles = true;
    private bool ellipses = true;
    private bool lines = true;
    private bool text = true;
    private bool polygon = true;

    [DefaultValue(true)]
    public bool PaintRectangles 
    {
      get
      {
        return rectangles;
      }
      set
      {
        elements = value ? elements + 1 : elements - 1;
        starts[0] = value ? 1 : 0;
        rectangles = value;
      }
    }

    [DefaultValue(true)]
    public bool PaintEllipses
    {
      get
      {
        return ellipses;
      }
      set
      {
        elements = value ? elements + 1 : elements - 1;
        starts[1] = value ? 1 : 0;
        ellipses = value;
      }
    }

    [DefaultValue(true)]
    public bool PaintLines
    {
      get
      {
        return lines;
      }
      set
      {
        elements = value ? elements + 1 : elements - 1;
        starts[2] = value ? 1 : 0;
        lines = value;
      }
    }

    [DefaultValue(true)]
    public bool PaintText
    {
      get
      {
        return text;
      }
      set
      {
        elements = value ? elements + 1 : elements - 1;
        starts[3] = value ? 1 : 0;
        text = value;
      }
    }

    [DefaultValue(true)]
    public bool PaintPolygon
    {
      get
      {
        return polygon;
      }
      set
      {
        elements = value ? elements + 1 : elements - 1;
        starts[4] = value ? 1 : 0;
        polygon = value;
      }
    }

    private int size = 12;

    private void DoPaintRectangles()
    {
      if (PaintRectangles)
      {
        Rectangle rect;
        int asize = size * elements;

        for (int i = 0; i < this.Width; i += asize)
        {
          for (int j = 0; j < this.Height; j += (size + ElementDensity))
          {
            rect = Rectangle.FromLTRB(i, j, i + size, this.Bottom);
            Canvas.Rectangle(rect, RandomColor());
            ElementCount++;
          }
        }
      }
    }

    private void DoPaintEllipses()
    {
      if (PaintEllipses)
      {
        Rectangle rect;
        int asize = size * elements;
        int start = starts[0] * size;

        for (int i = start; i < this.Width; i += asize)
        {
          for (int j = 0; j < this.Height; j += (size + ElementDensity))
          {
            rect = Rectangle.FromLTRB(i, j, i + size, this.Bottom);
            Canvas.Ellipse(rect, RandomColor());
            ElementCount++;
          }
        }
      }
    }

    private void DoPaintLines()
    {
      if (PaintLines)
      {
        Rectangle rect;
        int asize = size * elements;
        int start = starts[0] * size + starts[1] * size;

        for (int i = start; i < this.Width; i += asize)
        {
          for (int j = 0; j < this.Height; j += (size + ElementDensity))
          {
            rect = new Rectangle(i, j, size, size);

            for (int k = rect.Left; k < rect.Right; k+=2)
            {
              Point top = new Point(k, rect.Top);
              Point bottom = new Point(k, this.Bottom);
              Canvas.Line(top, bottom, RandomColor());
              ElementCount++;
            }
          }
        }
      }
    }

    private void DoPaintText()
    {
      if (PaintText)
      {
        int asize = size * elements;
        int start = starts[0] * size + starts[1] * size + starts[2] * size;

        for (int i = start; i < this.Width; i += asize)
        {
          for (int j = 0; j < this.Height; j += (size + ElementDensity))
          {
            Canvas.Text(new Point(i, j), RandomText(), Color.Black);
            ElementCount++;
          }
        }
      }
    }

    private void DoPaintPolygons()
    {
      if (PaintPolygon)
      {
        Rectangle rect;
        Point top, left, right, bottom;
        int asize = size * elements;
        int start = starts[0] * size + starts[1] * size + starts[2] * size + starts[3] * size;

        for (int i = start; i < this.Width; i += asize)
        {
          for (int j = 0; j < this.Height; j += (size + ElementDensity))
          {
            rect = new Rectangle(i, j, size, size);
            left = new Point(rect.Left, rect.Top + size / 2);
            top = new Point(rect.Left + size / 2, rect.Top);
            right = new Point(rect.Right, rect.Top + size / 2);
            bottom = new Point(rect.Left + size / 2, this.Bottom);
            Canvas.Polygon(RandomColor(), left, top, right, bottom);
            ElementCount++;
          }
        }
      }
    }

    public int ElementDensity { get; set; }

    public int ElementCount { get; set; }

    private void DoPaintElements()
    {
      ElementCount = 0;
      DoPaintRectangles();
      DoPaintEllipses();
      DoPaintLines();
      DoPaintText();
      DoPaintPolygons();
    }

    private void Draw(Graphics g, Rectangle r)
    {
      Canvas.InitCanvas(r, g, this.Handle);
      DoPaintElements();
      Canvas.ShowImage();
      Utils.Update();
    }

  }
}
