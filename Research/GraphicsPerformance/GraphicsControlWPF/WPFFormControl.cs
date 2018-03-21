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
using GraphicsControl;
using System.ComponentModel;
using System.Threading;

namespace GraphicsControlWPF
{
  public class WPFFormControl : Control
  {
    static WPFFormControl()
    {
      DefaultStyleKeyProperty.OverrideMetadata(typeof(WPFFormControl), new FrameworkPropertyMetadata(typeof(WPFFormControl)));
    }

    public WPFFormControl()
    {
      InitializeControl();
      CompositionTarget.Rendering += new EventHandler(CompositionTarget_Rendering);
    }

    void CompositionTarget_Rendering(object sender, EventArgs e)
    {
      Utils.Update();
    }

    private void InitializeControl()
    {
      Canvas = new CanvasWPF();
      this.Background = new SolidColorBrush(Colors.Gray);
      Utils.Clock.Start();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
      Draw(drawingContext, new Rect(RenderSize));
    }

    public GraphicsControl.Canvas Canvas { get; set; }

    private Random rnd = new Random();

    private Color RandomColor()
    {
      byte red = (byte)rnd.Next(0, 255);
      byte green = (byte)rnd.Next(0, 255);
      byte blue = (byte)rnd.Next(0, 255);
      return Color.FromArgb(255, red, green, blue);
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
    private int[] starts = new int[] { 1, 1, 1, 1, 1 };

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
        Rect rect;
        int asize = size * elements;

        for (int i = 0; i < RenderSize.Width; i += asize)
        {
          for (int j = 0; j < RenderSize.Height; j += (size + ElementDensity))
          {
            rect = new Rect(i, j, size, RenderSize.Height - j);
            //rect = new Rect(i, j, size, size);
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
        Rect rect;
        int asize = size * elements;
        int start = starts[0] * size;

        for (int i = start; i < RenderSize.Width; i += asize)
        {
          for (int j = 0; j < RenderSize.Height; j += (size + ElementDensity))
          {
            rect = new Rect(i, j, size, RenderSize.Height - j);
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
        Rect rect;
        int asize = size * elements;
        int start = starts[0] * size + starts[1] * size;

        for (int i = start; i < RenderSize.Width; i += asize)
        {
          for (int j = 0; j < RenderSize.Height; j += (size + ElementDensity))
          {
            rect = new Rect(i, j, size, size);

            for (double k = rect.Left; k < rect.Right; k += 2)
            {
              Point top = new Point(k, rect.Top);
              Point bottom = new Point(k, RenderSize.Height);
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

        for (int i = start; i < RenderSize.Width; i += asize)
        {
          for (int j = 0; j < RenderSize.Height; j += (size + ElementDensity))
          {
            Canvas.Text(new Point(i, j), RandomText(), Colors.Black);
            ElementCount++;
          }
        }
      }
    }
 

    private void DoPaintPolygons()
    {
      if (PaintPolygon)
      {
        Rect rect;
        Point top, left, right, bottom;
        int asize = size * elements;
        int start = starts[0] * size + starts[1] * size + starts[2] * size + starts[3] * size;

        for (int i = start; i < RenderSize.Width; i += asize)
        {
          for (int j = 0; j < RenderSize.Height; j += (size + ElementDensity))
          {
            rect = new Rect(i, j, size, size);
            left = new Point(rect.Left, rect.Top + size / 2);
            top = new Point(rect.Left + size / 2, rect.Top);
            right = new Point(rect.Right, rect.Top + size / 2);
            bottom = new Point(rect.Left + size / 2, RenderSize.Height);
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

    private void Draw(DrawingContext g, Rect r)
    {
      Canvas.InitCanvas(r, g);
      DoPaintElements();
      Canvas.ShowImage();
    }
  }
}
