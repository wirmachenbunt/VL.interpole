using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V2;

using SlimDX;
using D2D = SlimDX.Direct2D;
using DWrite = SlimDX.DirectWrite;

using VVVV.Utils.VMath;
using SlimDX.DirectWrite;



namespace VVVV.Nodes
{
    partial class Interpole : IPluginEvaluate
    {
        #region variables

        private D2D.Factory m_factory;
        private D2D.WindowRenderTarget m_renderTarget;
        private D2D.LinearGradientBrush m_backBrushEx;
        private D2D.GradientStopCollection m_backBrushGradient;
        bool m_debugMode = false;
        bool m_initialized = false;

        private D2D.SolidColorBrush grayBrushD2D;
        private D2D.SolidColorBrush redBrushD2D;
        private D2D.SolidColorBrush brightGrayBrushD2D;
        private D2D.SolidColorBrush clipColorD2D;

        private DWrite.Factory dwFactory;
        private DWrite.TextFormat _textFormat;
        private DWrite.TextLayout _textLayout;
        private DWrite.TextFormat _textFormatFlow;


        #endregion

        public void InitD2DObjects()
        {
            grayBrushD2D = new D2D.SolidColorBrush(m_renderTarget, new Color4(Color.Gray));
            brightGrayBrushD2D = new D2D.SolidColorBrush(m_renderTarget, new Color4(0.2f, 0.2f, 0.2f));
            redBrushD2D = new D2D.SolidColorBrush(m_renderTarget, new Color4(Color.Red));
            clipColorD2D = new D2D.SolidColorBrush(m_renderTarget, new Color4(Color.AliceBlue));

            //text
            dwFactory = new SlimDX.DirectWrite.Factory(SlimDX.DirectWrite.FactoryType.Isolated);

            _textFormat = dwFactory.CreateTextFormat("Calibri", FontWeight.Regular, SlimDX.DirectWrite.FontStyle.Normal, FontStretch.Normal, 10, "en-us");
            _textFormat.TextAlignment = TextAlignment.Leading;
            _textFormat.ParagraphAlignment = ParagraphAlignment.Near;

            _textFormatFlow = dwFactory.CreateTextFormat(
    "Arial",
    DWrite.FontWeight.Normal, DWrite.FontStyle.Normal, DWrite.FontStretch.Normal,
    20f, "de-de");
            _textFormatFlow.TextAlignment = DWrite.TextAlignment.Leading;
            _textFormatFlow.ParagraphAlignment = DWrite.ParagraphAlignment.Near;

            _textLayout = dwFactory.CreateTextLayout(
                "text", _textFormatFlow,
                400f, 100f);
            
        }

        public void Draw2D2()
        {

            m_renderTarget.BeginDraw();
            try
            {
                m_renderTarget.Clear(new Color4(0.1f, 0.1f, 0.1f));



                if (initialized)
                {

                    m_renderTarget.Transform = Matrix3x2.Translation(new PointF(StagePos.X, StagePos.Y));
                    DrawClipsD2D();
                    DrawPlayBarD2D();
                    DrawPlayHeadD2D();
                }

            }
            finally
            {
                m_renderTarget.EndDraw();
            }


        }

        public void DrawClipsD2D()
        {

            foreach (Clip clip in clipList)
            {
                clipColorD2D = new D2D.SolidColorBrush(m_renderTarget, new Color4(Color.FromName(clip.Color)));

                if (clip.FadeIn > 0 || clip.FadeOut > 0) // optimization, only draw fade clip when fade is used

                {
                    int fadeIn = clip.FadeIn / timeZoom;
                    int fadeOut = clip.FadeOut / timeZoom;
                    m_renderTarget.FillRectangle(grayBrushD2D, clip.Bounds);
                    PointF[] points = {
                        new Point(clip.Bounds.X, clip.Bounds.Y+20),
                        new Point(clip.Bounds.X+fadeIn, clip.Bounds.Y),
                        new Point(clip.Bounds.Width - fadeOut + clip.Bounds.X, clip.Bounds.Y),
                        new Point(clip.Bounds.Width + clip.Bounds.X, clip.Bounds.Y+20) };


                    using (D2D.PathGeometry geometry = new D2D.PathGeometry(m_factory))
                    using (D2D.SimplifiedGeometrySink sink = geometry.Open())
                    {

                        sink.BeginFigure(points[0], D2D.FigureBegin.Filled);

                        sink.AddLines(points);

                        sink.EndFigure(D2D.FigureEnd.Closed);

                        sink.Close();

                        m_renderTarget.FillGeometry(geometry, clipColorD2D);

                    }

                }
                else
                {
                    m_renderTarget.FillRectangle(clipColorD2D, clip.Bounds);
                }

                /*
                _textLayout = dwFactory.CreateTextLayout(
                clip.Name, _textFormatFlow,
                400f, 100f);
                */

                

                Point topRight = new Point(clip.Bounds.X,clip.Bounds.Y);
                m_renderTarget.DrawTextLayout(topRight, _textLayout, redBrushD2D, D2D.DrawTextOptions.Clip);

                /*
                Color ClipColor = Color.FromName(clip.Color);
                SolidBrush ClipBrush = new SolidBrush(ClipColor);


                //e.Graphics.FillRectangle(grayBrush, clip.Bounds);

                int fadeIn = clip.FadeIn / timeZoom;
                int fadeOut = clip.FadeOut / timeZoom;

            


                //e.Graphics.FillPolygon(ClipBrush, points);


                string text = clip.Name; // create text
                SizeF stringSize = new SizeF();
                //stringSize = e.Graphics.MeasureString(text, clipFont, 400);

                Point center = new Point(clip.Bounds.Location.X, clip.Bounds.Y - Convert.ToInt16(stringSize.Height));

                Rectangle Bounds = new Rectangle(
                center,
                new Size(Convert.ToInt32(stringSize.Width + 1),
                Convert.ToInt32(stringSize.Height + 1)));

                //e.Graphics.DrawString(text, clipFont, textBrush, Bounds, format); //text
                */
            }
        }

        public void DrawPlayHeadD2D()
        {
            int pos = playPosFrames / timeZoom;


            PointF[] points = { new Point(-7 + pos, 20 - StagePos.Y), new Point(7 + pos, 20 - StagePos.Y), new Point(0 + pos, 30 - StagePos.Y) }; //play head triangle
            using (D2D.PathGeometry geometry = new D2D.PathGeometry(m_factory))
            using (D2D.SimplifiedGeometrySink sink = geometry.Open())
            {

                sink.BeginFigure(points[0], D2D.FigureBegin.Filled);

                sink.AddLines(points);

                sink.EndFigure(D2D.FigureEnd.Closed);

                sink.Close();

                m_renderTarget.FillGeometry(geometry, redBrushD2D);

            }
            m_renderTarget.DrawLine(grayBrushD2D, new Point(pos, 30 - StagePos.Y), new Point(pos, this.Height - StagePos.Y));
        }

        public void DrawPlayBarD2D()
        {
            myplaybar.Bounds = new Rectangle(new Point(0 - StagePos.X, 0 - StagePos.Y), new Size(this.Width, 30)); //update playbar bounds


            m_renderTarget.FillRectangle(grayBrushD2D, myplaybar.Bounds); // background

            //DrawLoopRegion(e);

            DrawRulerD2D();

            //DrawTimeCode(e);
        }

        public void DrawRulerD2D()
        {
            int rulerSteps = 1; //in seconds
            if (timeZoom > 1) rulerSteps = 5;
            if (timeZoom > 4) rulerSteps = 10;
            if (timeZoom > 10) rulerSteps = 20;

            double startRulerPos = Math.Abs(StagePos.X) * timeZoom;
            startRulerPos = (startRulerPos / Framerate[0]) / rulerSteps;
            startRulerPos = Math.Truncate(startRulerPos) * rulerSteps; // hier ist die stelle für die sekunden

            double endRulerPos = ((this.Width + Math.Abs(StagePos.X)) * timeZoom) / Framerate[0];

            for (int i = Convert.ToInt32(startRulerPos); i < Convert.ToInt32(endRulerPos); i += rulerSteps)
            {
                int pos = (i * Framerate[0]) / timeZoom;
                int size = 30;
                int height = 5;
                int y = 25;
                int thickness = 1;
                string timecode = Frames2Timecode(i * Framerate[0], Framerate[0], true);
                string time = (i % 60).ToString() + "s";
                if (time == "0s")
                {
                    time = timecode + "s";
                    size = 200;
                    height = 12;
                    thickness = 3;
                    y = 17;
                }
                //stringSize = e.Graphics.MeasureString(time, rulerFont, 1000);


                //e.Graphics.DrawString(time, rulerFont, whiteBrush, new Rectangle(new Point(pos - Convert.ToInt32(stringSize.Width / 2) + 1, 5 - StagePos.Y), new Size(size, 50)), format); //draw seconds
                m_renderTarget.FillRectangle(brightGrayBrushD2D, new Rectangle(new Point(pos - (thickness / 2), y - StagePos.Y), new Size(thickness, height)));
            }

            //FLogger.Log(LogType.Debug, countBars.ToString());

        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (m_initialized)
            {
                // for workspace hittest
                myworkspace.Bounds = new Rectangle(new Point(0 - StagePos.X, 0 - StagePos.Y), new Size(this.Width, this.Height));
                Draw2D2();

                //FLogger.Log(LogType.Debug, "OnPaint");
            }

        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if ((this.Width > 0) && (this.Height > 0))
            {
                if (m_initialized)
                {
                    //Resize render target
                    m_renderTarget.Resize(this.Size);

                    //FLogger.Log(LogType.Debug, "OnResize");
                }
            }
        }

        private void UnloadGraphics()
        {
            if (m_backBrushEx != null) { m_backBrushEx.Dispose(); }
            if (m_backBrushGradient != null) { m_backBrushGradient.Dispose(); }
            if (m_renderTarget != null) { m_renderTarget.Dispose(); }
            if (m_factory != null) { m_factory.Dispose(); }

            m_backBrushEx = null;
            m_backBrushGradient = null;
            m_renderTarget = null;
            m_factory = null;
        }

    }
}
