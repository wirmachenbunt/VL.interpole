#region usings
using System;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using SlimDX;
using D2D = SlimDX.Direct2D;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "d2dGUI",
                Category = "GUI",
                Help = "Template with some gui elements",
                Tags = "c#",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class GUITemplateNode : UserControl, IPluginEvaluate
    {
        #region fields & pins

        [Output("Button")]
        public ISpread<bool> FButtonOut;

        [Output("Check Box")]
        public ISpread<bool> FCheckBoxOut;

        [Output("Text Box")]
        public ISpread<string> FTextBoxOut;

        [Output("Slider")]
        public ISpread<double> FSliderOut;

        [Import()]
        public ILogger FLogger;

        //gui controls
        bool FButtonClick = false;
        CheckBox FCheckBox = new CheckBox();
        TextBox FTextBox = new TextBox();
        TrackBar FSlider = new TrackBar();

        private D2D.Factory m_factory;
        private D2D.WindowRenderTarget m_renderTarget;
        private D2D.LinearGradientBrush m_backBrushEx;
        private D2D.GradientStopCollection m_backBrushGradient;
        bool m_debugMode = false;
        bool m_initialized = false;


        #endregion fields & pins

        #region constructor and init

        public GUITemplateNode()
        {
            //setup the gui
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            InitializeComponent();
        }

        void InitializeComponent()
        {
            //clear controls in case init is called multiple times
            Controls.Clear();

            //add a button
            var button = new Button();
            button.Text = "Bang";
            //set position and size
            button.Bounds = new Rectangle(10, 10, 100, 25);
            button.FlatStyle = FlatStyle.Flat;
            //listen to click event
            button.Click += ButtonClicked;

            //add a checkbox
            FCheckBox.Text = "Toggle";
            FCheckBox.Bounds = new Rectangle(15, 50, 100, 25);
            FCheckBox.FlatStyle = FlatStyle.Flat;

            //add a textbox
            FTextBox.Bounds = new Rectangle(130, 10, 150, 25);
            FTextBox.BorderStyle = BorderStyle.FixedSingle;

            //add a slider
            FSlider.Maximum = 1000;
            FSlider.TickFrequency = 20;
            FSlider.Bounds = new Rectangle(125, 50, 160, 25);

            //add to controls
            Controls.Add(button);
            Controls.Add(FCheckBox);
            Controls.Add(FTextBox);
            Controls.Add(FSlider);

            //Get requested debug level
            D2D.DebugLevel debugLevel = D2D.DebugLevel.None;
            if (m_debugMode) { debugLevel = D2D.DebugLevel.Error; }

            //Create factory object
            m_factory = new D2D.Factory(D2D.FactoryType.SingleThreaded, debugLevel);

            //Create the render target
            m_renderTarget = new D2D.WindowRenderTarget(m_factory, new D2D.WindowRenderTargetProperties()
            {
                Handle = this.Handle,
                PixelSize = this.Size,
                PresentOptions = D2D.PresentOptions.Immediately
            });

            //Create linear gradient brush
            D2D.GradientStop[] gradientStops = new D2D.GradientStop[]
            {
                new D2D.GradientStop(){ Position = 0f, Color = new Color4(Color.LightGray) },
                new D2D.GradientStop(){ Position = 1f, Color = new Color4(Color.LightSteelBlue) }
            };
            m_backBrushGradient = new D2D.GradientStopCollection(m_renderTarget, gradientStops);
            m_backBrushEx = new D2D.LinearGradientBrush(
                m_renderTarget,
                m_backBrushGradient,
                new D2D.LinearGradientBrushProperties()
                {
                    StartPoint = new PointF(0, this.Height),
                    EndPoint = new PointF(0, 0)
                });

            //Update initialization flag
            m_initialized = true;
        }

        //called if the button is clicked
        void ButtonClicked(object sender, EventArgs e)
        {
            if (sender is Button)
            {
                //set button click to true to read it in Evaluate()
                FButtonClick = true;
            }
        }

        #endregion constructor and init

        protected override void OnPaint(PaintEventArgs e)
        {

            
            base.OnPaint(e);

            if (m_initialized)
            {
                m_renderTarget.BeginDraw();
                try
                {
                    m_renderTarget.Clear(new Color4(this.BackColor));

                    //Perform all Direct2D rendering here
                    m_renderTarget.FillRectangle(
                        m_backBrushEx,
                        new Rectangle(new Point(), this.Size));
                }
                finally
                {
                    m_renderTarget.EndDraw();
                }
            }
            else
            {
                //e.Graphics.FillRectangle(m_backBrushGdi, e.ClipRectangle);
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
                    m_backBrushEx.StartPoint = new PointF(0, this.Height);
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

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            m_renderTarget.BeginDraw();
            try
            {
                m_renderTarget.Clear(new Color4(this.BackColor));

                //Perform all Direct2D rendering here
                m_renderTarget.FillRectangle(
                    m_backBrushEx,
                    new Rectangle(new Point(), this.Size));
            }
            finally
            {
                m_renderTarget.EndDraw();
            }
        }
    }
}