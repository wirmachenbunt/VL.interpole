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

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Clips",
                Category = "GUI",
                Help = "Clip Timeline",
                Tags = "c#",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class Clips : UserControl, IPluginEvaluate
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

        ClipsDrawing p = new ClipsDrawing(); // create AutomataPaint Object

        #endregion fields & pins

        #region constructor and init

        public Clips()
        {
            //setup the gui
            InitializeComponent();
        }

        void InitializeComponent()
        {
            //clear controls in case init is called multiple times
            Controls.Clear();

            Paint += p.PaintAutomata; //paint event
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        

           
        }



        #endregion constructor and init

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            
            //Invalidate();
        }
    }
}