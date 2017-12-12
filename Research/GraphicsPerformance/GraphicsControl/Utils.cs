using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace GraphicsControl
{
  public static class Utils
  {
    private static float frameAccumulator;
    private static int frameCount;
    private static float framesPerSecond;


    static public Point RectCenter(Rectangle r)
    {
      return new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
    }

    internal static void Update()
    {
      frameAccumulator += Clock.Update();
      ++frameCount;
      if (frameAccumulator >= 1.0f)
      {
        framesPerSecond = frameCount / frameAccumulator;

        frameAccumulator = 0.0f;
        frameCount = 0;
      }
    }

    public static float FramesPerSecond
    {
      get
      {
        return framesPerSecond;
      }
    }

    private static TClock clock;
    internal static TClock Clock
    {
      get
      {
        if (clock == null)
        {
          clock = new TClock();
        }
        return clock;
      }
    }

    internal class TClock
    {
      private bool isRunning;
      private readonly long frequency;
      private long count;

      public TClock()
      {
        frequency = System.Diagnostics.Stopwatch.Frequency;
      }

      public void Start()
      {
        count = System.Diagnostics.Stopwatch.GetTimestamp();
        isRunning = true;
      }

      public float Update()
      {
        float result = 0.0f;
        if (isRunning)
        {
          long last = count;
          count = System.Diagnostics.Stopwatch.GetTimestamp();
          result = (float)(count - last) / frequency;
        }

        return result;
      }
    }
  }
}
