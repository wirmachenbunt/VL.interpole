using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GraphicsControl;

namespace WinFormApp
{
  public partial class Form1 : Form
  {
    private long ticks;
    private List<float> frames = new List<float>();

    public Form1()
    {
      InitializeComponent();
    }

    private void rbCanvas_CheckedChanged(object sender, EventArgs e)
    {
      timer1.Enabled = false;
      OnChangeParam();
      if (winFormControl1.Canvas != null)
      {
        winFormControl1.Canvas.Dispose();
      }
      if (rbDirect2D.Checked)
      {
        winFormControl1.Canvas = new CanvasDirect2D();
        winFormControl1.UseBuffer = false;
      }
      else
      {
        winFormControl1.Canvas = new CanvasGDIplus();
        winFormControl1.UseBuffer = true;
      }
      winFormControl1.Canvas.AntiAlias = cbAntiAlias.Checked;
      winFormControl1.Invalidate();
      timer1.Enabled = true;
    }


    private void timer1_Tick(object sender, EventArgs e)
    {
      if(Utils.FramesPerSecond > 0) frames.Add(Utils.FramesPerSecond);
      string fps = frames.Count > 0 ? frames.Average().ToString("F2") : "0";
      string seconds = TimeSpan.FromTicks(DateTime.Now.Ticks - ticks).Seconds.ToString();
      this.Text = "FPS: " + fps + " | Elements rendered per frame: " + winFormControl1.ElementCount.ToString() +
        " | Elapsed seconds: " + seconds;
      this.winFormControl1.Invalidate();
    }


    private void Form1_Load(object sender, EventArgs e)
    {
      OnChangeParam();
      timer1.Enabled = true;
    }

    private void cbElements_CheckedChanged(object sender, EventArgs e)
    {
      OnChangeParam();
      CheckBox c = sender as CheckBox;
      switch (c.Name)
      {
        case "cbLine":
          winFormControl1.PaintLines = c.Checked;
          break;
        case "cbEllipse":
          winFormControl1.PaintEllipses = c.Checked;
          break;
        case "cbText":
          winFormControl1.PaintText = c.Checked;
          break;
        case "cbRectangle":
          winFormControl1.PaintRectangles = c.Checked;
          break;
        case "cbPolygon":
          winFormControl1.PaintPolygon = c.Checked;
          break;
        default:
          break;
      }
    }

    private void cbAntiAlias_CheckedChanged(object sender, EventArgs e)
    {
      OnChangeParam();
      winFormControl1.Canvas.AntiAlias = cbAntiAlias.Checked;
      winFormControl1.Invalidate();
    }

    private void tbDensity_Scroll(object sender, EventArgs e)
    {
      OnChangeParam();
      winFormControl1.ElementDensity = tbDensity.Value;
    }

    private void OnChangeParam()
    {
      frames.Clear();
      ticks = DateTime.Now.Ticks;
    }
  }
}
